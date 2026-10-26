# Hourly forecast over all days, with a day scrubber — design

Date: 2026-10-26 · App: VibeWeather (.NET MAUI Blazor, Android), UI in Czech

## Goal

"Po hodinách" shows the hourly forecast for as many days as the weather source offers, not just 72 hours. A scrubber
under the chart moves along it by day. Tapping a day in "16 dní" opens "Po hodinách" at that day, when hourly data
for it exists.

## Decisions

| Topic | Decision |
|---|---|
| Hours per source | Open-Meteo: 384 hours (16 days, `forecast_hours`). Azure Maps: 240 hours (`duration=240`, its maximum, 10 days); its daily forecast still has 15 days. |
| Day seeker | A scrubber bar under the chart card (not chips or arrows). |
| Hour detail card | Unchanged: always the current hour ("TEĎ"). |
| Days without hourly data | Their "16 dní" rows aren't tappable and have no `›`. |
| Temperature curve | One scale across all hours shown, as today. |
| Scroll handling | In JavaScript: scroll events don't round-trip to .NET. Blazor renders the scrubber and asks for the first jump. |

## Data

- `OpenMeteoWeatherProvider.HourCount` 72 → 384. The request asks for `HourCount + 1` hours as today.
- `AzureMapsWeatherProvider`: hourly request `duration=240`; `MapHours` takes up to 239 forecast hours after the
  current one (today 71).
- `IWeatherProvider` summary: "next 72 hours" → "hourly up to 16 days (10 from Azure Maps)".
- Other users of `Forecast.Hours` filter by time (morning summary: today; precipitation alerts: the next dry hour,
  bounded by their own look-ahead), so more hours don't change them.
- A longer Open-Meteo request may count somewhat more against the free tier's limit (it weighs requests by days and
  variables); it is still one request per forecast.

## Po hodinách

- Heading: "PŘÍŠTÍCH N DNÍ" from the number of calendar days the hours touch (`Czech.Count`: "PŘÍŠTÍ 3 DNY",
  "PŘÍŠTÍCH 16 DNÍ"), instead of the fixed "PŘÍŠTÍ 3 DNY". The headline (max/min of the next 24 hours) is unchanged.
- The chart track is unchanged apart from its length: 56 px per hour, day labels on each day's first column.
- The page takes `?day=yyyy-MM-dd`. When that day has hours, the chart opens scrolled so the day's first hour is at the
  left edge; otherwise (no parameter, unknown or past day) it starts at now, as today.

### Scrubber

A card under the chart card:

```
 Dnes  Út   St   Čt   Pá   So   Ne   Po  …
  26   27   28   29   30   31    1    2
 ├────┼────┼────┼────┼────┼────┼────┼────┤
   [▓▓▓▓▓]
```

- The track is the whole chart's length; each day is a segment as wide as its share of the hours (today and the last
  day are usually partial). Labels: weekday ("Dnes" for today) over the day of the month; segments too narrow for a
  label leave it out.
- A thumb shows the part of the chart on screen: its left at `scrollLeft / scrollWidth`, its width
  `clientWidth / scrollWidth` of the track.
- Scrolling the chart moves the thumb.
- Dragging the thumb scrolls the chart in proportion.
- Tapping a day segment scrolls to that day's first hour (smoothly).
- The day under the thumb's left edge is shown as the current one (stronger label).

## 16 dní

- A row whose date has hourly data is a link to `hourly?day=yyyy-MM-dd`, with a small `›` after the high temperature.
- Today's row stays open with its details, as now; tapping it opens "Po hodinách" at now.
- Rows without hourly data are as today.

## Code

- `ForecastView`:
  - `HourDay(DateOnly Date, string Weekday, string DayOfMonth, int FirstColumn, string Left, string Width)` — one per
    calendar day in the hours, Left/Width in percent of the track; `HourDays` list.
  - `HourHeading` ("PŘÍŠTÍCH 16 DNÍ").
  - `DayRow.HourlyLink`: `hourly?day=…` when the day has hours, else null.
- `wwwroot/app.js` — `vibeWeather.hourScrubber`:
  - `attach(scroller, scrubber)`: scroll listener (passive) that positions the thumb and marks the current day;
    pointer handlers on the thumb (drag) and segments (jump). Idempotent; detached with the elements.
  - `scrollToColumn(scroller, column, smooth)`: `scrollLeft = column × 56`.
- `Hourly.razor`: `[SupplyParameterFromQuery] Day`; after the first render attaches the scrubber and, when Day matches
  an `HourDay`, scrolls to its first column (not smooth). The scrubber markup and styles live in `Hourly.razor` /
  `Hourly.razor.css`.
- `Daily.razor`: rows with `HourlyLink` render as `<a>` (same look) with the `›`.

## Checking

Build, then on the emulator (`emulator-5554`):

1. "Po hodinách" shows 16 days (Open-Meteo); the heading says so; scrolling stays smooth with 384 columns.
2. Scrolling the chart moves the thumb; dragging the thumb scrolls the chart; tapping a day jumps to its first hour.
3. In "16 dní", tapping a later day opens "Po hodinách" at that day; the tab bar's "Po hodinách" opens at now.
4. With Azure Maps as the source (if a key is set): 10 days of hours; rows 11–15 in "16 dní" aren't tappable.
