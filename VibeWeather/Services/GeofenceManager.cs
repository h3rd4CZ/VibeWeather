using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>
/// Geofence definitions and which of them the device is currently inside. Both are persisted, because
/// transitions can arrive while no UI is running. <see cref="Changed"/> may be raised off the UI thread.
/// </summary>
public sealed class GeofenceManager(IGeofenceMonitor monitor, ILogger<GeofenceManager> logger)
{
    public const int MinRadiusMeters = 100;
    public const int MaxRadiusMeters = 10_000;

    const string PlacesPreference = "geofences";
    const string InsidePreference = "geofences_inside";
    const string QuietUntilPreference = "geofences_quiet_until";
    const string QuietIdsPreference = "geofences_quiet_ids";

    // After re-registering, Android repeats "enter" for fences the device is already in; don't notify for
    // those. Only the re-registered fences are muted, so one created right after app start still announces.
    static readonly TimeSpan QuietAfterRestore = TimeSpan.FromMinutes(2);

    readonly Lock gate = new();
    List<GeofencePlace>? places;
    HashSet<string>? inside;

    public event Action? Changed;

    /// <summary>Most recently entered first; those never entered last.</summary>
    public IReadOnlyList<GeofencePlace> Places
    {
        get { lock (gate) return [.. LoadPlaces().OrderByDescending(p => p.LastEntered ?? DateTimeOffset.MinValue)]; }
    }

    /// <summary>Geofences the device is currently inside.</summary>
    public IReadOnlyList<GeofencePlace> InsidePlaces
    {
        get { lock (gate) return LoadPlaces().Where(p => LoadInside().Contains(p.Id)).ToList(); }
    }

    public bool IsInside(string id)
    {
        lock (gate) return LoadInside().Contains(id);
    }

    /// <summary>"350 m", "2,5 km".</summary>
    public static string FormatRadius(int meters) =>
        meters < 1000 ? $"{meters} m" : $"{Czech.Number(meters / 1000.0, "0.#")} km";

    /// <summary>When the device last arrived: "14:05" today, "12. 10. 14:05" before, null when never entered.</summary>
    public static string? FormatLastEntered(GeofencePlace fence, DisplayUnits units)
    {
        if (fence.LastEntered is not { } at) return null;
        var local = at.ToLocalTime().DateTime;
        var day = local.Date == DateTime.Today ? "" : local.ToString("d. M. ", Czech.Culture);
        return $"{day}{units.Time(local)}";
    }

    /// <summary>
    /// Registers a geofence with the platform and saves it. A place that already has a geofence keeps a single
    /// one, updated to the new radius. Throws <see cref="GeofenceException"/>.
    /// </summary>
    public async Task<GeofencePlace> AddAsync(WeatherLocation place, int radiusMeters)
    {
        List<GeofencePlace> before;
        GeofencePlace geofence;
        lock (gate)
        {
            before = LoadPlaces();
            // Reusing the id makes the platform replace the existing fence instead of adding a second one.
            var existing = before.FirstOrDefault(p => p.Place.Key == place.Key);
            geofence = new GeofencePlace(
                existing?.Id ?? Guid.NewGuid().ToString("N"),
                place with { IsDevice = false },
                Math.Clamp(radiusMeters, MinRadiusMeters, MaxRadiusMeters),
                existing?.LastEntered);
            // Save before registering: the initial "enter" can arrive right away and is ignored for unknown ids.
            StorePlaces([.. before.Where(p => p.Id != geofence.Id), geofence]);
        }
        try
        {
            await monitor.RegisterAsync([geofence]);
        }
        catch
        {
            lock (gate) StorePlaces(before);
            throw;
        }
        Notify();
        return geofence;
    }

    public async Task RemoveAsync(string id)
    {
        try
        {
            await monitor.UnregisterAsync([id]);
        }
        catch (GeofenceException ex)
        {
            // Still forget it locally; a stale platform fence is ignored because its id is unknown.
            logger.LogWarning(ex, "Unregistering geofence {Id} failed", id);
        }
        lock (gate)
        {
            StorePlaces(LoadPlaces().Where(p => p.Id != id).ToList());
            var set = LoadInside();
            if (set.Remove(id)) StoreInside(set);
        }
        Notify();
    }

    /// <summary>
    /// Registers all saved geofences again, as Android requires after a reboot or when geofencing was unavailable.
    /// Does nothing without background location permission.
    /// </summary>
    /// <param name="forgetInside">True after a reboot, when the remembered inside states may be stale.</param>
    public async Task RestoreAsync(bool forgetInside)
    {
        var all = Places;
        if (all.Count == 0 || !await monitor.HasBackgroundPermissionAsync()) return;

        Preferences.Default.Set(QuietIdsPreference, string.Join(',', all.Select(g => g.Id)));
        Preferences.Default.Set(QuietUntilPreference, DateTime.UtcNow.Add(QuietAfterRestore).Ticks);
        if (forgetInside)
        {
            lock (gate) StoreInside([]);
            Notify();
        }
        try
        {
            await monitor.RegisterAsync(all);
        }
        catch (GeofenceException ex)
        {
            logger.LogWarning(ex, "Restoring geofences failed");
        }
    }

    /// <summary>
    /// Records the device entering or leaving geofences. Returns the places newly entered (all of them, for the
    /// location selection) and those of them to announce: not the ones repeating what was known after a restore.
    /// </summary>
    public GeofenceTransition ApplyTransition(IEnumerable<string> ids, bool entered)
    {
        GeofenceTransition result;
        lock (gate)
        {
            var set = LoadInside();
            var known = LoadPlaces().ToDictionary(p => p.Id);
            var reported = ids.Where(known.ContainsKey).ToList();
            var changed = reported.Where(id => entered ? set.Add(id) : set.Remove(id)).ToList();

            bool quietWindow = DateTime.UtcNow.Ticks < Preferences.Default.Get(QuietUntilPreference, 0L);
            var muted = quietWindow
                ? Preferences.Default.Get(QuietIdsPreference, "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
                : [];
            // Real arrivals get the time; a repeat after a restore keeps the earlier one, unless the fence has none yet
            // (the device was already inside when it was created or before arrival times were kept).
            var stamped = entered
                ? reported.Where(id => (changed.Contains(id) && !muted.Contains(id)) || known[id].LastEntered is null).ToHashSet()
                : [];
            if (changed.Count == 0 && stamped.Count == 0) return GeofenceTransition.None;
            if (changed.Count > 0) StoreInside(set);
            if (stamped.Count > 0)
            {
                var now = DateTimeOffset.UtcNow;
                StorePlaces(LoadPlaces().Select(p => stamped.Contains(p.Id) ? p with { LastEntered = now } : p).ToList());
                known = LoadPlaces().ToDictionary(p => p.Id);
            }

            List<GeofencePlace> arrived = entered ? changed.Select(id => known[id]).ToList() : [];
            result = new GeofenceTransition(arrived, arrived.Where(p => !muted.Contains(p.Id)).ToList());
        }
        Notify();
        return result;
    }

    /// <summary>Gives places new names (e.g. in another language), by key; positions and fences stay as they are.</summary>
    public void RenamePlaces(IReadOnlyDictionary<string, WeatherLocation> byKey)
    {
        lock (gate)
        {
            var list = LoadPlaces();
            if (!list.Any(p => byKey.ContainsKey(p.Place.Key))) return;
            StorePlaces(list.Select(p => byKey.TryGetValue(p.Place.Key, out var renamed)
                ? p with { Place = p.Place with { Name = renamed.Name, Region = renamed.Region } }
                : p).ToList());
        }
        Notify();
    }

    /// <summary>Geofencing stopped (e.g. location turned off), so inside states are unknown until it resumes.</summary>
    public void ForgetInside()
    {
        lock (gate) StoreInside([]);
        Notify();
    }

    List<GeofencePlace> LoadPlaces()
    {
        if (places is not null) return places;
        var json = Preferences.Default.Get<string?>(PlacesPreference, null);
        try
        {
            places = json is null ? [] : JsonSerializer.Deserialize(json, AppJsonContext.Default.ListGeofencePlace) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Saved geofences unreadable");
            places = [];
        }
        return places;
    }

    HashSet<string> LoadInside() =>
        inside ??= [.. Preferences.Default.Get(InsidePreference, "").Split(',', StringSplitOptions.RemoveEmptyEntries)];

    void StorePlaces(List<GeofencePlace> list)
    {
        places = list;
        Preferences.Default.Set(PlacesPreference, JsonSerializer.Serialize(list, AppJsonContext.Default.ListGeofencePlace));
    }

    void StoreInside(HashSet<string> set)
    {
        inside = set;
        Preferences.Default.Set(InsidePreference, string.Join(',', set));
    }

    void Notify() => Changed?.Invoke();
}

/// <summary>What a geofence transition changed: the places entered, and those of them to announce.</summary>
public sealed record GeofenceTransition(IReadOnlyList<GeofencePlace> Entered, IReadOnlyList<GeofencePlace> Announce)
{
    public static readonly GeofenceTransition None = new([], []);
}
