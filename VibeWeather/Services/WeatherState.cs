using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeWeather.Data;
using VibeWeather.Services.Diagnostics;

namespace VibeWeather.Services;

/// <summary>
/// App-wide weather state: saved locations, the selected location, its forecast and loading status.
/// Components subscribe to <see cref="Changed"/>, which may be raised off the UI thread.
/// </summary>
public sealed class WeatherState
{
    readonly IWeatherProvider provider;
    readonly LocationSelection selection;
    readonly GeofenceManager geofences;
    readonly ILocationSearch search;
    readonly DisplaySettings display;
    readonly ErrorReporter errors;
    readonly ILogger<WeatherState> logger;

    public WeatherState(IWeatherProvider provider, LocationSelection selection, GeofenceManager geofences, ILocationSearch search,
        DisplaySettings display, ErrorReporter errors, WeatherSourceSettings source, ILogger<WeatherState> logger)
    {
        this.provider = provider;
        this.selection = selection;
        this.geofences = geofences;
        this.search = search;
        this.display = display;
        this.errors = errors;
        this.logger = logger;
        geofences.Changed += OnGeofencesChanged;
        selection.Changed += OnSelectionChanged;
        source.Changed += OnSourceChanged;
        display.Changed += OnDisplayChanged;
    }

    // Other units or time format: show the same forecast again, no download needed.
    void OnDisplayChanged()
    {
        if (forecast is { } shown) View = new ForecastView(shown, display.Units);
        Notify();
    }

    // Another weather service (or key): drop what the old one said and load the forecast again.
    void OnSourceChanged()
    {
        Summaries = new Dictionary<string, LocationSummary>();
        Notify();
        if (started) _ = RefreshAsync();
    }

    /// <summary>How many of the most recently added locations Settings shows.</summary>
    public const int RecentLocationCount = 4;

    const string SavedLocationsPreference = "saved_locations";
    static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    // Starting list until the user adds or removes locations.
    static readonly WeatherLocation[] DefaultLocations =
    [
        new("Oslo", "Norsko", 59.9139, 10.7522),
        new("Lisabon", "Portugalsko", 38.7223, -9.1393),
        new("Tokio", "Japonsko", 35.6762, 139.6503),
    ];

    readonly SemaphoreSlim refreshLock = new(1, 1);
    List<WeatherLocation>? saved;
    // The forecast behind View, to rebuild it when the units change.
    Forecast? forecast;
    DateTime lastUpdatedUtc;
    bool started;

    public event Action? Changed;

    /// <summary>Key of the location in effect (see <see cref="LocationSelection"/>); <see cref="WeatherLocation.DeviceKey"/> means "where I am".</summary>
    public string SelectedKey => selection.CurrentKey;

    /// <summary>The location in effect without resolving GPS: the last known device position stands in for "Aktuální poloha".</summary>
    public WeatherLocation? SelectedLocation => selection.Current;

    /// <summary>Last known device position, or null if none is known yet. Settings lists it first.</summary>
    public WeatherLocation? DeviceLocation => selection.StoredPosition?.Location;

    /// <summary>"poloha z 14:05" while "Aktuální poloha" is in effect and its position is too old to count as current; else null.</summary>
    public string? PositionNote => selection.StaleSince is { } since ? PositionAge(since) : null;

    /// <summary>"poloha z 14:05": when the last device position was found, however recent; null while none is known.</summary>
    public string? DevicePositionTime => selection.StoredPosition is { } position ? PositionAge(position.At) : null;

    string PositionAge(DateTimeOffset since)
    {
        // Stored before the time was kept, its age is unknown.
        if (since.UtcTicks == 0) return "poloha může být zastaralá";
        var local = since.ToLocalTime().DateTime;
        var day = local.Date == DateTime.Today ? "" : local.ToString("d. M. ", Czech.Culture);
        return $"poloha z {day}{display.Units.Time(local)}";
    }

    /// <summary>Raised when another place comes into effect: picked by the user, or a geofence entered or left.</summary>
    public event Action? SelectionChanged;

    public ForecastView? View { get; private set; }
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Set when "Aktuální poloha" is shown with a position too old to count as current ("Poloha z 14:05").</summary>
    public string? Notice { get; private set; }

    public IReadOnlyDictionary<string, LocationSummary> Summaries { get; private set; } = new Dictionary<string, LocationSummary>();

    /// <summary>Saved locations, most recently added first.</summary>
    public IReadOnlyList<WeatherLocation> SavedLocations => saved ??= LoadSavedLocations();

    public IReadOnlyList<WeatherLocation> RecentLocations => SavedLocations.Take(RecentLocationCount).ToList();

    /// <summary>Geofenced places the device is inside right now.</summary>
    public IReadOnlyList<GeofencePlace> InsideGeofences => geofences.InsidePlaces;

    /// <summary>Loads the forecast on first use; later calls do nothing.</summary>
    public Task EnsureStartedAsync()
    {
        if (started) return Task.CompletedTask;
        started = true;
        _ = RenameToCzechAsync();
        return RefreshAsync();
    }

    const string CzechNamesPreference = "location_names_cs";

    /// <summary>
    /// Once: places saved while the app was English ("Prague", "Czechia") get their Czech names from the place search,
    /// matched by position. Coordinates stay as they are, so keys and the selection don't change. Tried again on the
    /// next start if the search fails.
    /// </summary>
    async Task RenameToCzechAsync()
    {
        if (Preferences.Default.Get(CzechNamesPreference, false)) return;
        try
        {
            var places = SavedLocations.Concat(geofences.Places.Select(g => g.Place)).DistinctBy(l => l.Key).ToList();
            var renamed = new Dictionary<string, WeatherLocation>();
            foreach (var place in places)
            {
                var match = (await search.SearchAsync(place.Name)).FirstOrDefault(r =>
                    Math.Abs(r.Latitude - place.Latitude) < 0.05 && Math.Abs(r.Longitude - place.Longitude) < 0.05);
                if (match is not null && (match.Name != place.Name || match.Region != place.Region))
                    renamed[place.Key] = place with { Name = match.Name, Region = match.Region };
            }
            if (renamed.Count > 0)
            {
                StoreSavedLocations(SavedLocations.Select(l => renamed.GetValueOrDefault(l.Key, l)).ToList());
                geofences.RenamePlaces(renamed);
                selection.RenameBase(renamed);
                // The header shows the forecast's own copy of the name.
                if (renamed.ContainsKey(SelectedKey)) _ = RefreshAsync();
            }
            Preferences.Default.Set(CzechNamesPreference, true);
        }
        catch (WeatherProviderException ex)
        {
            logger.LogInformation(ex, "Renaming saved places to Czech failed; trying again on the next start");
        }
    }

    public Task RefreshIfStaleAsync() =>
        started && DateTime.UtcNow - lastUpdatedUtc > StaleAfter ? RefreshAsync() : Task.CompletedTask;

    /// <summary>Makes "Aktuální poloha" or a saved city the weather location, also over a geofence the device is in.</summary>
    public async Task SelectAsync(WeatherLocation location)
    {
        // Another place reloads through OnSelectionChanged; picking the same one again refreshes it.
        if (!selection.Select(location) && started) await RefreshAsync();
    }

    /// <summary>Makes a geofence the device is inside the weather location.</summary>
    public async Task SelectFenceAsync(GeofencePlace fence)
    {
        if (!selection.SelectFence(fence) && started) await RefreshAsync();
    }

    void OnSelectionChanged()
    {
        SelectionChanged?.Invoke();
        Notify();
        // Before the first load (e.g. the app is starting from a notification) the start-up load picks this up.
        if (!started) return;
        View = null;
        forecast = null;
        _ = RefreshAsync();
    }

    /// <summary>Raised to bring a forecast tab to the front, e.g. from a notification. The argument is its route.</summary>
    public event Action<string>? ShowForecastRequested;

    /// <summary>
    /// Opened from a notification about the place with <paramref name="key"/>: shows the tab at <paramref name="route"/>
    /// and makes the place the weather location when it can be one (a saved city, "Aktuální poloha", or a geofence
    /// the device is still inside).
    /// </summary>
    public Task ShowAsync(string key, string route = "")
    {
        RequestRoute(route);
        if (key == SelectedKey) return started ? RefreshAsync() : Task.CompletedTask;
        if (key == WeatherLocation.DeviceKey) return SelectAsync(SelectionRules.Device);
        if (SavedLocations.FirstOrDefault(l => l.Key == key) is { } city) return SelectAsync(city);
        if (geofences.Places.FirstOrDefault(g => g.Place.Key == key) is { } fence) return SelectFenceAsync(fence);
        return Task.CompletedTask;
    }

    /// <summary>Opened from a geofence arrival notification: shows Teď with that geofence, if the device is still inside it.</summary>
    public Task ShowAsync(GeofencePlace fence)
    {
        RequestRoute("");
        return SelectFenceAsync(fence);
    }

    void RequestRoute(string route)
    {
        // Started from a notification, no page listens yet: MainLayout takes the route when it starts.
        if (ShowForecastRequested is null) pendingRoute = route;
        else ShowForecastRequested.Invoke(route);
    }

    string? pendingRoute;

    /// <summary>The page shown, relative to the app's root ("" is Teď); set by the layouts on every navigation.</summary>
    public string CurrentRoute { get; set; } = "";

    /// <summary>Shows the Teď tab, without changing the location or reloading.</summary>
    public void ShowHome() => ShowForecastRequested?.Invoke("");

    /// <summary>A route asked for before any page could show it (see <see cref="ShowAsync(string, string)"/>); null once taken.</summary>
    public string? TakePendingRoute()
    {
        var route = pendingRoute;
        pendingRoute = null;
        return route;
    }

    /// <summary>Saves a location, or moves it to the top if it is already saved.</summary>
    public void AddLocation(WeatherLocation location)
    {
        var list = SavedLocations.Where(l => l.Key != location.Key).Prepend(location with { IsDevice = false }).ToList();
        StoreSavedLocations(list);
    }

    /// <summary>Removes a saved location. If it was the chosen one, "Aktuální poloha" takes its place.</summary>
    public void RemoveLocation(WeatherLocation location)
    {
        StoreSavedLocations(SavedLocations.Where(l => l.Key != location.Key).ToList());
        selection.ForgetCity(location.Key);
    }

    public bool IsSaved(WeatherLocation location) => SavedLocations.Any(l => l.Key == location.Key);

    public async Task RefreshAsync()
    {
        await refreshLock.WaitAsync();
        try
        {
            IsLoading = true;
            Error = null;
            Notify();

            var location = await ResolveSelectedAsync();
            forecast = await provider.GetForecastAsync(location);
            View = new ForecastView(forecast, display.Units);
            lastUpdatedUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Error = errors.Report(ex, "Načítání předpovědi", retry: RefreshAsync).Message;
        }
        finally
        {
            IsLoading = false;
            refreshLock.Release();
            Notify();
        }
    }

    /// <summary>
    /// Settings' refresh button: a new device position now, also when the stored one is recent; the forecast reloads
    /// when "Aktuální poloha" is shown. Returns whether a new position was found.
    /// </summary>
    public async Task<bool> RefreshDevicePositionAsync()
    {
        bool found = await selection.RefreshPositionNowAsync() is not null;
        Notify();
        if (found && started && SelectedKey == WeatherLocation.DeviceKey) await RefreshAsync();
        return found;
    }

    /// <summary>Loads current temperatures for the locations Settings shows.</summary>
    public async Task LoadSummariesAsync()
    {
        // The position from the last hour, else a new one; only with permission already granted, Settings shouldn't prompt on open.
        var device = await selection.DevicePositionAsync();
        Notify();
        // A place can appear more than once (a geofence at a saved city, two geofences at one place); ask once.
        IEnumerable<WeatherLocation> all = [.. InsideGeofences.Select(g => g.Place), .. RecentLocations];
        if (device is not null) all = all.Prepend(device);
        var locations = all.DistinctBy(l => l.Key).ToList();
        try
        {
            var summaries = await provider.GetSummariesAsync(locations);
            Summaries = locations.Zip(summaries).ToDictionary(p => p.First.Key, p => p.Second);
            Notify();
        }
        catch (WeatherProviderException ex)
        {
            // The list still works without temperatures; only interrupt for throttling, which affects the whole app.
            errors.Report(ex, "Načítání teplot pro vaše místa", popup: ex.IsThrottled);
        }
    }

    async Task<WeatherLocation> ResolveSelectedAsync()
    {
        Notice = null;
        var resolved = await selection.ResolveAsync(background: false)
            ?? throw new WeatherProviderException("Vaše poloha není k dispozici. Povolte přístup k poloze nebo přidejte místo v Nastavení.");
        if (resolved.StaleSince is { } since)
        {
            var note = PositionAge(since);
            Notice = char.ToUpper(note[0], Czech.Culture) + note[1..];
        }
        return resolved.Location;
    }

    List<WeatherLocation> LoadSavedLocations() => ReadSavedLocations(logger);

    /// <summary>The saved cities as stored, or the starting list. Also read once by <see cref="LocationSelection"/> to take over the old selection.</summary>
    internal static List<WeatherLocation> ReadSavedLocations(ILogger logger)
    {
        var json = Preferences.Default.Get<string?>(SavedLocationsPreference, null);
        if (json is null) return [.. DefaultLocations];
        try
        {
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListWeatherLocation) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Saved locations unreadable, using defaults");
            return [.. DefaultLocations];
        }
    }

    void StoreSavedLocations(List<WeatherLocation> list)
    {
        saved = list;
        Preferences.Default.Set(SavedLocationsPreference, JsonSerializer.Serialize(list, AppJsonContext.Default.ListWeatherLocation));
        Notify();
    }

    // Entering or leaving a geofence changes the Settings list; fetch temperatures for newly listed places.
    void OnGeofencesChanged()
    {
        Notify();
        if (Summaries.Count > 0 && InsideGeofences.Any(g => !Summaries.ContainsKey(g.Place.Key)))
            _ = LoadSummariesAsync();
    }

    void Notify() => Changed?.Invoke();
}
