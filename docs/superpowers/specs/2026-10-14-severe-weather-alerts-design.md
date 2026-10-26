# Severe weather alerts — design

Date: 2026-10-14 · App: VibeWeather (.NET MAUI Blazor, Android), UI in Czech

## Goal

Notify the user about official severe-weather warnings (storms, wind, heat, floods, …) for the place selected in the
app, and show active warnings in the app. Replaces the placeholder "Nebezpečné počasí" switch in Settings, which does
nothing today.

## Decisions

| Topic | Decision |
|---|---|
| Source | Azure Maps *Get Severe Weather Alerts* (`/weather/severe/alerts/json`, api-version 1.1), `language=cs-CZ`, `details=true`. Official agencies worldwide (ČHMÚ for Czechia). |
| Key | The saved Azure Maps key, also when Open-Meteo is the forecast source. Without a key the feature can't be turned on; the switch says why. |
| Places | Only the selected place (like precipitation alerts). One request per check. |
| Notify | When a warning is new, upgraded, extended or corrected (`latestStatus.english` = New, Upgrade, Extend, Correct, Update). Silent while it continues. Cancel/Expire, or the warning disappearing from results, removes its notification. One notification per warning, updated in place. |
| Timing | Hourly, only within the existing alert hours (`AlertHours`, weekday/weekend). Outside them the next check waits for their start. A check runs at once when the feature is switched on and when the selected place changes. |
| In app | Banner per active warning at the top of the Teď tab; tapping opens a detail page. Notification tap opens the same page. |

## Architecture

Separate monitor with its own hourly alarm (approach A), following the precipitation-alert pattern.

- **`Services/SevereWeather/SevereWeatherMonitor.cs`** — the feature's logic. `Enabled`, `EnableAsync`/`Disable`/`ToggleAsync`,
  `EnsureScheduled`, `Refresh`, `RunCheckAsync(ct)` (download, diff with stored warnings, plan next check, return what to
  post/remove), `Active` (stored warnings for the selected place), `NextCheck`/`LastCheck`/`NextCheckReason`, `Changed`.
  Uses `AlertHours` from `PrecipAlertMonitor` (same hours) and the selected location from `WeatherState`; re-checks on
  `WeatherState.SelectionChanged`.
- **`Services/SevereWeather/SevereWeatherAlert.cs`** — `record SevereWarning(string Id, string Name, string Status, string Area,
  DateTimeOffset Start, DateTimeOffset End, string Source, string Details, string? Disclaimer, int Priority)`; Id combines
  `sourceId` and `alertId`.
- **`Services/AzureMaps/AzureMapsSevereAlerts.cs`** — fetches and maps the API response (DTOs in `AzureMapsDtos.cs`), using
  `AzureMapsHttp` (Czech errors, timeout/cancellation handling) and the saved key from `WeatherSourceSettings`.
- **`ISevereWeatherScheduler`** (in `Services/SevereWeather/`, same shape as `IPrecipAlertScheduler`: `Schedule(atUtc)`,
  `Cancel()`, `IsScheduled`) with the Android `AndroidSevereWeatherScheduler` + `SevereWeatherReceiver`
  (`Platforms/Android/SevereWeather/`) — one pending alarm via the existing `ExactAlarms` helper; the receiver runs
  `RunCheckAsync` within the broadcast's time budget (8 s) and posts/removes notifications.
- **`Platforms/Android/SevereWeather/SevereNotifications.cs`** — channel "Výstrahy" (high importance), one notification per
  warning (id from the warning Id), BigText with area, time range and agency; tap opens `/warning/{id}`.
- **UI** — `Components/Shared/WarningBanner.razor` on Home; `Components/Pages/WarningDetail.razor` (`/warning/{id}`);
  Settings switch row with last/next check, hidden details when off; restore after reboot via `GeofenceRestoreReceiver`
  (`EnsureScheduled`), re-arm on app start (`App.CreateWindow`).

## Data flow

Alarm → receiver → `RunCheckAsync`: selected place → Azure request → list of warnings → compare with stored list
→ store new list → plan next check (+1 h, clamped to alert hours) → receiver posts notifications
for new/changed warnings and cancels ones that ended → `Changed` refreshes the banner/Settings when the app is open.

**Notify rule** (per warning, against the stored list from the previous check of the same place):
- Id not stored before → notify (unless its status is Cancel/Expire).
- Stored, and status is Update/Upgrade/Extend/Correct with a different status, end time or name than stored → notify
  again (same notification id, so it's replaced).
- Status Continue, or nothing changed → no notification (an already posted one stays).
- Status Cancel/Expire, or missing from the new results → remove its notification and drop it from the store.
- The selected place changes → the store is cleared (and its notifications removed) before the first check of the new
  place.

## Display

- Banner: "Výstraha · {name} · do {end time}" (or "od {start}" when it starts later), coloured by priority
  (priority ≤ 10 red, else orange — the scale is provider-specific, so only two steps).
- Detail page: name, area, "od – do" in the place's time, agency, full official text (`alertDetails`, often Czech for
  ČHMÚ), disclaimer when present.
- Times shown with `DisplayUnits` (24/12-hour).

## Errors

As in precipitation alerts: timeout → retry in 2 min; provider error → retry in 15 min (or Retry-After), reported to the
error log without a popup; missing/rejected key → feature turns itself off and Settings says so; no selected position →
retry in 15 min. Stored warnings are kept on errors (the banner shows the last known state).

## Out of scope

Several places, push from a server, MeteoAlarm, warnings for Open-Meteo users without a key, iOS.

## Testing

Build; emulator end-to-end with a place that has an active warning (found via the API — usually several in the US,
Canada or Europe): enable, select the place, check notification text, banner, detail page, and that a second check
doesn't notify again. Verify Settings shows last/next check and that switching off removes notifications.
