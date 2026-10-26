# Location Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The app and all three alarms (precipitation alerts, severe weather warnings, morning summary) always use the same location: "Aktuální poloha" (resolved on every start and alarm run, cached at most 1 hour), a saved city, or a geofence the device is inside — falling back to the user's choice when the geofence is left.

**Architecture:** A new `LocationSelection` singleton owns the base selection (device or city), the geofence override and the device position cache; its decisions are pure functions in `SelectionRules` (tested with a file-based C# test). `WeatherState`, the alarms, the geofence receiver and Settings ask it for the effective location instead of keeping their own. Alarms read the position in the background, which needs the "Povolit vždy" permission; Settings asks for it when an alarm is switched on, through a permission card shared with the geofence editor.

**Tech Stack:** .NET 10 MAUI Blazor Hybrid (Android only), MAUI Essentials (`Geolocation`, `Permissions`, `Preferences`), System.Text.Json source generation, file-based C# test (`dotnet run file.cs`).

**Spec:** `docs/superpowers/specs/2026-10-16-location-selection-design.md`

**Conventions (read first):**
- App root: `C:\dev\misc\scratchpad\mauiapp\VibeWeather` (paths below are relative to it unless they start with `tests/` or `docs/`, which are relative to `C:\dev\misc\scratchpad\mauiapp`).
- All user-visible text is Czech. Comments and log templates stay English. Times shown to the user go through `DisplayUnits` (`display.Units.Time(...)`); dates use `Czech.Culture`.
- Build: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug` → expect `Počet chyb: 0` (Czech MSBuild output; "chyb" = errors).
- Emulator deploy (only in Task 10): from the app root, `dotnet build -t:Run -f net10.0-android -c Debug -p:AdbTarget="-s emulator-5554" -p:RuntimeIdentifier=android-x64`. Never `EmbedAssembliesIntoApk`. Never the physical phone.
- adb: `"$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"` (Git Bash). Package: `com.companyname.vibeweather`.
- The folder is not a git repository: there are no commit steps.
- Files are UTF-8 with LF line endings; keep it that way.

---

## File map

| File | Responsibility |
|---|---|
| Create `Services/SelectionRules.cs` | Pure rules: override on enter/leave, effective location, 1-hour freshness, migration. `DevicePosition`, `ResolvedLocation` records. |
| Create `tests/location-selection-rules.cs` | File-based test for `SelectionRules`. |
| Modify `Services/DeviceLocationService.cs` | Background mode (needs "Povolit vždy", never prompts, 3 s fix), returns the fix time, keeps the name of a nearby position. |
| Modify `Services/GeofenceManager.cs` | `ApplyTransition` returns all entered places plus those to announce. |
| Create `Services/LocationSelection.cs` | Stores base selection, override and device position; resolves the effective location; raises `Changed`. |
| Modify `Services/WeatherState.cs` | Delegates selection to `LocationSelection`; stale-position notice; notification taps by key. |
| Modify `MauiProgram.cs` | Register `LocationSelection`. |
| Modify `Platforms/Android/Geofencing/GeofenceBroadcastReceiver.cs`, `Platforms/Android/MainActivity.cs`, `Platforms/Android/NotificationIntents.cs` | Use the new selection calls. |
| Modify `Services/PrecipAlerts/PrecipAlertMonitor.cs`, `Services/SevereWeather/SevereWeatherMonitor.cs`, `Services/MorningSummary/MorningSummaryService.cs` | Resolve the location in the background on every run. |
| Create `Components/Shared/BackgroundLocationPrompt.razor` + `.razor.css` | The "Povolit vždy" explanation and denied cards. |
| Modify `Components/Pages/GeofenceEditor.razor` + `.razor.css` | Use the shared prompt. |
| Modify `Components/Pages/Settings.razor` | Permission flow for alarms, missing-permission row, geofence row selection, stale note in subtitles. |

---

### Task 1: Selection rules (test first)

**Files:**
- Create: `Services/SelectionRules.cs`
- Test: `tests/location-selection-rules.cs`

- [ ] **Step 1: Write the failing test**

Create `C:\dev\misc\scratchpad\mauiapp\tests\location-selection-rules.cs`:

```csharp
#:include ../VibeWeather/Services/SelectionRules.cs
#:include ../VibeWeather/Data/WeatherModels.cs
// Checks SelectionRules: run with `dotnet run location-selection-rules.cs` from this folder. Exit code 1 on failure.
using VibeWeather.Data;
using VibeWeather.Services;

int failures = 0;
void Check(string what, bool ok) { Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
var praha = new WeatherLocation("Praha", "Česko", 50.08, 14.43);
var brno = new WeatherLocation("Brno", "Česko", 49.19, 16.61);
var home = new GeofencePlace("home", new WeatherLocation("Domov", "Česko", 50.10, 14.50), 300);
var district = new GeofencePlace("district", new WeatherLocation("Čtvrť", "Česko", 50.10, 14.51), 2000);
var office = new GeofencePlace("office", praha, 500);
DevicePosition At(TimeSpan age) => new(new WeatherLocation("Kolín", "Česko", 50.03, 15.20, IsDevice: true), now - age);

// Rule 1
Check("enter: smallest of those entered wins", SelectionRules.OverrideAfterEnter([district, home]) == "home");
Check("enter: nothing entered, no override", SelectionRules.OverrideAfterEnter([]) is null);

// Rule 2
Check("change: still inside the override, it stays", SelectionRules.OverrideAfterChange("home", [home, district]) == "home");
Check("change: left the override, smallest still inside takes over", SelectionRules.OverrideAfterChange("home", [district]) == "district");
Check("change: left the last geofence, no override", SelectionRules.OverrideAfterChange("home", []) is null);
Check("change: no override stays none while inside", SelectionRules.OverrideAfterChange(null, [home]) is null);

// Effective location
Check("effective: geofence over a city", SelectionRules.Effective(brno, home, null) == home.Place);
Check("effective: city", SelectionRules.Effective(brno, null, At(TimeSpan.Zero)) == brno);
Check("effective: device uses the position", SelectionRules.Effective(SelectionRules.Device, null, At(TimeSpan.Zero))?.Name == "Kolín");
Check("effective: device without a position is unknown", SelectionRules.Effective(SelectionRules.Device, null, null) is null);

// Rule 7
Check("fresh under an hour", SelectionRules.IsFresh(At(TimeSpan.FromMinutes(59)), now));
Check("not fresh at an hour", !SelectionRules.IsFresh(At(TimeSpan.FromHours(1)), now));
Check("newer: found replaces older stored", SelectionRules.Newer(At(TimeSpan.FromHours(2)), At(TimeSpan.Zero)) == At(TimeSpan.Zero));
Check("newer: nothing found keeps stored", SelectionRules.Newer(At(TimeSpan.FromHours(2)), null) == At(TimeSpan.FromHours(2)));
Check("newer: older found doesn't replace", SelectionRules.Newer(At(TimeSpan.Zero), At(TimeSpan.FromHours(3))) == At(TimeSpan.Zero));
Check("resolve: fresh is not stale", SelectionRules.Resolve(At(TimeSpan.FromMinutes(5)), now)?.StaleSince is null);
Check("resolve: old is stale since its fix", SelectionRules.Resolve(At(TimeSpan.FromHours(3)), now)?.StaleSince == now - TimeSpan.FromHours(3));
Check("resolve: never known is null", SelectionRules.Resolve(null, now) is null);

// Rule 6
var (b1, o1) = SelectionRules.Migrate("device", [praha, brno], [home]);
Check("migrate: device", b1 == SelectionRules.Device && o1 is null);
var (b2, o2) = SelectionRules.Migrate(brno.Key, [praha, brno], []);
Check("migrate: city", b2 == brno && o2 is null);
var (b3, o3) = SelectionRules.Migrate(home.Place.Key, [praha, brno], [home]);
Check("migrate: geofence inside → device + override", b3 == SelectionRules.Device && o3 == "home");
var (b4, o4) = SelectionRules.Migrate(home.Place.Key, [praha, brno], []);
Check("migrate: geofence outside → device", b4 == SelectionRules.Device && o4 is null);
var (b5, o5) = SelectionRules.Migrate(praha.Key, [praha, brno], [office]);
Check("migrate: city with a geofence inside at it → city + override", b5 == praha && o5 == "office");
var (b6, o6) = SelectionRules.Migrate(null, [praha], []);
Check("migrate: nothing stored → device", b6 == SelectionRules.Device && o6 is null);

Console.WriteLine(failures == 0 ? "All passed" : $"{failures} failed");
return failures == 0 ? 0 : 1;
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `C:\dev\misc\scratchpad\mauiapp\tests`): `dotnet run location-selection-rules.cs`
Expected: build error — `SelectionRules.cs` does not exist (`Could not find file` / `CS0103: The name 'SelectionRules' does not exist`).

- [ ] **Step 3: Write the implementation**

Create `Services/SelectionRules.cs` (no MAUI types here, so the test can compile it alone):

```csharp
using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>A device position and when it was fixed.</summary>
public sealed record DevicePosition(WeatherLocation Location, DateTimeOffset At);

/// <summary>A location to load. <paramref name="StaleSince"/> is the fix time of a device position older than an hour.</summary>
public sealed record ResolvedLocation(WeatherLocation Location, DateTimeOffset? StaleSince);

/// <summary>
/// The rules of <see cref="LocationSelection"/> without storage or platform calls.
/// Spec: docs/superpowers/specs/2026-10-16-location-selection-design.md.
/// </summary>
public static class SelectionRules
{
    /// <summary>A device position younger than this is used without asking for a new one.</summary>
    public static readonly TimeSpan MaxPositionAge = TimeSpan.FromHours(1);

    /// <summary>"Aktuální poloha" as a selection, independent of any position.</summary>
    public static readonly WeatherLocation Device = new("Aktuální poloha", "", 0, 0, IsDevice: true);

    /// <summary>Rule 1: of the geofences just entered, the smallest takes over; null when none was entered.</summary>
    public static string? OverrideAfterEnter(IEnumerable<GeofencePlace> entered) =>
        entered.MinBy(g => g.RadiusMeters)?.Id;

    /// <summary>
    /// Rule 2: the override after the inside states changed. It stays while the device is inside it; once it's left
    /// (or removed), the smallest geofence still inside takes over, or none. No override stays none.
    /// </summary>
    public static string? OverrideAfterChange(string? overrideId, IReadOnlyCollection<GeofencePlace> inside)
    {
        if (overrideId is null || inside.Any(g => g.Id == overrideId)) return overrideId;
        return inside.MinBy(g => g.RadiusMeters)?.Id;
    }

    /// <summary>The location in effect: the override geofence's place, else the base (the position for "Aktuální poloha", null while unknown).</summary>
    public static WeatherLocation? Effective(WeatherLocation baseSelection, GeofencePlace? fence, DevicePosition? position) =>
        fence?.Place ?? (baseSelection.IsDevice ? position?.Location : baseSelection);

    public static bool IsFresh(DevicePosition position, DateTimeOffset now) => now - position.At < MaxPositionAge;

    /// <summary>The newer of the stored and a just found position.</summary>
    public static DevicePosition? Newer(DevicePosition? stored, DevicePosition? found) =>
        found is not null && (stored is null || found.At > stored.At) ? found : stored;

    /// <summary>Rule 7: the position to use, stale when older than an hour; null when no position was ever known.</summary>
    public static ResolvedLocation? Resolve(DevicePosition? position, DateTimeOffset now) =>
        position is null ? null : new(position.Location, IsFresh(position, now) ? null : position.At);

    /// <summary>
    /// Rule 6: base selection and override from the single key stored before. A saved city's key keeps that city,
    /// anything else becomes "Aktuální poloha"; a geofence at that key the device is inside becomes the override.
    /// </summary>
    public static (WeatherLocation Base, string? OverrideId) Migrate(string? legacyKey, IReadOnlyList<WeatherLocation> cities,
        IReadOnlyCollection<GeofencePlace> inside)
    {
        var city = cities.FirstOrDefault(c => !c.IsDevice && c.Key == legacyKey);
        var fence = inside.Where(g => g.Place.Key == legacyKey).MinBy(g => g.RadiusMeters);
        return (city ?? Device, fence?.Id);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `C:\dev\misc\scratchpad\mauiapp\tests`): `dotnet run location-selection-rules.cs`
Expected: every line `ok`, last line `All passed`, exit code 0.

- [ ] **Step 5: Build the app**

Run: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug`
Expected: `Počet chyb: 0`.

---

### Task 2: Device position in the background, with its time

**Files:**
- Modify: `Services/DeviceLocationService.cs` (whole file below)

The only caller today is `WeatherState` (lines 275 and 301); it stops compiling until Task 5 replaces those calls. To keep this task buildable, Step 2 adds a temporary adapter that Task 5 removes.

- [ ] **Step 1: Replace the file**

Replace the whole content of `Services/DeviceLocationService.cs` with:

```csharp
using Microsoft.Extensions.Logging;
using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>The device's position, named with the platform geocoder (Open-Meteo has no reverse geocoding).</summary>
public sealed class DeviceLocationService(IGeofenceMonitor permissions, ILogger<DeviceLocationService> logger)
{
    /// <summary>The name when the geocoder doesn't know the place.</summary>
    public const string UnnamedPlace = "Aktuální poloha";

    static readonly TimeSpan ForegroundFixTimeout = TimeSpan.FromSeconds(10);
    // Alarm receivers get about 10 seconds in all; the forecast download needs the rest.
    static readonly TimeSpan BackgroundFixTimeout = TimeSpan.FromSeconds(3);
    // A new fix this close to the last named position keeps that name, saving a geocoder call.
    const double SameNameKm = 1;

    /// <summary>
    /// The position with the time of its fix: the platform's last known fix when younger than
    /// <see cref="SelectionRules.MaxPositionAge"/>, else a new one (or that older fix when none comes). Null when location
    /// is unavailable or not allowed. In the <paramref name="background"/> it needs "Povolit vždy" and never prompts;
    /// in the foreground <paramref name="requestPermission"/> lets it show the permission prompt.
    /// <paramref name="named"/> is the last position, whose name is kept when the new one is near it.
    /// </summary>
    public async Task<DevicePosition?> GetCurrentAsync(bool background, bool requestPermission, WeatherLocation? named,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await HasPermissionAsync(background, requestPermission)) return null;

            var position = await Geolocation.Default.GetLastKnownLocationAsync();
            if (position is null || DateTimeOffset.UtcNow - position.Timestamp >= SelectionRules.MaxPositionAge)
            {
                var timeout = background ? BackgroundFixTimeout : ForegroundFixTimeout;
                position = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Medium, timeout), cancellationToken)
                    ?? position;
            }
            if (position is null) return null;

            bool near = named is not null && named.Name != UnnamedPlace
                && Location.CalculateDistance(named.Latitude, named.Longitude, position.Latitude, position.Longitude,
                    DistanceUnits.Kilometers) < SameNameKm;
            var (name, region) = near ? (named!.Name, named.Region) : await NameAsync(position.Latitude, position.Longitude);
            return new DevicePosition(
                new WeatherLocation(name, region, position.Latitude, position.Longitude, IsDevice: true), position.Timestamp);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException)
        {
            logger.LogInformation(ex, "Device location unavailable");
            return null;
        }
    }

    /// <summary>Prague comes as its region, "Hlavní město Praha"; the city is "Praha".</summary>
    public static string? CityName(string? name)
    {
        const string Capital = "Hlavní město ";
        return name?.StartsWith(Capital, StringComparison.Ordinal) == true ? name[Capital.Length..] : name;
    }

    async Task<bool> HasPermissionAsync(bool background, bool request)
    {
        if (background) return await permissions.HasBackgroundPermissionAsync();
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted && request)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            return status == PermissionStatus.Granted;
        });
    }

    async Task<(string Name, string Region)> NameAsync(double latitude, double longitude)
    {
        try
        {
#if ANDROID
            var place = await CzechPlacemarkAsync(latitude, longitude);
#else
            var place = (await Geocoding.Default.GetPlacemarksAsync(latitude, longitude))?.FirstOrDefault();
#endif
            var name = CityName(place?.Locality ?? place?.SubAdminArea ?? place?.AdminArea);
            if (!string.IsNullOrWhiteSpace(name))
                return (name, place?.CountryName ?? "");
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Reverse geocoding failed");
        }
        return (UnnamedPlace, "");
    }

#if ANDROID
    /// <summary>
    /// Android's geocoder asked for Czech names ("Praha", "Česko") whatever the phone's language. MAUI's Geocoding
    /// always uses the device language.
    /// </summary>
    static Task<global::Android.Locations.Address?> CzechPlacemarkAsync(double latitude, double longitude) =>
        Task.Run(() =>
        {
            if (!global::Android.Locations.Geocoder.IsPresent) return null;
            var geocoder = new global::Android.Locations.Geocoder(Platform.AppContext, Java.Util.Locale.ForLanguageTag("cs-CZ"));
            // The listener variant (API 33+) does the same off the main thread; this one blocks, hence Task.Run.
#pragma warning disable CS0618, CA1422
            return geocoder.GetFromLocation(latitude, longitude, 1)?.FirstOrDefault();
#pragma warning restore CS0618, CA1422
        });
#endif
}
```

- [ ] **Step 2: Keep `WeatherState` compiling until Task 5**

In `Services/WeatherState.cs`, replace line 275:

```csharp
        DeviceLocation ??= await deviceLocation.GetCurrentAsync(requestPermission: false);
```

with:

```csharp
        DeviceLocation ??= (await deviceLocation.GetCurrentAsync(false, false, null))?.Location;
```

and replace line 301:

```csharp
        DeviceLocation = await deviceLocation.GetCurrentAsync(requestPermission: true) ?? DeviceLocation;
```

with:

```csharp
        DeviceLocation = (await deviceLocation.GetCurrentAsync(false, true, null))?.Location ?? DeviceLocation;
```

- [ ] **Step 3: Build**

Run: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug`
Expected: `Počet chyb: 0`.

---

### Task 3: Geofence transitions report every entered place

**Files:**
- Modify: `Services/GeofenceManager.cs:131-159`
- Modify: `Platforms/Android/Geofencing/GeofenceBroadcastReceiver.cs:43-45` (keep it compiling; Task 6 finishes it)

- [ ] **Step 1: Add the result record and change `ApplyTransition`**

In `Services/GeofenceManager.cs`, replace the whole `ApplyTransition` method (the doc comment starting `/// Records the device entering or leaving geofences.` through its closing brace) with:

```csharp
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
            var changed = new List<string>();
            foreach (var id in ids)
            {
                if (known.ContainsKey(id) && (entered ? set.Add(id) : set.Remove(id)))
                    changed.Add(id);
            }
            if (changed.Count == 0) return GeofenceTransition.None;
            StoreInside(set);

            bool quietWindow = DateTime.UtcNow.Ticks < Preferences.Default.Get(QuietUntilPreference, 0L);
            var muted = quietWindow
                ? Preferences.Default.Get(QuietIdsPreference, "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
                : [];
            List<GeofencePlace> arrived = entered ? changed.Select(id => known[id]).ToList() : [];
            result = new GeofenceTransition(arrived, arrived.Where(p => !muted.Contains(p.Id)).ToList());
        }
        Notify();
        return result;
    }
```

At the end of the file (after the class's closing brace), add:

```csharp

/// <summary>What a geofence transition changed: the places entered, and those of them to announce.</summary>
public sealed record GeofenceTransition(IReadOnlyList<GeofencePlace> Entered, IReadOnlyList<GeofencePlace> Announce)
{
    public static readonly GeofenceTransition None = new([], []);
}
```

- [ ] **Step 2: Adapt the receiver**

In `Platforms/Android/Geofencing/GeofenceBroadcastReceiver.cs`, replace:

```csharp
        var arrivals = geofences.ApplyTransition(ids, entered);
        if (arrivals.Count == 0) return;
```

with:

```csharp
        var arrivals = geofences.ApplyTransition(ids, entered).Announce;
        if (arrivals.Count == 0) return;
```

- [ ] **Step 3: Build**

Run: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug`
Expected: `Počet chyb: 0`.

---

### Task 4: `LocationSelection` service

**Files:**
- Create: `Services/LocationSelection.cs`
- Modify: `Services/WeatherState.cs:309-322` (make the saved-list reader static)
- Modify: `MauiProgram.cs:35-38`

- [ ] **Step 1: Make the saved cities readable without a `WeatherState`**

In `Services/WeatherState.cs`, replace the `LoadSavedLocations` method:

```csharp
    List<WeatherLocation> LoadSavedLocations()
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
```

with:

```csharp
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
```

- [ ] **Step 2: Create the service**

Create `Services/LocationSelection.cs`:

```csharp
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

    readonly GeofenceManager geofences;
    readonly DeviceLocationService device;
    readonly ILogger<LocationSelection> logger;
    readonly Lock gate = new();
    readonly SemaphoreSlim positionLock = new(1, 1);
    bool loaded;
    WeatherLocation baseSelection = SelectionRules.Device;
    string? overrideId;
    DevicePosition? position;
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

    /// <summary>Fix time of the position in effect when it's over an hour old; null when fresh, or a city or geofence is in effect.</summary>
    public DateTimeOffset? StaleSince
    {
        get
        {
            lock (gate)
            {
                Load();
                return Fence is null && baseSelection.IsDevice && position is { } p && !SelectionRules.IsFresh(p, DateTimeOffset.UtcNow)
                    ? p.At
                    : null;
            }
        }
    }

    // The override geofence while it exists.
    GeofencePlace? Fence => overrideId is null ? null : geofences.Places.FirstOrDefault(g => g.Id == overrideId);

    /// <summary>
    /// The location to load, for the UI and alarms alike. For "Aktuální poloha" (rule 7): the stored position if under
    /// an hour old, else a new one, else the stored one marked stale; null only when no position was ever known.
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
        var resolved = place is not null
            ? new ResolvedLocation(place, null)
            : SelectionRules.Resolve(
                await RefreshPositionAsync(background, requestPermission: !background, cancellationToken), DateTimeOffset.UtcNow);
        logger.LogInformation("Location in effect ({Mode}): {Name} {Lat:0.000},{Lon:0.000}, stale since {Since}",
            background ? "background" : "foreground", resolved?.Location.Name, resolved?.Location.Latitude,
            resolved?.Location.Longitude, resolved?.StaleSince);
        return resolved;
    }

    /// <summary>The device position for Settings' first row, by the same one-hour rule; never prompts.</summary>
    public async Task<WeatherLocation?> DevicePositionAsync() =>
        (await RefreshPositionAsync(background: false, requestPermission: false, CancellationToken.None))?.Location;

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

    // Rule 2: the override geofence was left, removed, or inside states were forgotten.
    void OnGeofencesChanged() =>
        Update(() => overrideId = SelectionRules.OverrideAfterChange(overrideId, geofences.InsidePlaces));

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

    async Task<DevicePosition?> RefreshPositionAsync(bool background, bool requestPermission, CancellationToken cancellationToken)
    {
        await positionLock.WaitAsync(cancellationToken);
        try
        {
            var stored = StoredPosition;
            if (stored is not null && SelectionRules.IsFresh(stored, DateTimeOffset.UtcNow)) return stored;
            var found = await device.GetCurrentAsync(background, requestPermission, stored?.Location, cancellationToken);
            var newer = SelectionRules.Newer(stored, found);
            if (newer is not null && newer != stored) StorePosition(newer);
            return newer;
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
            Preferences.Default.Set(PositionPreference, JsonSerializer.Serialize(value.Location, AppJsonContext.Default.WeatherLocation));
            Preferences.Default.Set(PositionTimePreference, value.At.UtcTicks);
        }
    }
}
```

- [ ] **Step 3: Register it**

In `MauiProgram.cs`, replace:

```csharp
		builder.Services.AddSingleton<GeofenceManager>();
```

with:

```csharp
		builder.Services.AddSingleton<GeofenceManager>();
		builder.Services.AddSingleton<LocationSelection>();
```

- [ ] **Step 4: Build**

Run: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug`
Expected: `Počet chyb: 0`. (Nothing uses the service yet.)

---

### Task 5: `WeatherState` uses `LocationSelection`

**Files:**
- Modify: `Services/WeatherState.cs` (several regions, listed per step)

- [ ] **Step 1: Constructor and fields**

Replace:

```csharp
    readonly IWeatherProvider provider;
    readonly DeviceLocationService deviceLocation;
    readonly GeofenceManager geofences;
```

with:

```csharp
    readonly IWeatherProvider provider;
    readonly LocationSelection selection;
    readonly GeofenceManager geofences;
```

Replace:

```csharp
    public WeatherState(IWeatherProvider provider, DeviceLocationService deviceLocation, GeofenceManager geofences, ILocationSearch search,
        DisplaySettings display, ErrorReporter errors, WeatherSourceSettings source, ILogger<WeatherState> logger)
    {
        this.provider = provider;
        this.deviceLocation = deviceLocation;
        this.geofences = geofences;
```

with:

```csharp
    public WeatherState(IWeatherProvider provider, LocationSelection selection, GeofenceManager geofences, ILocationSearch search,
        DisplaySettings display, ErrorReporter errors, WeatherSourceSettings source, ILogger<WeatherState> logger)
    {
        this.provider = provider;
        this.selection = selection;
        this.geofences = geofences;
```

and in the same constructor, replace:

```csharp
        geofences.Changed += OnGeofencesChanged;
```

with:

```csharp
        geofences.Changed += OnGeofencesChanged;
        selection.Changed += OnSelectionChanged;
```

- [ ] **Step 2: Remove the old selection and device-position members**

Delete the line:

```csharp
    const string SelectedKeyPreference = "selected_location";
```

Replace everything from

```csharp
    /// <summary>Key of the chosen location; <see cref="WeatherLocation.DeviceKey"/> means "where I am".</summary>
```

down to and including the `FindLocation` method (ending `?? geofences.Places.Select(g => g.Place).FirstOrDefault(l => l.Key == key);`) with:

```csharp
    /// <summary>Key of the location in effect (see <see cref="LocationSelection"/>); <see cref="WeatherLocation.DeviceKey"/> means "where I am".</summary>
    public string SelectedKey => selection.CurrentKey;

    /// <summary>The location in effect without resolving GPS: the last known device position stands in for "Aktuální poloha".</summary>
    public WeatherLocation? SelectedLocation => selection.Current;

    /// <summary>Last known device position, or null if none is known yet. Settings lists it first.</summary>
    public WeatherLocation? DeviceLocation => selection.StoredPosition?.Location;

    /// <summary>"poloha z 14:05" while "Aktuální poloha" is in effect and its position is over an hour old; else null.</summary>
    public string? PositionNote => selection.StaleSince is { } since ? PositionAge(since) : null;

    string PositionAge(DateTimeOffset since)
    {
        // Stored before the time was kept, its age is unknown.
        if (since.UtcTicks == 0) return "poloha může být zastaralá";
        var local = since.ToLocalTime().DateTime;
        var day = local.Date == DateTime.Today ? "" : local.ToString("d. M. ", Czech.Culture);
        return $"poloha z {day}{display.Units.Time(local)}";
    }
```

- [ ] **Step 3: Notice doc**

Replace:

```csharp
    /// <summary>Set when the device location was chosen but couldn't be used, so a saved city is shown instead.</summary>
    public string? Notice { get; private set; }
```

with:

```csharp
    /// <summary>Set when "Aktuální poloha" is shown with a position over an hour old ("Poloha z 14:05").</summary>
    public string? Notice { get; private set; }
```

- [ ] **Step 4: Renames reach the base selection**

In `RenameToCzechAsync`, replace:

```csharp
                geofences.RenamePlaces(renamed);
```

with:

```csharp
                geofences.RenamePlaces(renamed);
                selection.RenameBase(renamed);
```

- [ ] **Step 5: Selecting and showing**

Replace the methods `SelectAsync` and `ShowAsync` (from `public async Task SelectAsync(WeatherLocation location)` through the end of `ShowAsync`, i.e. up to but not including `string? pendingRoute;`) with:

```csharp
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
```

Note: the original file declares `public event Action<string>? ShowForecastRequested;` between `SelectAsync` and `ShowAsync`; the replacement above includes it, so make sure it now appears only once.

- [ ] **Step 6: Removing a city**

Replace:

```csharp
    /// <summary>Removes a saved location. If it was selected, the app switches back to the device location.</summary>
    public void RemoveLocation(WeatherLocation location)
    {
        StoreSavedLocations(SavedLocations.Where(l => l.Key != location.Key).ToList());
        if (SelectedKey == location.Key)
            _ = SelectAsync(new WeatherLocation("Aktuální poloha", "", 0, 0, IsDevice: true));
    }
```

with:

```csharp
    /// <summary>Removes a saved location. If it was the chosen one, "Aktuální poloha" takes its place.</summary>
    public void RemoveLocation(WeatherLocation location)
    {
        StoreSavedLocations(SavedLocations.Where(l => l.Key != location.Key).ToList());
        selection.ForgetCity(location.Key);
    }
```

- [ ] **Step 7: Summaries use the one-hour rule**

In `LoadSummariesAsync`, replace:

```csharp
        // Only use the device location here if permission was already granted; Settings shouldn't prompt on open.
        DeviceLocation ??= (await deviceLocation.GetCurrentAsync(false, false, null))?.Location;
        // A place can appear more than once (a geofence at a saved city, two geofences at one place); ask once.
        IEnumerable<WeatherLocation> all = [.. InsideGeofences.Select(g => g.Place), .. RecentLocations];
        if (DeviceLocation is not null) all = all.Prepend(DeviceLocation);
```

with:

```csharp
        // The position from the last hour, else a new one; only with permission already granted, Settings shouldn't prompt on open.
        var device = await selection.DevicePositionAsync();
        Notify();
        // A place can appear more than once (a geofence at a saved city, two geofences at one place); ask once.
        IEnumerable<WeatherLocation> all = [.. InsideGeofences.Select(g => g.Place), .. RecentLocations];
        if (device is not null) all = all.Prepend(device);
```

- [ ] **Step 8: Resolving for the forecast**

Replace the whole `ResolveSelectedAsync` method with:

```csharp
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
```

- [ ] **Step 9: Update the callers in the Android project**

In `Platforms/Android/NotificationIntents.cs`, replace:

```csharp
        if (weather.FindLocation(key) is { } location) _ = weather.ShowAsync(location, route);
```

with:

```csharp
        _ = weather.ShowAsync(key, route);
```

In `Platforms/Android/MainActivity.cs`, replace:

```csharp
			if (geofence is not null) _ = weather.ShowAsync(geofence.Place);
```

with:

```csharp
			if (geofence is not null) _ = weather.ShowAsync(geofence);
```

In `Platforms/Android/Geofencing/GeofenceBroadcastReceiver.cs`, replace:

```csharp
        var arrivals = geofences.ApplyTransition(ids, entered).Announce;
        if (arrivals.Count == 0) return;

        // The arrival becomes the weather location, also while the app is closed (it's saved and used on the next
        // start). Of overlapping fences entered together, the smallest is the most specific place.
        if (services!.GetService<WeatherState>() is { } weather)
            _ = weather.SelectAsync(arrivals.MinBy(g => g.RadiusMeters)!.Place);
```

with:

```csharp
        var transition = geofences.ApplyTransition(ids, entered);
        // The smallest geofence entered becomes the location for the app and all alarms, also while the app is closed.
        // Leaving it is handled by LocationSelection itself, through GeofenceManager.Changed.
        services!.GetService<LocationSelection>()?.OnFencesEntered(transition.Entered);
        var arrivals = transition.Announce;
        if (arrivals.Count == 0) return;
```

- [ ] **Step 10: Build**

Run: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug`
Expected: `Počet chyb: 0`. If `CS0103 'deviceLocation'` or `'DeviceLocationPreference'` appears, a leftover of the removed members is still in `WeatherState.cs`; delete it.

Then check no old members remain:
Run: `grep -n "FindLocation\|DeviceLocationPreference\|LoadDeviceLocation\|lastDeviceLocation\|SelectedKeyPreference" -r C:/dev/misc/scratchpad/mauiapp/VibeWeather --include=*.cs --include=*.razor`
Expected: no output.

---

### Task 6: Alarms resolve the location on every run

**Files:**
- Modify: `Services/PrecipAlerts/PrecipAlertMonitor.cs:84-111, 233-240`
- Modify: `Services/SevereWeather/SevereWeatherMonitor.cs:49-68, 194-201`
- Modify: `Services/MorningSummary/MorningSummaryService.cs:43-59, 136-156`

- [ ] **Step 1: Precipitation alerts**

In `PrecipAlertMonitor.cs`, replace:

```csharp
    readonly WeatherState weather;
    readonly IPrecipAlertScheduler scheduler;
```

with:

```csharp
    readonly WeatherState weather;
    readonly LocationSelection selection;
    readonly IPrecipAlertScheduler scheduler;
```

Replace:

```csharp
    public PrecipAlertMonitor(IWeatherProvider provider, WeatherState weather, IPrecipAlertScheduler scheduler,
        WeatherSourceSettings source, DisplaySettings display, ErrorReporter errors, ILogger<PrecipAlertMonitor> logger)
    {
        this.provider = provider;
        this.weather = weather;
```

with:

```csharp
    public PrecipAlertMonitor(IWeatherProvider provider, WeatherState weather, LocationSelection selection,
        IPrecipAlertScheduler scheduler, WeatherSourceSettings source, DisplaySettings display, ErrorReporter errors,
        ILogger<PrecipAlertMonitor> logger)
    {
        this.provider = provider;
        this.weather = weather;
        this.selection = selection;
```

Replace:

```csharp
        var location = weather.SelectedLocation;
        if (location is null)
        {
            logger.LogInformation("Precipitation check skipped: no known position for the selected location");
            return (DateTime.UtcNow + DryInterval, "Poloha vybraného místa zatím není známá", null);
        }
```

with:

```csharp
        // The same location as the app shows; "Aktuální poloha" is found again here (at most an hour old).
        var location = (await selection.ResolveAsync(background: true, cancellationToken))?.Location;
        if (location is null)
        {
            logger.LogInformation("Precipitation check skipped: no known position for the selected location");
            return (DateTime.UtcNow + DryInterval, "Poloha vybraného místa zatím není známá", null);
        }
```

- [ ] **Step 2: Severe weather**

In `SevereWeatherMonitor.cs`, replace:

```csharp
    readonly WeatherState weather;
    readonly PrecipAlertMonitor precip;
```

with:

```csharp
    readonly WeatherState weather;
    readonly LocationSelection selection;
    readonly PrecipAlertMonitor precip;
```

Replace:

```csharp
    public SevereWeatherMonitor(AzureMapsSevereAlerts alerts, WeatherState weather, PrecipAlertMonitor precip,
        ISevereWeatherScheduler scheduler, ISevereWarningNotifier notifier, ErrorReporter errors,
        ILogger<SevereWeatherMonitor> logger)
    {
        this.alerts = alerts;
        this.weather = weather;
```

with:

```csharp
    public SevereWeatherMonitor(AzureMapsSevereAlerts alerts, WeatherState weather, LocationSelection selection,
        PrecipAlertMonitor precip, ISevereWeatherScheduler scheduler, ISevereWarningNotifier notifier, ErrorReporter errors,
        ILogger<SevereWeatherMonitor> logger)
    {
        this.alerts = alerts;
        this.weather = weather;
        this.selection = selection;
```

Replace:

```csharp
        var place = weather.SelectedLocation;
        if (place is null)
```

with:

```csharp
        var place = (await selection.ResolveAsync(background: true, cancellationToken))?.Location;
        if (place is null)
```

- [ ] **Step 3: Morning summary**

In `MorningSummaryService.cs`, replace:

```csharp
    readonly WeatherState weather;
```

with:

```csharp
    readonly LocationSelection selection;
```

Replace:

```csharp
    public MorningSummaryService(IWeatherProvider provider, WeatherState weather, IMorningSummaryScheduler scheduler,
        DisplaySettings display, ErrorReporter errors, ILogger<MorningSummaryService> logger)
    {
        this.provider = provider;
        this.weather = weather;
```

with:

```csharp
    public MorningSummaryService(IWeatherProvider provider, LocationSelection selection, IMorningSummaryScheduler scheduler,
        DisplaySettings display, ErrorReporter errors, ILogger<MorningSummaryService> logger)
    {
        this.provider = provider;
        this.selection = selection;
```

Replace the start of `BuildAsync`:

```csharp
        var location = weather.SelectedLocation;
        if (location is null)
        {
            logger.LogInformation("Morning summary skipped: no known position for the selected location");
            return null;
        }
```

with:

```csharp
        WeatherLocation? location;
        try
        {
            // The same location as the app shows; "Aktuální poloha" is found again here (at most an hour old).
            location = (await selection.ResolveAsync(background: true, cancellationToken))?.Location;
        }
        catch (OperationCanceledException ex)
        {
            errors.Report(ex, "Zjišťování polohy pro ranní přehled", popup: false);
            return null;
        }
        if (location is null)
        {
            logger.LogInformation("Morning summary skipped: no known position for the selected location");
            return null;
        }
```

- [ ] **Step 4: Build**

Run: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug`
Expected: `Počet chyb: 0`.

Then: `grep -rn "weather.SelectedLocation" C:/dev/misc/scratchpad/mauiapp/VibeWeather/Services`
Expected: no output.

---

### Task 7: Shared "Povolit vždy" prompt

**Files:**
- Create: `Components/Shared/BackgroundLocationPrompt.razor`
- Create: `Components/Shared/BackgroundLocationPrompt.razor.css`
- Modify: `Components/Pages/GeofenceEditor.razor:56-93, 99, 106, 139-142, 159-173`
- Modify: `Components/Pages/GeofenceEditor.razor.css:42-75`

- [ ] **Step 1: Create the component**

Create `Components/Shared/BackgroundLocationPrompt.razor`:

```razor
@inject IGeofenceMonitor Monitor

@* Location "Povolit vždy", which Android grants only on its own permission screen; after a refusal, how to allow it there. *@
@if (!denied)
{
    <section class="card permission">
        <span class="permission-title">Povolte přístup k poloze vždy</span>
        <span class="permission-text">@ChildContent</span>
        <button type="button" class="primary-btn" disabled="@busy" @onclick="AllowAsync">Pokračovat</button>
    </section>
}
else
{
    <section class="card permission">
        <span class="permission-title">Přístup k poloze není povolen vždy</span>
        <span class="permission-text">
            Otevřete nastavení aplikace, přejděte do Oprávnění → Poloha, zvolte <strong>Povolit vždy</strong>
            a nechte zapnutou možnost <strong>Používat přesnou polohu</strong>. Pak se vraťte a @AfterText.
        </span>
        <button type="button" class="primary-btn" @onclick="Monitor.OpenAppSettings">Otevřít nastavení</button>
        <button type="button" class="primary-btn secondary" disabled="@busy" @onclick="CheckAgainAsync">@RetryLabel</button>
    </section>
}

@code {
    /// <summary>Why it's needed, ending with what to pick on the next screen.</summary>
    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;

    /// <summary>What to do once it's allowed in the system settings, e.g. "zónu vytvořte".</summary>
    [Parameter, EditorRequired] public string AfterText { get; set; } = "";

    /// <summary>The button that goes on once it's allowed, e.g. "Povoleno, vytvořit zónu".</summary>
    [Parameter, EditorRequired] public string RetryLabel { get; set; } = "";

    /// <summary>Called when background location is granted.</summary>
    [Parameter] public EventCallback OnGranted { get; set; }

    bool denied;
    bool busy;

    async Task AllowAsync()
    {
        busy = true;
        bool granted = await Monitor.RequestBackgroundPermissionAsync();
        busy = false;
        if (granted) await OnGranted.InvokeAsync();
        else denied = true;
    }

    // Back from the system settings, where it may have been allowed.
    async Task CheckAgainAsync()
    {
        busy = true;
        bool granted = await Monitor.HasBackgroundPermissionAsync();
        busy = false;
        if (granted) await OnGranted.InvokeAsync();
    }
}
```

- [ ] **Step 2: Move the styles**

Create `Components/Shared/BackgroundLocationPrompt.razor.css` with:

```css
.permission {
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 18px;
}

.permission-title {
    font-size: 18px;
    font-weight: 600;
    letter-spacing: -0.01em;
}

.permission-text {
    font-size: 14px;
    line-height: 1.45;
    color: var(--mut);
    text-wrap: pretty;
}

    /* ::deep: the explanation comes from the parent, so its <strong> carries the parent's scope. */
    .permission-text ::deep strong,
    .permission-text strong {
        color: var(--ink);
        font-weight: 600;
    }

.permission .primary-btn {
    margin-top: 6px;
}

.primary-btn.secondary {
    border: 1.5px solid var(--line);
    background: transparent;
    color: var(--ink);
}
```

In `Components/Pages/GeofenceEditor.razor.css`, delete lines 42–75: the rules `.permission`, `.permission-title`, `.permission-text`, `.permission-text strong`, `.permission .primary-btn` and `.primary-btn.secondary` (everything from `.permission {` up to, not including, `.error {`).

- [ ] **Step 3: Use it in the geofence editor**

In `Components/Pages/GeofenceEditor.razor`, replace the block from `@if (step == Step.ExplainBackground)` through the closing `}` of the `else if (step == Step.BackgroundDenied)` section (lines 56–77) with:

```razor
            @if (step == Step.Background)
            {
                <BackgroundLocationPrompt AfterText="zónu vytvořte" RetryLabel="Povoleno, vytvořit zónu" OnGranted="OnBackgroundGranted">
                    Aby aplikace poznala váš příjezd i ve chvíli, kdy je zavřená, potřebuje Android přístup k poloze nastavený na
                    <strong>Povolit vždy</strong>. Na další obrazovce s oprávněními Androidu tuto možnost vyberte.
                </BackgroundLocationPrompt>
            }
```

Replace:

```razor
            @if (step == Step.Choose)
            {
                <button type="button" class="primary-btn" disabled="@busy" @onclick="CreateAsync">
                    @(busy ? "Vytvářím…" : "Vytvořit zónu")
                </button>
            }
            else if (step == Step.BackgroundDenied)
            {
                <button type="button" class="primary-btn secondary" disabled="@busy" @onclick="CreateAsync">Povoleno, vytvořit zónu</button>
            }
```

with:

```razor
            @if (step == Step.Choose)
            {
                <button type="button" class="primary-btn" disabled="@busy" @onclick="CreateAsync">
                    @(busy ? "Vytvářím…" : "Vytvořit zónu")
                </button>
            }
```

Replace `enum Step { Choose, ExplainBackground, BackgroundDenied }` with `enum Step { Choose, Background }`.

In `CreateAsync`, replace:

```csharp
            if (!await Monitor.HasBackgroundPermissionAsync())
            {
                step = step == Step.BackgroundDenied ? Step.BackgroundDenied : Step.ExplainBackground;
                return;
            }
```

with:

```csharp
            if (!await Monitor.HasBackgroundPermissionAsync())
            {
                step = Step.Background;
                return;
            }
```

Replace the whole `AllowBackgroundAsync` method with:

```csharp
    async Task OnBackgroundGranted()
    {
        step = Step.Choose;
        await CreateAsync();
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug`
Expected: `Počet chyb: 0`.

---

### Task 8: Settings — permission flow, check mark, stale note

**Files:**
- Modify: `Components/Pages/Settings.razor` (markup lines 1–16, 31, 134, 193, 215–238; code lines 387–478)

- [ ] **Step 1: Inject the permission service**

After `@inject IMorningSummaryScheduler MorningScheduler`, add:

```razor
@inject IGeofenceMonitor Monitor
```

- [ ] **Step 2: Location rows pass their geofence**

Replace:

```razor
                    <button type="button" class="row loc-row" @onclick="() => Select(loc)">
```

with:

```razor
                    <button type="button" class="row loc-row" @onclick="() => Select(loc, fence)">
```

- [ ] **Step 3: Alarm switches go through the permission flow**

Replace `@onclick="Alerts.ToggleAsync"` with `@onclick="() => ToggleAlarmAsync(Alarm.Precip)"`.
Replace `@onclick="Morning.ToggleAsync"` with `@onclick="() => ToggleAlarmAsync(Alarm.Morning)"`.
Replace `@onclick="ToggleSevereAsync"` with `@onclick="() => ToggleAlarmAsync(Alarm.Severe)"`.

Replace:

```razor
                @if (severeError is not null)
                {
                    <div class="row plan-row"><span class="row-sub key-error">@severeError</span></div>
                }
```

with:

```razor
                @if (alarmError is not null)
                {
                    <div class="row plan-row"><span class="row-sub key-error">@alarmError</span></div>
                }
```

After the exact-alarm row (the `@if ((Alerts.Enabled && !Alerts.OnTime) || (Morning.Enabled && !Morning.OnTime))` block, ending `</div>` + `}`), add:

```razor
                @* Allowed when switched on, but it can be taken away in the system settings later. *@
                @if (backgroundMissing)
                {
                    <div class="row late-row">
                        <div class="row-text">
                            <span class="toggle-label">Poloha na pozadí není povolena</span>
                            <span class="row-sub">Oznámení použijí poslední známou polohu. Povolte přístup k poloze „Povolit vždy“.</span>
                        </div>
                        <button type="button" class="pill" @onclick="Monitor.OpenAppSettings">Povolit</button>
                    </div>
                }
```

At the end of the OZNÁMENÍ section, replace:

```razor
                }
            </div>
        </section>

        <section class="group">
            <span class="mono-label group-label">DIAGNOSTIKA</span>
```

with:

```razor
                }
            </div>
            @if (pendingAlarm is not null)
            {
                <BackgroundLocationPrompt AfterText="oznámení zapněte" RetryLabel="Povoleno, zapnout" OnGranted="EnablePendingAsync">
                    Aby upozornění a ranní přehled sledovaly vaši aktuální polohu i při zavřené aplikaci, potřebuje Android
                    přístup k poloze nastavený na <strong>Povolit vždy</strong>. Na další obrazovce s oprávněními Androidu
                    tuto možnost vyberte.
                </BackgroundLocationPrompt>
            }
        </section>

        <section class="group">
            <span class="mono-label group-label">DIAGNOSTIKA</span>
```

- [ ] **Step 4: Code — placeholder, rows, subtitles**

Replace:

```csharp
    static readonly WeatherLocation DevicePlaceholder = new("Aktuální poloha", "", 0, 0, IsDevice: true);
```

with:

```csharp
    static readonly WeatherLocation DevicePlaceholder = SelectionRules.Device;
```

Replace:

```csharp
    // Why warnings couldn't be switched on (no Azure Maps key), shown under the switch.
    string? severeError;

    async Task ToggleSevereAsync() => severeError = await Severe.ToggleAsync();

    string SevereSubtitle => Severe.Enabled
        ? $"Oficiální výstrahy pro {State.SelectedLocation?.Name ?? "vybrané místo"} · Azure Maps"
        : "Oficiální výstrahy pro vybrané místo · potřebuje klíč Azure Maps";

    string MorningSubtitle => Morning.Enabled
        ? $"Další: {MorningNext.ToString("ddd", Czech.Culture)} {Units.Time(MorningNext)} · {State.SelectedLocation?.Name ?? "vybrané místo"}"
        : "Jednou denně dnešní předpověď pro vybrané místo";
```

with:

```csharp
    enum Alarm { Precip, Morning, Severe }

    // Why an alarm couldn't be switched on (no location access, no Azure Maps key), shown under the switches.
    string? alarmError;
    // Switched on, waiting for "Povolit vždy".
    Alarm? pendingAlarm;
    // An alarm is on without background location (taken away in the system settings).
    bool backgroundMissing;

    bool IsOn(Alarm alarm) => alarm switch
    {
        Alarm.Precip => Alerts.Enabled,
        Alarm.Morning => Morning.Enabled,
        _ => Severe.Enabled,
    };

    // Every alarm follows the selected location, also "Aktuální poloha" with the app closed: it needs location "Povolit vždy".
    async Task ToggleAlarmAsync(Alarm alarm)
    {
        alarmError = null;
        if (IsOn(alarm))
        {
            if (alarm == Alarm.Precip) Alerts.Disable();
            else if (alarm == Alarm.Morning) Morning.Disable();
            else Severe.Disable();
            await CheckBackgroundAsync();
            return;
        }
        if (!await Monitor.RequestForegroundPermissionAsync())
        {
            alarmError = "Oznámení potřebují přístup k poloze. Pokračujte jeho povolením.";
            return;
        }
        if (!await Monitor.HasBackgroundPermissionAsync())
        {
            pendingAlarm = alarm;
            return;
        }
        await EnableAsync(alarm);
    }

    async Task EnablePendingAsync()
    {
        if (pendingAlarm is not { } alarm) return;
        pendingAlarm = null;
        await EnableAsync(alarm);
    }

    async Task EnableAsync(Alarm alarm)
    {
        if (alarm == Alarm.Precip) await Alerts.EnableAsync();
        else if (alarm == Alarm.Morning) await Morning.EnableAsync();
        else alarmError = await Severe.EnableAsync();
        await CheckBackgroundAsync();
    }

    async Task CheckBackgroundAsync() =>
        backgroundMissing = (Alerts.Enabled || Morning.Enabled || Severe.Enabled) && !await Monitor.HasBackgroundPermissionAsync();

    // " · poloha z 14:05" while "Aktuální poloha" is in effect with a position over an hour old.
    string PositionSuffix => State.PositionNote is { } note ? $" · {note}" : "";

    string SevereSubtitle => Severe.Enabled
        ? $"Oficiální výstrahy pro {State.SelectedLocation?.Name ?? "vybrané místo"}{PositionSuffix} · Azure Maps"
        : "Oficiální výstrahy pro vybrané místo · potřebuje klíč Azure Maps";

    string MorningSubtitle => Morning.Enabled
        ? $"Další: {MorningNext.ToString("ddd", Czech.Culture)} {Units.Time(MorningNext)} · {State.SelectedLocation?.Name ?? "vybrané místo"}{PositionSuffix}"
        : "Jednou denně dnešní předpověď pro vybrané místo";
```

Replace:

```csharp
    string AlertsSubtitle =>
        $"Když {(State.SelectedLocation?.Name is { } name ? $"v místě {name}" : "na vybraném místě")} začne pršet, sněžit nebo padat mrznoucí déšť";
```

with:

```csharp
    string AlertsSubtitle =>
        $"Když {(State.SelectedLocation?.Name is { } name ? $"v místě {name}" : "na vybraném místě")} začne pršet, sněžit nebo padat mrznoucí déšť{PositionSuffix}";
```

- [ ] **Step 5: Code — lifecycle and selection**

Replace:

```csharp
    protected override async Task OnInitializedAsync()
    {
        State.Changed += OnStateChanged;
        Alerts.Changed += OnStateChanged;
        Morning.Changed += OnStateChanged;
        Severe.Changed += OnStateChanged;
        Source.Changed += OnSourceChanged;
        if (await Source.GetAzureKeyAsync() is { Length: > 0 } key) azureKeyHint = Hint(key);
        await State.LoadSummariesAsync();
    }
```

with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        State.Changed += OnStateChanged;
        Alerts.Changed += OnAlarmsChanged;
        Morning.Changed += OnAlarmsChanged;
        Severe.Changed += OnAlarmsChanged;
        Source.Changed += OnSourceChanged;
        if (await Source.GetAzureKeyAsync() is { Length: > 0 } key) azureKeyHint = Hint(key);
        await CheckBackgroundAsync();
        await State.LoadSummariesAsync();
    }
```

Replace:

```csharp
    async Task Select(WeatherLocation loc)
    {
        _ = State.SelectAsync(loc);
        await Close();
    }
```

with:

```csharp
    async Task Select(WeatherLocation loc, GeofencePlace? fence)
    {
        _ = fence is null ? State.SelectAsync(loc) : State.SelectFenceAsync(fence);
        await Close();
    }
```

Replace:

```csharp
    void OnStateChanged() => InvokeAsync(StateHasChanged);
```

with:

```csharp
    void OnStateChanged() => InvokeAsync(StateHasChanged);

    // The alarms also report a return to the app, when background location may have been allowed or taken away.
    void OnAlarmsChanged() => InvokeAsync(async () =>
    {
        await CheckBackgroundAsync();
        StateHasChanged();
    });
```

Replace the `Dispose` body lines:

```csharp
        Alerts.Changed -= OnStateChanged;
        Morning.Changed -= OnStateChanged;
        Severe.Changed -= OnStateChanged;
```

with:

```csharp
        Alerts.Changed -= OnAlarmsChanged;
        Morning.Changed -= OnAlarmsChanged;
        Severe.Changed -= OnAlarmsChanged;
```

- [ ] **Step 6: Build**

Run: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug`
Expected: `Počet chyb: 0`. (`Alerts.ToggleAsync`, `Morning.ToggleAsync`, `Severe.ToggleAsync` stay in their services; they're simply no longer called from Settings.)

---

### Task 9: Re-run the rules test

- [ ] **Step 1:** Run (from `C:\dev\misc\scratchpad\mauiapp\tests`): `dotnet run location-selection-rules.cs`
Expected: `All passed`.

- [ ] **Step 2:** Run the existing test too: `dotnet run severe-warning-diff.cs`
Expected: passes as before (it doesn't touch changed code; this guards against accidental edits in `Data/`).

---

### Task 10: Verify on the emulator

The emulator is `emulator-5554` (MAUI_Emulator_API_36). In Git Bash set `ADB="$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"`. Positions: `"$ADB" -s emulator-5554 emu geo fix <longitude> <latitude>` (longitude first). Logs: `"$ADB" -s emulator-5554 logcat -d | grep -E "Location in effect|Selected location is now"` (clear first with `logcat -c`). If those lines don't appear, the Debug logger writes under tag `DOTNET`: `logcat -d -s DOTNET`.

- [ ] **Step 1: Deploy**

From the app root: `dotnet build -t:Run -f net10.0-android -c Debug -p:AdbTarget="-s emulator-5554" -p:RuntimeIdentifier=android-x64`
Expected: the app starts. Logcat shows one `Location in effect (foreground)` line.

- [ ] **Step 2: Migration**

Expected on first start: the previously selected place is still selected (check mark in Settings). If it was a geofence place and the emulator is outside it, "Aktuální poloha" is selected.

- [ ] **Step 3: Aktuální poloha, one-hour cache**

Set `geo fix 14.4378 50.0755` (Praha). In Settings tap "Aktuální poloha". Expected: header "Praha". Restart the app (`am force-stop com.companyname.vibeweather`, then launch): logcat shows the same position without a new fix (`stale since` empty). Move to `geo fix 16.6068 49.1951` (Brno) and restart: still Praha (under an hour). To check expiry without waiting an hour, temporarily set `MaxPositionAge` in `Services/SelectionRules.cs` to `TimeSpan.FromMinutes(1)`, deploy, wait a minute and restart. Expected: Brno. Keep the 1-minute value for Steps 8 and 9, then restore `TimeSpan.FromHours(1)` and deploy again before Step 10.

- [ ] **Step 4: Geofence enter and leave**

Create a geofence at Praha with 1 km (Locations → zone). With the emulator in Brno, "Aktuální poloha" selected. Move to Praha (`geo fix 14.4378 50.0755`). Expected within a minute or two: arrival notification; Settings shows the Praha geofence row checked; logcat `Selected location is now 50.08,14.44`. Move back to Brno. Expected: `Selected location is now device`; header shows Brno again.

- [ ] **Step 5: Tap overrides the geofence**

Inside the Praha geofence, tap a saved city (e.g. Oslo). Expected: Oslo selected. Leave the geofence: Oslo stays.

- [ ] **Step 6: Overlapping geofences**

Add a second, larger geofence (5 km) around Praha. Enter both: the 1 km one is selected. Move about 2 km away (`geo fix 14.47 50.075`): the 5 km one takes over. Move to Brno: back to the base selection.

- [ ] **Step 7: Alarms and the permission flow**

Revoke location "Povolit vždy" for the app in Android settings (set "Jen při používání"). In Settings switch on Upozornění na srážky. Expected: the "Povolte přístup k poloze vždy" card; Pokračovat opens the system screen; choosing "Povolit vždy" and returning switches the alarm on. Deny instead: the denied card shows; the switch stays off.

- [ ] **Step 8: Alarm with the app closed**

With Ranní přehled on, "Aktuální poloha" selected, tap the debug button "Ranní přehled za 10 s", then press Home and `am kill com.companyname.vibeweather` (kills the background process; the alarm stays). Move the emulator (`geo fix` to a new place) and wait over a minute (the shortened `MaxPositionAge` from Step 3). Expected after 10 s: a morning summary notification for the new place; logcat `Location in effect (background): <new place>`.

- [ ] **Step 9: Position unavailable**

Turn location off on the emulator (quick settings) and wait over a minute (shortened `MaxPositionAge`), then reopen the app. Expected: Teď header subtitle "Poloha z HH:mm"; alarm subtitles in Settings end with "· poloha z HH:mm".

- [ ] **Step 10: Background permission revoked while an alarm is on**

With an alarm on, set location to "Jen při používání" in Android settings, return to the app, open Settings. Expected: row "Poloha na pozadí není povolena" with Povolit.

- [ ] **Step 11: Geofence editor still works**

Create a new zone with background location revoked. Expected: the same cards as before; the "Povoleno, vytvořit zónu" button is now inside the denied card.

Report each step as passed or failed with what was seen; don't mark the work done with failing steps.
