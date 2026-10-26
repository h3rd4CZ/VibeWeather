# Location selection — design

Date: 2026-10-16 · App: VibeWeather (.NET MAUI Blazor, Android), UI in Czech

## Goal

The app and all its alarms (precipitation alerts, severe weather warnings, morning summary) always use the same
location, chosen by one set of rules. Today they drift apart:

- The device position is resolved only when "Aktuální poloha" is selected and the forecast loads in the foreground.
  Settings reuses the stored position forever (`DeviceLocation ??= …`), and alarms use whatever was stored last.
- Entering a geofence selects its place, but leaving it doesn't select anything back, so the alerts keep watching
  the geofence place.
- When the position is unknown, the UI shows the first saved city while the alarms skip their check.

## Location kinds

| Kind | Used for UI and alarms |
|---|---|
| **Aktuální poloha** (device position) | Resolved on every app start and every alarm run. A position younger than the cache duration (1 hour by default, see below) is reused without asking GPS. |
| **Saved city** | Always that city; nothing changes it. |
| **Geofence place** | While a geofence is active (the device is inside it), its place. On leaving, the last chosen city or device position comes back. |

## Decisions

| Topic | Decision |
|---|---|
| Tap while inside a geofence | Tapping a city or Aktuální poloha in Settings overrides the geofence immediately, for UI and alarms, and becomes the selection restored after leaving. The next geofence entered takes over again. |
| Background position | Every alarm requires the "Povolit vždy" (background location) permission, asked the same way as when creating a geofence. Alarms read the position themselves in the background. |
| When the permission is required | When turning on any alarm, whatever location is selected. |
| Cache duration | Chosen in Settings → Místa → "Platnost aktuální polohy": 5 min, 15 min, 30 min, 1 h (default) or 2 h. Applies to the UI and all alarms. |
| Refresh button | Settings → Místa → "Obnovit aktuální polohu" asks for a new fix at once, ignoring the cache, and stores it; the forecast reloads when Aktuální poloha is in effect. The row says what was found, or that no new position came. |
| Position unavailable | When no position younger than the cache duration can be had (GPS off, timeout, permission revoked), UI and alarms both use the last known position, however old, and show when it is from ("Poloha z 14:05"). |
| Overlapping geofences | The one with the smallest radius wins (as today). |

## Rules

State, kept in Preferences:

- **Base selection** — Aktuální poloha or a saved city, stored as the whole `WeatherLocation`. Changed only by tapping
  Aktuální poloha or a city.
- **Geofence override** — id of the geofence currently in charge, or none.
- **Device position** — last resolved position with the time of its fix.

**Effective location** = the override geofence's place if there is one, else the base selection. The UI (check mark,
forecast, labels) and all alarms use only this.

1. **Entering geofences** sets the override to the smallest of the geofences just entered.
2. **Leaving the override geofence** (or it being deleted, or geofencing becoming unavailable so inside states are
   forgotten): if the device is still inside other geofences, the smallest of them becomes the override; otherwise
   the override is cleared and the base selection applies. Leaving any other geofence changes nothing.
3. **Tapping Aktuální poloha or a city** sets the base selection and clears the override.
4. **Tapping a geofence row** (listed in Settings while inside it) or a geofence arrival notification sets the
   override to that geofence — only while the device is inside it; otherwise the tap just opens the app and the
   effective location stays.
5. **Removing the base city** sets the base selection to Aktuální poloha (as today).
6. **Migration**: when the stored `selected_location` key belongs to a geofence place, the base selection becomes
   Aktuální poloha and the override that geofence if the device is inside it. A city key becomes that city's
   `WeatherLocation`; `device` stays Aktuální poloha.
7. **Resolving Aktuální poloha** (the same for UI and alarms):
   1. Stored position younger than the cache duration, and no geofence entered or left since it was found → use it. (Crossing a
      geofence boundary proves the device moved, so the stored position no longer counts as current; the next lookup
      also ignores a platform fix that isn't newer than the stored one. If no new position comes, the stored one is
      used marked stale, as in 3.)
   2. Otherwise the platform's last known fix if younger than the cache duration, else a fresh fix (foreground: 10 s timeout;
      background: 3 s, see below). A new position is stored and named by the geocoder.
   3. If that fails → the stored position, however old, marked stale with the time of its fix.
   4. No position ever known → unknown: the UI shows the existing error ("Vaše poloha není k dispozici…"), alarms
      skip the check and retry as today ("Poloha vybraného místa zatím není známá").

The effective location changing (rules 1–6) raises `WeatherState.SelectionChanged`, so the forecast reloads and every
alarm checks again at once (existing behaviour). A new device position under Aktuální poloha does not.

## Architecture

Approach A: a new service owns the rules; everything else asks it.

- **`Services/LocationSelection.cs`** (new, singleton). Depends on `GeofenceManager` and `DeviceLocationService`.
  - `WeatherLocation? Current` / `string CurrentKey` — effective location without resolving GPS (stored device
    position stands in), for labels and check marks.
  - `Task<ResolvedLocation?> ResolveAsync(bool background, CancellationToken)` — effective location, applying rule 7
    when it's Aktuální poloha. `record ResolvedLocation(WeatherLocation Location, DateTimeOffset? StaleSince)`.
    The only way UI and alarms get the location to load.
  - `Task<WeatherLocation?> DevicePositionAsync()` — the device position for Settings' first row: rule 7 in the
    foreground, never prompts for permission.
  - `Select(WeatherLocation)` — rule 3 (Aktuální poloha or a city); `SelectFence(GeofencePlace)` — rule 4. Separate,
    because a geofence can sit at a saved city and share its key.
  - `OnFencesEntered(IReadOnlyList<GeofencePlace>)` — rule 1; called by the geofence receiver.
  - Subscribes to `GeofenceManager.Changed` for rule 2.
  - `event Action? Changed` — the effective location changed.
- **`GeofenceManager.ApplyTransition`** returns all places just entered as well as the ones to announce, so entries
  repeated after a reboot (muted for notifications) still restore the override.
- **`GeofenceBroadcastReceiver`** calls `LocationSelection.OnFencesEntered` with all entered places instead of
  `WeatherState.SelectAsync`.
- **`DeviceLocationService.GetCurrentAsync`** gets a `background` mode: checks the background location permission
  instead of "when in use", never prompts, and caps a fresh fix at 3 s. Returns the fix time with the position.
- **`WeatherState`**
  - `SelectedKey`, `SelectedLocation`, `FindLocation(DeviceKey)` delegate to `LocationSelection`; `SelectAsync` calls
    `LocationSelection.Select`; `SelectionChanged` is raised from `LocationSelection.Changed`.
  - `ResolveSelectedAsync` uses `ResolveAsync(background: false)`. The fallback to the first saved city goes;
    `Notice` becomes "Poloha z {čas}" when stale.
  - `LoadSummariesAsync` uses `DevicePositionAsync()` instead of `DeviceLocation ??= …`.
  - `RemoveLocation` of the base city selects Aktuální poloha (rule 5); `RenameToCzechAsync` renames the stored base
    city too.
  - Runs the migration (rule 6) once, since it owns the saved city list.
- **Alarms** — `PrecipAlertMonitor.CheckAsync`, `SevereWeatherMonitor.CheckAsync`, `MorningSummaryService.BuildAsync`
  use `await selection.ResolveAsync(background: true, ct)` instead of `weather.SelectedLocation`. Their receivers
  have about 8 s; the 3 s position cap leaves the rest for the download.
- **`Components/BackgroundLocationPrompt.razor`** (new) — the two permission cards now in `GeofenceEditor.razor`
  ("Povolte přístup k poloze vždy" with Pokračovat, "Přístup k poloze není povolen vždy" with Otevřít nastavení),
  with the explanation text as a parameter. Used by `GeofenceEditor` and Settings. Permission calls stay on
  `IGeofenceMonitor`.
- **Settings**
  - Turning on any alarm switch: location permission → background permission (explain → system request → denied
    card) → notification permission → enable. The switch stays off unless location and background location are
    granted. Card text for alarms: "Aby upozornění fungovala i při zavřené aplikaci, potřebuje Android přístup
    k poloze nastavený na **Povolit vždy**…".
  - When an alarm is on but background location is no longer granted: a warning row under the alarms with
    Otevřít nastavení.
  - Check mark follows `CurrentKey` (the geofence row while it's in charge). Tapping a geofence row calls
    `SelectFence`, other rows `Select`. `MainActivity`'s arrival-notification tap calls `SelectFence` too.
  - Alarm subtitles name the effective location, plus " · poloha z {čas}" when stale.

## Error handling

- GPS failure, timeout or missing permission never throws to callers: rule 7.3/7.4 applies.
- Unreadable stored selection → Aktuální poloha, no override (logged).
- Override pointing at a geofence that no longer exists → cleared on load (rule 2).

## Testing

No test project exists; verified on the Android emulator (`-t:Run`, `RuntimeIdentifier=android-x64`), positions set
with `adb emu geo fix`, alarms fired with `adb shell am broadcast` to their receivers while the app is closed, results
read from logcat:

1. Enter a geofence → UI and all alarms use its place; leave → the base selection again.
2. Inside a geofence, tap a city → city stays; leave the geofence → still the city.
3. Inside two overlapping geofences, leave the smaller → the larger takes over; leave it → base selection.
4. Aktuální poloha: app restart within 1 hour reuses the position; after moving the emulator and 1 hour (or a
   shortened cache in a debug run) a new fix is used, also by an alarm with the app closed.
5. Location off → UI notice and alarm use the stale position with "Poloha z …".
6. Turning on each alarm runs the permission flow; denying background location leaves the switch off.
7. Revoking background location while an alarm is on → warning row in Settings, alarm uses the stale position.
8. Migration: a stored geofence key in `selected_location` (inside and outside the fence).
