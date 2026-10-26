# Quick place picker — design

Date: 2026-10-26 · App: VibeWeather (.NET MAUI Blazor, Android), UI in Czech

## Goal

Change the forecast's place as fast as possible from any main tab: a floating button opens a sheet with the same
places as Settings → Místa; one tap selects a place and closes the sheet.

## Decisions

| Topic | Decision |
|---|---|
| Button | Round, 52 px, location-pin icon in the accent colour, floating bottom right just above the tab bar. On the four main tabs only (MainLayout), not on Settings and its sub-screens (OverlayLayout). |
| Sheet | Slides up from the bottom over a dimmed screen; header "Místa" with a ✕; only the place list (no "Přidat nebo odebrat místa", no other rows). |
| Place list | One shared component, `PlaceList`, used by Settings and the sheet: same rows, order and behaviour as Settings today (device position first, geofence places the device is inside, then the recent cities; temperature and icon; ✓ on the selected place; UVNITŘ chip). |
| Device position row | Set apart in both places: a location-pin icon before its name and an accent-tinted background across the card's width. The ✓ still marks the selected place, this row included. |
| Picking | Selects the place (the forecast loads in the background, as from Settings) and closes the sheet; Settings goes back as today. |
| Closing without picking | ✕, a tap on the dimmed area, or Android Back. |
| Content under the button | The main content gets more bottom padding, so the last card can scroll clear of the button. |

## Code

- `Components/Shared/PlaceList.razor` (+ `.razor.css`): the rows (no wrapper element, so they stay direct children of
  the caller's `.group-card` and keep its row dividers). Loads the summaries (`State.LoadSummariesAsync`) on open and
  re-renders on `State.Changed`. Parameter `OnSelected` (EventCallback) runs after a pick.
- `Components/Pages/Settings.razor`: uses `<PlaceList OnSelected="Close" />`; its own copy of the rows, `Locations`,
  `Subtitle`, `Select`, `DevicePlaceholder` and the `loc-*` styles move to `PlaceList`.
- `Services/PlacePicker.cs`: singleton with `IsOpen`, `Open()`, `Close()` (returns whether it was open), `Changed`.
- `Components/Shared/QuickPlaces.razor` (+ `.razor.css`): the button and the sheet, rendered in `MainLayout`. While
  open, a `NavigationLock` turns any navigation (Back with WebView history) into closing the sheet.
- `MainPage.xaml.cs` `OnBackButtonPressed`: closes the picker first when it is open (Back without WebView history).
- `WeatherIcons.Pin`: the location-pin path.

## Checking

On the emulator: the button on every main tab, not in Settings; the sheet lists the same places as Settings, device
row highlighted in both; picking a place switches the forecast and closes the sheet; ✕, tapping outside and Back
close it without a change; Settings still selects places and goes back.
