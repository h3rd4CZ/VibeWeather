using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>
/// Which location the app and all its alarms use. The base selection is "Aktuální poloha" or a saved city, chosen by
/// the user; a geofence the device is inside overrides it until the device leaves or the user picks something else.
/// The rules are in <see cref="SelectionRules"/>. <see cref="Changed"/> may be raised off the UI thread.
/// </summary>
public sealed class LocationSelection
{
    const string BasePreference = "selection_base";
    const string OverridePreference = "selection_fence";
    // From before this class: the one selected key, and the device position (whose time wasn't kept).
    const string LegacyKeyPreference = "selected_location";
    const string PositionPreference = "last_device_location";
    const string PositionTimePreference = "last_device_location_at";
    // Set when the device entered or left a geofence after the position was found: it has moved since.
    const string PositionOutdatedPreference = "last_device_location_outdated";
    const string MaxPositionAgePreference = "position_max_age_minutes";

    readonly GeofenceManager geofences;
    readonly DeviceLocationService device;
    readonly ILogger<LocationSelection> logger;
    readonly Lock gate = new();
    readonly SemaphoreSlim positionLock = new(1, 1);
    bool loaded;
    WeatherLocation baseSelection = SelectionRules.Device;
    string? overrideId;
    DevicePosition? position;
    bool positionOutdated;
    // Geofences the device was inside at the last change, to notice it entering or leaving one.
    HashSet<string> insideIds = [];
    // Key of the location in effect after the last change, to tell whether the next one changes it.
    string effectiveKey = WeatherLocation.DeviceKey;

    public LocationSelection(GeofenceManager geofences, DeviceLocationService device, ILogger<LocationSelection> logger)
    {
        this.geofences = geofences;
        this.device = device;
        this.logger = logger;
        geofences.Changed += OnGeofencesChanged;
    }

    /// <summary>Another place came into effect (not raised for a new position under "Aktuální poloha").</summary>
    public event Action? Changed;

    /// <summary>Key of the location in effect; <see cref="WeatherLocation.DeviceKey"/> while "Aktuální poloha" is.</summary>
    public string CurrentKey
    {
        get { lock (gate) { Load(); return effectiveKey; } }
    }

    /// <summary>The location in effect without asking for a position (the stored one stands in); null while none is known.</summary>
    public WeatherLocation? Current
    {
        get { lock (gate) { Load(); return SelectionRules.Effective(baseSelection, Fence, position); } }
    }

    /// <summary>The user's choice beneath any geofence: "Aktuální poloha" or a saved city.</summary>
    public WeatherLocation Base
    {
        get { lock (gate) { Load(); return baseSelection; } }
    }

    /// <summary>The last device position, however old.</summary>
    public DevicePosition? StoredPosition
    {
        get { lock (gate) { Load(); return position; } }
    }

    /// <summary>
    /// How long a device position is used without asking for a new one, chosen in Settings. Read from Preferences
    /// each time, so alarms running in another process follow a change too.
    /// </summary>
    public TimeSpan MaxPositionAge
    {
        get
        {
            var minutes = Preferences.Default.Get(MaxPositionAgePreference, (int)SelectionRules.DefaultMaxPositionAge.TotalMinutes);
            var age = TimeSpan.FromMinutes(minutes);
            return SelectionRules.PositionAgeChoices.Contains(age) ? age : SelectionRules.DefaultMaxPositionAge;
        }
    }

    public void SetMaxPositionAge(TimeSpan age) =>
        Preferences.Default.Set(MaxPositionAgePreference, (int)age.TotalMinutes);

    /// <summary>
    /// Fix time of the position in effect when it's older than <see cref="MaxPositionAge"/>, or the device has entered
    /// or left a geofence since; null when current, or a city or geofence is in effect.
    /// </summary>
    public DateTimeOffset? StaleSince
    {
        get
        {
            lock (gate)
            {
                Load();
                return Fence is null && baseSelection.IsDevice && position is { } p
                    ? SelectionRules.Resolve(p, DateTimeOffset.UtcNow, MaxPositionAge, positionOutdated)!.StaleSince
                    : null;
            }
        }
    }

    // The override geofence while it exists.
    GeofencePlace? Fence => overrideId is null ? null : geofences.Places.FirstOrDefault(g => g.Id == overrideId);

    /// <summary>
    /// The location to load, for the UI and alarms alike. For "Aktuální poloha" (rule 7): the stored position if younger
    /// than <see cref="MaxPositionAge"/> and no geofence was entered or left since, else a new one, else the stored one marked stale; null
    /// only when no position was ever known.
    /// In the <paramref name="background"/> (alarms) it needs "Povolit vždy" and never prompts; in the foreground it may
    /// ask for location permission.
    /// </summary>
    public async Task<ResolvedLocation?> ResolveAsync(bool background, CancellationToken cancellationToken = default)
    {
        WeatherLocation? place;
        lock (gate)
        {
            Load();
            place = Fence?.Place ?? (baseSelection.IsDevice ? null : baseSelection);
        }
        ResolvedLocation? resolved;
        if (place is not null) resolved = new ResolvedLocation(place, null);
        else
        {
            var (current, outdated) = await RefreshPositionAsync(background, requestPermission: !background, cancellationToken);
            resolved = SelectionRules.Resolve(current, DateTimeOffset.UtcNow, MaxPositionAge, outdated);
        }
        logger.LogInformation("Location in effect ({Mode}): {Name} {Lat:0.000},{Lon:0.000}, stale since {Since}",
            background ? "background" : "foreground", resolved?.Location.Name, resolved?.Location.Latitude,
            resolved?.Location.Longitude, resolved?.StaleSince);
        return resolved;
    }

    /// <summary>The device position for Settings' first row, by the same <see cref="MaxPositionAge"/> rule; never prompts.</summary>
    public async Task<WeatherLocation?> DevicePositionAsync() =>
        (await RefreshPositionAsync(background: false, requestPermission: false, CancellationToken.None)).Position?.Location;

    /// <summary>Rule 3: "Aktuální poloha" or a city becomes the base selection, over any geofence. Returns whether another place came into effect.</summary>
    public bool Select(WeatherLocation location) => Update(() =>
    {
        baseSelection = location.IsDevice ? SelectionRules.Device : location;
        overrideId = null;
    });

    /// <summary>Rule 4: the geofence overrides the base selection, if the device is inside it. Returns whether another place came into effect.</summary>
    public bool SelectFence(GeofencePlace fence) =>
        geofences.IsInside(fence.Id) && Update(() => overrideId = fence.Id);

    /// <summary>Rule 1: the geofences just entered, also entries repeated after a reboot.</summary>
    public void OnFencesEntered(IReadOnlyList<GeofencePlace> entered)
    {
        if (SelectionRules.OverrideAfterEnter(entered) is { } id) Update(() => overrideId = id);
    }

    /// <summary>Rule 5: a removed saved city stops being the base selection; "Aktuální poloha" takes its place.</summary>
    public void ForgetCity(string key) => Update(() =>
    {
        if (!baseSelection.IsDevice && baseSelection.Key == key) baseSelection = SelectionRules.Device;
    });

    /// <summary>Gives the base city a new name (e.g. Czech instead of English), matched by key.</summary>
    public void RenameBase(IReadOnlyDictionary<string, WeatherLocation> byKey) => Update(() =>
    {
        if (!baseSelection.IsDevice && byKey.TryGetValue(baseSelection.Key, out var renamed))
            baseSelection = renamed with { IsDevice = false };
    });

    // Rule 2: the override geofence was left, removed, or inside states were forgotten. Entering or leaving any
    // geofence also means the device moved, so the stored position no longer counts as current (rule 7).
    void OnGeofencesChanged() => Update(() =>
    {
        var inside = geofences.InsidePlaces;
        overrideId = SelectionRules.OverrideAfterChange(overrideId, inside);
        var ids = inside.Select(g => g.Id).ToHashSet();
        if (ids.SetEquals(insideIds)) return;
        insideIds = ids;
        positionOutdated = true;
        Preferences.Default.Set(PositionOutdatedPreference, true);
    });

    // Applies a change and stores it; raises Changed (outside the lock) when another place came into effect.
    bool Update(Action change)
    {
        bool changed;
        lock (gate)
        {
            Load();
            change();
            Store();
            var key = Fence?.Place.Key ?? baseSelection.Key;
            changed = key != effectiveKey;
            effectiveKey = key;
        }
        if (changed)
        {
            logger.LogInformation("Selected location is now {Key}", CurrentKey);
            Changed?.Invoke();
        }
        return changed;
    }

    // The position by rule 7, and whether it's outdated by a geofence move with no newer one found.
    /// <summary>
    /// Asks for a new device position now, whatever its age (Settings' refresh button); may prompt for permission.
    /// Returns the new position, or null when none newer than the stored one came.
    /// </summary>
    public async Task<DevicePosition?> RefreshPositionNowAsync()
    {
        var before = StoredPosition;
        var (current, _) = await RefreshPositionAsync(background: false, requestPermission: true, CancellationToken.None, force: true);
        return current is not null && current != before ? current : null;
    }

    async Task<(DevicePosition? Position, bool Outdated)> RefreshPositionAsync(bool background, bool requestPermission,
        CancellationToken cancellationToken, bool force = false)
    {
        await positionLock.WaitAsync(cancellationToken);
        try
        {
            DevicePosition? stored;
            bool outdated;
            lock (gate)
            {
                Load();
                (stored, outdated) = (position, positionOutdated);
            }
            var maxAge = MaxPositionAge;
            if (!force && !SelectionRules.NeedsLookup(stored, DateTimeOffset.UtcNow, maxAge, outdated)) return (stored, false);
            // Forced: no cached fix counts, so a new one is asked for.
            var found = await device.GetCurrentAsync(background, requestPermission, stored?.Location,
                force ? TimeSpan.Zero : maxAge, force || outdated ? stored?.At : null, cancellationToken);
            var newer = SelectionRules.Newer(stored, found);
            if (newer is null || newer == stored) return (stored, outdated);
            StorePosition(newer);
            return (newer, false);
        }
        finally
        {
            positionLock.Release();
        }
    }

    // Callers hold the gate.
    void Load()
    {
        if (loaded) return;
        loaded = true;
        position = LoadPosition();
        positionOutdated = Preferences.Default.Get(PositionOutdatedPreference, false);
        insideIds = geofences.InsidePlaces.Select(g => g.Id).ToHashSet();
        var json = Preferences.Default.Get<string?>(BasePreference, null);
        if (json is null)
        {
            // First start of this version: take over the one key selected before (rule 6).
            (baseSelection, overrideId) = SelectionRules.Migrate(
                Preferences.Default.Get<string?>(LegacyKeyPreference, null),
                WeatherState.ReadSavedLocations(logger),
                geofences.InsidePlaces);
            Preferences.Default.Remove(LegacyKeyPreference);
        }
        else
        {
            try
            {
                baseSelection = JsonSerializer.Deserialize(json, AppJsonContext.Default.WeatherLocation) ?? SelectionRules.Device;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Stored location selection unreadable, using the device location");
            }
            if (baseSelection.IsDevice) baseSelection = SelectionRules.Device;
            overrideId = Preferences.Default.Get<string?>(OverridePreference, null);
        }
        // The geofence may have been left or removed while nothing listened (rule 2).
        overrideId = SelectionRules.OverrideAfterChange(overrideId, geofences.InsidePlaces);
        Store();
        effectiveKey = Fence?.Place.Key ?? baseSelection.Key;
    }

    void Store()
    {
        Preferences.Default.Set(BasePreference, JsonSerializer.Serialize(baseSelection, AppJsonContext.Default.WeatherLocation));
        if (overrideId is null) Preferences.Default.Remove(OverridePreference);
        else Preferences.Default.Set(OverridePreference, overrideId);
    }

    DevicePosition? LoadPosition()
    {
        var json = Preferences.Default.Get<string?>(PositionPreference, null);
        if (json is null) return null;
        try
        {
            var stored = JsonSerializer.Deserialize(json, AppJsonContext.Default.WeatherLocation);
            if (stored is null) return null;
            // Saved before CityName existed, it may still read "Hlavní město Praha".
            stored = stored with { Name = DeviceLocationService.CityName(stored.Name) ?? stored.Name, IsDevice = true };
            // Saved before its time was kept: as old as can be, so the next position found replaces it.
            var at = new DateTimeOffset(Preferences.Default.Get(PositionTimePreference, 0L), TimeSpan.Zero);
            return new DevicePosition(stored, at);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    void StorePosition(DevicePosition value)
    {
        lock (gate)
        {
            position = value;
            positionOutdated = false;
            Preferences.Default.Remove(PositionOutdatedPreference);
            Preferences.Default.Set(PositionPreference, JsonSerializer.Serialize(value.Location, AppJsonContext.Default.WeatherLocation));
            Preferences.Default.Set(PositionTimePreference, value.At.UtcTicks);
        }
    }
}
