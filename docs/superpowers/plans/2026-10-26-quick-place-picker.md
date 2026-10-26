# Quick place picker — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A floating button (bottom right, main tabs) opens a sheet with the same place list as Settings; the device position row is set apart in both.

**Architecture:** The Settings place rows become a shared `PlaceList` component. A `PlacePicker` singleton holds whether the sheet is open, so the Android Back handler can close it; `QuickPlaces` (button + sheet) lives in `MainLayout`.

**Tech Stack:** .NET 10 MAUI Blazor Hybrid (Android), Razor components with scoped CSS.

Spec: `docs/superpowers/specs/2026-10-26-quick-place-picker-design.md`. Branch: `quick-place-picker`.

Builds: `dotnet build VibeWeather.csproj -c Debug -nologo` from `VibeWeather/` must end with `Počet chyb: 0` (NuGet feed errors from the private credential provider are noise; rerun once if restore alone fails).

---

### Task 1: Shared `PlaceList`, device row set apart

**Files:** Create `VibeWeather/Components/Shared/PlaceList.razor`, `PlaceList.razor.css`; modify `VibeWeather/Data/WeatherIcons.cs`, `VibeWeather/Components/Pages/Settings.razor`, `Settings.razor.css`.

- [ ] Add to `WeatherIcons` (after `Search`): `public const string Pin = "M12 21s-6.5-5.6-6.5-11a6.5 6.5 0 0 1 13 0c0 5.4-6.5 11-6.5 11zM12 7.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5";` with a one-line doc comment.
- [ ] Create `PlaceList.razor`: the `@foreach` rows from Settings (button `row loc-row`, plus class `device` for `loc.IsDevice`, pin icon before the device name), and in `@code`: `OnSelected` parameter, `DevicePlaceholder`, `Locations`, `Subtitle`, `Select` (moved from Settings; `Select` invokes `OnSelected` instead of `Close`), `OnInitializedAsync` subscribing `State.Changed` and calling `State.LoadSummariesAsync()`, `Dispose`.
- [ ] Create `PlaceList.razor.css`: the `.loc-row`, `.loc-check`, `.loc-name-line`, `.loc-name`, `.loc-temp` rules moved from `Settings.razor.css`, plus:

```css
/* The device position, always first: tinted across the card's width (its 16 px padding) and marked with a pin. */
.loc-row.device {
    width: calc(100% + 32px);
    margin: 0 -16px;
    padding: 0 16px;
    background: var(--acc-soft);
}

    .loc-row.device:first-child {
        border-radius: 18px 18px 0 0;
    }

.loc-name-line ::deep .loc-pin {
    flex: none;
    color: var(--acc);
}
```

- [ ] In `Settings.razor` replace the `@foreach (var (loc, fence) in Locations) { … }` block with `<PlaceList OnSelected="Close" />`; delete `DevicePlaceholder`, `Locations`, `Subtitle`, `Select`; remove `await State.LoadSummariesAsync();` from `OnInitializedAsync` (PlaceList loads them). Delete the moved rules from `Settings.razor.css`.
- [ ] Build → `Počet chyb: 0`. Commit "Settings: place rows as a shared PlaceList; device row set apart".

### Task 2: `PlacePicker` state and Back

**Files:** Create `VibeWeather/Services/PlacePicker.cs`; modify `VibeWeather/MauiProgram.cs`, `VibeWeather/MainPage.xaml.cs`.

- [ ] `PlacePicker`: `public bool IsOpen { get; private set; }`, `public event Action? Changed;`, `Open()` (no-op when open), `bool Close()` (false when already closed). Doc: the quick place picker's sheet; Back closes it first.
- [ ] Register `builder.Services.AddSingleton<PlacePicker>();` next to `WeatherState`.
- [ ] `MainPage.OnBackButtonPressed`: first `if (Handler?.MauiContext?.Services.GetService<PlacePicker>()?.Close() == true) return true;`, comment why.
- [ ] Build; commit "PlacePicker: open state, closed first by Back".

### Task 3: `QuickPlaces` button and sheet

**Files:** Create `VibeWeather/Components/Shared/QuickPlaces.razor`, `QuickPlaces.razor.css`; modify `VibeWeather/Components/Layout/MainLayout.razor`, `MainLayout.razor.css`.

- [ ] `QuickPlaces.razor`: the `quick-btn` button (pin icon, `aria-label="Změnit místo"`, `@onclick` → `Picker.Open`); when `Picker.IsOpen`: `<NavigationLock OnBeforeInternalNavigation="CloseInstead" />`, a `quick-backdrop` (click → close), and a `quick-sheet` section with header "Místa" + ✕ (`WeatherIcons.TabClose`) and `<div class="group-card"><PlaceList OnSelected="Close" /></div>`. `CloseInstead(LocationChangingContext c)` calls `c.PreventNavigation()` and closes. Subscribe/unsubscribe `Picker.Changed`.
- [ ] `QuickPlaces.razor.css`: button `position: absolute; right: 16px; bottom: 80px; z-index: 30;` 52 px round, `background: var(--acc)`, white icon, shadow; backdrop `absolute; inset: 0; z-index: 40; rgba(0,0,0,.35)` with a fade-in; sheet `absolute; left/right/bottom: 0; z-index: 41; max-height: 80%; overflow-y: auto; padding: 8px 16px 24px; border-radius: 22px 22px 0 0; background: var(--bg)` sliding up; header flex with title 20 px/600 and a 40 px round ✕ on `var(--surf)`.
- [ ] `MainLayout.razor`: `<QuickPlaces />` between `<TabBar />` and `<ErrorDialog />`. `MainLayout.razor.css`: `.app-content` bottom padding 24px → 92px (button 52 + 16 margin + 24).
- [ ] Build; commit "Quick place picker: floating button and sheet".

### Task 4: Emulator check

- [ ] Deploy (`dotnet build VibeWeather.csproj -t:Run -f net10.0-android -c Debug -p:AdbTarget="-s emulator-5554" -p:RuntimeIdentifier=android-x64 -nologo`).
- [ ] Button on Teď / Příští hodina / Po hodinách / 16 dní, bottom right above the tab bar, not covering the last card when scrolled down; not in Settings.
- [ ] Tap → sheet with the same rows as Settings; device row tinted with a pin; ✓ on the selected place.
- [ ] Pick another place → sheet closes, header shows the new place, forecast reloads.
- [ ] Reopen; ✕ closes; reopen, tap the dimmed area closes; reopen, Android Back (`adb shell input keyevent 4`) closes, also on a tab reached from another (WebView history).
- [ ] Settings: device row tinted with a pin; picking a place still goes back.
- [ ] Fix, commit "Quick place picker: fixes from the emulator check" if anything changed.
