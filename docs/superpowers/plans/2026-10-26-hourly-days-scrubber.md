# Hourly forecast over all days, with a day scrubber — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Po hodinách" shows every day the source has hours for (16 from Open-Meteo, 10 from Azure Maps), with a day scrubber under the chart; tapping a day in "16 dní" opens "Po hodinách" at that day.

**Architecture:** `ForecastView` gains the per-day data (`HourDays`, `HourHeading`, `DayRow.HourlyLink`), covered by a file-based C# check in `tests/`. The providers fetch more hours. The scrubber is rendered by Blazor in `Hourly.razor`, but all scroll/drag handling lives in `wwwroot/app.js` (`vibeWeather.hourScrubber`) so scrolling never round-trips to .NET. `Daily.razor` rows with hours navigate to `hourly?day=yyyy-MM-dd`.

**Tech Stack:** .NET 10 MAUI Blazor Hybrid (Android), Razor components with scoped CSS, plain JS in `wwwroot/app.js`, file-based C# checks (`dotnet run x.cs`).

Spec: `docs/superpowers/specs/2026-10-26-hourly-days-scrubber-design.md`.

**Notes for the engineer**

- Work on branch `hourly-days-scrubber` (already created, spec committed).
- Builds print many `NU1608`/`NU1900` warnings and sometimes `NuGet.targets … error` lines from a private feed's credential provider. They are noise as long as the build ends with `Počet chyb: 0` (Czech "Errors: 0"). If a first `dotnet run` of a new check fails on restore only, run it again.
- UI text is Czech. Match the comment style of the surrounding code (short, explains why).
- Line endings: files are LF in the working tree; keep them.

---

## File map

| File | Change |
|---|---|
| `VibeWeather/Data/ForecastView.cs` | `HourDay` record, `HourDays`, `HourHeading`, `LabelledDayHours`, `DayRow.HourlyLink` |
| `tests/hourly-days.cs` | New check for the above |
| `VibeWeather/Services/OpenMeteo/OpenMeteoWeatherProvider.cs` | 384 hours, cut to the daily forecast's last day |
| `VibeWeather/Services/AzureMaps/AzureMapsWeatherProvider.cs` | `duration=240`, 240 hours |
| `VibeWeather/Services/IWeatherProvider.cs` | Summary text |
| `VibeWeather/wwwroot/app.js` | `vibeWeather.hourScrubber` |
| `VibeWeather/Components/Pages/Hourly.razor` (+ `.razor.css`) | Heading, chart card split (scroller vs legend), scrubber card, `?day=` |
| `VibeWeather/Components/Pages/Daily.razor` (+ `.razor.css`) | Rows with hours navigate; `›` marker |

---

### Task 1: ForecastView — hourly days, heading, links

**Files:**
- Create: `tests/hourly-days.cs`
- Modify: `VibeWeather/Data/ForecastView.cs`

- [ ] **Step 1: Write the failing check**

Create `tests/hourly-days.cs`:

```csharp
#:include ../VibeWeather/Data/ForecastView.cs
#:include ../VibeWeather/Data/WeatherCodes.cs
#:include ../VibeWeather/Data/WeatherIcons.cs
#:include ../VibeWeather/Data/WeatherModels.cs
#:include ../VibeWeather/Data/DisplayUnits.cs
#:include ../VibeWeather/Data/Czech.cs
// Checks ForecastView's hourly days (scrubber segments, heading, links from the daily rows): run with
// `dotnet run hourly-days.cs` from this folder. Exit code 1 on failure.
using VibeWeather.Data;

int failures = 0;
void Check(string what, bool ok) { Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

// Monday 26 October 2026, 20:30; the hours start at 20:00.
var now = new DateTime(2026, 10, 26, 20, 30, 0);
var start = new DateTime(2026, 10, 26, 20, 0, 0);

ForecastView View(int hourCount, int dayCount)
{
    var hours = Enumerable.Range(0, hourCount).Select(i => new HourForecast(
        start.AddHours(i), 10, 9, 1, true, 0, 5, 80, 5, 0, 1015, 0)).ToList();
    var days = Enumerable.Range(0, dayCount).Select(i =>
    {
        var date = DateOnly.FromDateTime(now).AddDays(i);
        return new DayForecast(date, 1, 12, 4, 0, 10, 180, 1, 0,
            date.ToDateTime(new TimeOnly(7, 0)), date.ToDateTime(new TimeOnly(17, 0)));
    }).ToList();
    var current = new CurrentConditions(now, 10, 9, 1, false, 80, 5, 5, 8, 180, 1015, 10, 0);
    var forecast = new Forecast(new WeatherLocation("Ostrava", "Česko", 49.83, 18.28), now, current, hours, [], days);
    return new ForecastView(forecast, DisplayUnits.Default);
}

// 52 hours: 4 today (20:00–23:00), all of Tuesday and Wednesday; the daily forecast has a fourth day without hours.
var v = View(52, 4);
Check("three days", v.HourDays.Count == 3);
Check("today first, from now", v.HourDays[0] is { Weekday: "Dnes", DayOfMonth: "26", FirstColumn: 0, Left: "0.0", Width: "7.7" });
Check("tomorrow", v.HourDays[1] is { Weekday: "Út", DayOfMonth: "27", FirstColumn: 4, Left: "7.7", Width: "46.2" });
Check("third day", v.HourDays[2] is { Weekday: "St", DayOfMonth: "28", FirstColumn: 28, Left: "53.8", Width: "46.2" });
Check("short today has no label", !v.HourDays[0].Labelled && v.HourDays[1].Labelled);
Check("heading for three days", v.HourHeading == "PŘÍŠTÍ 3 DNY");
Check("daily row with hours links to its day", v.DayRows[1].HourlyLink == "hourly?day=2026-10-27");
Check("today's row links too", v.DayRows[0].HourlyLink == "hourly?day=2026-10-26");
Check("daily row without hours has no link", v.DayRows[3].HourlyLink is null);

// 16 days of hours: 4 today, then 15 full days.
v = View(4 + 15 * 24, 16);
Check("sixteen days", v.HourDays.Count == 16);
Check("heading for sixteen days", v.HourHeading == "PŘÍŠTÍCH 16 DNÍ");
Check("every daily row links", v.DayRows.All(d => d.HourlyLink is not null));

v = View(0, 3);
Check("no hours: no days, no links", v.HourDays.Count == 0 && v.DayRows.All(d => d.HourlyLink is null));

Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
```

- [ ] **Step 2: Run it to see it fail**

Run from `tests/`: `dotnet run hourly-days.cs`
Expected: build fails with `CS1061`-style errors: `ForecastView` has no `HourDays` / `HourHeading`, `DayRow` has no `HourlyLink`.

- [ ] **Step 3: Add the record and constant**

In `VibeWeather/Data/ForecastView.cs`, after the `GridLine` record (`public sealed record GridLine(string Label, int Bottom);`), add:

```csharp
    /// <summary>
    /// A calendar day in the hourly chart, for its scrubber: Weekday "Dnes" or "Út" over DayOfMonth "27"; FirstColumn
    /// is its first hour's column, Left and Width where its segment sits along the track (percent). Labelled when it
    /// has hours enough (<see cref="LabelledDayHours"/>) for its labels to fit.
    /// </summary>
    public sealed record HourDay(DateOnly Date, string Weekday, string DayOfMonth, int FirstColumn, string Left, string Width, bool Labelled);
```

After `const int HourColumnWidth = 56;` add:

```csharp
    // A day with fewer hours than this (today late, the last day early) is too narrow in the scrubber for its labels.
    const int LabelledDayHours = 12;
```

- [ ] **Step 4: Add `HourlyLink` to `DayRow`**

Replace the `DayRow` doc's last `<param>` line and the record declaration:

```csharp
    /// <param name="Wind">The day's strongest wind, "13 km/h"; WindTo is where it blows to (arrow rotation, degrees).</param>
    /// <param name="HourlyLink">"hourly?day=2026-10-27" when the hourly forecast reaches the day; else null.</param>
    public sealed record DayRow(
        string Name, string Date, WeatherCondition Condition, string Pop, string Low, string High, string LowColor, string HighColor,
        string RangeLeft, string RangeWidth, bool IsToday, string NowLeft,
        bool Open, string Summary, IReadOnlyList<Stat> Stats, IReadOnlyList<DayPrecipLine> Precip,
        string? WetColor, string? WetMix, string Wind, int WindTo, bool NewWeek, string? HourlyLink);
```

- [ ] **Step 5: Add the properties**

In the `// Hourly (three days)` property block, change the comment to `// Hourly (as many days as the source has hours for)` and add after `public string HourHeadline { get; }`:

```csharp
    /// <summary>"PŘÍŠTÍ 3 DNY", "PŘÍŠTÍCH 16 DNÍ": how many calendar days the hours reach into.</summary>
    public string HourHeading { get; }
    /// <summary>The calendar days in <see cref="HourColumns"/>, in order.</summary>
    public IReadOnlyList<HourDay> HourDays { get; }
```

- [ ] **Step 6: Compute them**

In the constructor, right after the `HourLegend = …;` statement, add:

```csharp
        // One scrubber segment per calendar day, as wide as its share of the hours.
        HourDays = hours.Select((h, i) => (Date: DateOnly.FromDateTime(h.Time), Column: i))
            .GroupBy(x => x.Date)
            .Select(g => new HourDay(g.Key,
                g.Key == DateOnly.FromDateTime(now) ? "Dnes" : DayOfWeek(g.Key),
                g.Key.Day.ToString(Inv),
                g.First().Column,
                Pct((double)g.First().Column / hours.Count),
                Pct((double)g.Count() / hours.Count),
                g.Count() >= LabelledDayHours)).ToList();
        HourHeading = HourDays.Count switch
        {
            <= 1 => "DNES",
            < 5 => $"PŘÍŠTÍ {HourDays.Count} DNY",
            _ => $"PŘÍŠTÍCH {HourDays.Count} DNÍ",
        };
```

In the `DayRows = days.Select((d, i) => new DayRow(` call, replace its last argument

```csharp
            NewWeek: i > 0 && d.Date.DayOfWeek == System.DayOfWeek.Monday)).ToList();
```

with

```csharp
            NewWeek: i > 0 && d.Date.DayOfWeek == System.DayOfWeek.Monday,
            HourlyLink: HourDays.Any(h => h.Date == d.Date) ? $"hourly?day={d.Date.ToString("yyyy-MM-dd", Inv)}" : null)).ToList();
```

- [ ] **Step 7: Run the check**

Run from `tests/`: `dotnet run hourly-days.cs`
Expected: every line `ok`, last line `ALL OK`, exit code 0.

- [ ] **Step 8: Commit**

```bash
git add tests/hourly-days.cs VibeWeather/Data/ForecastView.cs
git commit -m "ForecastView: hourly days for the scrubber, heading, links from daily rows

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Providers — more hours

**Files:**
- Modify: `VibeWeather/Services/OpenMeteo/OpenMeteoWeatherProvider.cs`
- Modify: `VibeWeather/Services/AzureMaps/AzureMapsWeatherProvider.cs`
- Modify: `VibeWeather/Services/IWeatherProvider.cs`

Background: Open-Meteo accepts `forecast_hours` up to 384 (385 → error "Allowed range 0 to 384"). With `forecast_days=16`, the 384 hours from now run about a day past the last daily date and end with nulls. The app shows each hour's precipitation from the *next* entry, so the last entry is never shown itself. Azure Maps' hourly `duration` accepts 1, 12, 24, 72, 120, 240.

- [ ] **Step 1: Open-Meteo hour count and request**

Replace

```csharp
    /// <summary>Three days of hours, for the Hourly tab.</summary>
    const int HourCount = 72;
```

with

```csharp
    /// <summary>The API's maximum, for the Hourly tab: 16 days of hours from now, cut to the daily forecast's days.</summary>
    const int HourCount = 384;
```

and in the URL replace `&forecast_hours={HourCount + 1}` with `&forecast_hours={HourCount}`.

- [ ] **Step 2: Open-Meteo mapping ends with the last daily day**

In `GetForecastAsync`, replace

```csharp
        return new Forecast(
            location,
            localNow,
            MapCurrent(response.Current),
            MapHours(response.Hourly),
            MapMinutes(response.Minutely15, localNow),
            MapDays(response.Daily));
```

with

```csharp
        var days = MapDays(response.Daily);
        return new Forecast(
            location,
            localNow,
            MapCurrent(response.Current),
            MapHours(response.Hourly, days.Count > 0 ? days[^1].Date : DateOnly.MaxValue),
            MapMinutes(response.Minutely15, localNow),
            days);
```

Replace the comment and the first two lines of `MapHours`:

```csharp
    // The API's precipitation and snowfall are totals for the hour ending at each time; the app's are for the
    // hour starting at it (as Azure Maps reports them), so they come from the next entry. Hence one hour more
    // is requested than shown.
    static List<HourForecast> MapHours(HourlyBlock h) =>
        h.Time.Take(HourCount).Select((time, i) => new HourForecast(
            ParseTime(time),
```

with

```csharp
    // The API's precipitation and snowfall are totals for the hour ending at each time; the app's are for the
    // hour starting at it (as Azure Maps reports them), so they come from the next entry, and the last entry isn't
    // shown itself. The hours reach about a day past the daily forecast, ending without data, so they stop after
    // its last day.
    static List<HourForecast> MapHours(HourlyBlock h, DateOnly lastDay) =>
        h.Time.Take(h.Time.Length - 1).Select(ParseTime)
            .TakeWhile((time, i) => DateOnly.FromDateTime(time) <= lastDay && h.Temperature.ElementAtOrDefault(i) is not null)
            .Select((time, i) => new HourForecast(
            time,
```

(The rest of the `new HourForecast(…)` arguments stay as they are; `i` is still the entry's index because `TakeWhile` only drops from the end.)

- [ ] **Step 3: Azure Maps 240 hours**

In `AzureMapsWeatherProvider.cs`:
- In the hourly request URL replace `&duration=72` with `&duration=240`.
- In `MapHours` replace

```csharp
        // Three days in all, for the Hourly tab.
        var next = hourly.Where(h => h.Date.DateTime > thisHour).Take(71).ToList();
```

with

```csharp
        // Ten days in all (the service's longest hourly forecast), for the Hourly tab.
        var next = hourly.Where(h => h.Date.DateTime > thisHour).Take(239).ToList();
```

- In the class summary (around line 10) replace `72 hours` with `240 hours`.

- [ ] **Step 4: Interface summary**

In `IWeatherProvider.cs` replace

```csharp
    /// <summary>Full forecast: current conditions, the next 60 (or 120) minutes, next 72 hours and 16 days (15 from Azure Maps).</summary>
```

with

```csharp
    /// <summary>Full forecast: current conditions, the next 60 (or 120) minutes, hours up to 16 days ahead (10 from Azure Maps) and 16 days (15 from Azure Maps).</summary>
```

- [ ] **Step 5: Build**

Run from `VibeWeather/`: `dotnet build -c Debug -nologo 2>&1 | tail -3`
Expected: `Počet chyb: 0`.

- [ ] **Step 6: Re-run the check** (ForecastView is unchanged, but make sure nothing else broke)

Run from `tests/`: `dotnet run hourly-days.cs` → `ALL OK`.

- [ ] **Step 7: Commit**

```bash
git add VibeWeather/Services/OpenMeteo/OpenMeteoWeatherProvider.cs VibeWeather/Services/AzureMaps/AzureMapsWeatherProvider.cs VibeWeather/Services/IWeatherProvider.cs
git commit -m "Hourly forecast for 16 days (Open-Meteo) and 10 days (Azure Maps)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Scrubber behaviour in app.js

**Files:**
- Modify: `VibeWeather/wwwroot/app.js`

The markup it expects (written in Task 4): a scroller element (`.hours-scroll`, its first child the 56 px-per-hour track) and a scrubber element containing `.scrub-day` buttons with `data-column`, a `.scrub-track`, and inside it a `.scrub-thumb`.

- [ ] **Step 1: Add `hourScrubber`**

In `window.vibeWeather = { … }`, after the `width:` entry, add:

```js
    // The day scrubber under the hourly chart (see Hourly.razor): the thumb shows the part of the chart on screen and
    // follows its scrolling; dragging the thumb scrolls the chart, a tap on the track centres the chart there, a tap on
    // a day goes to its first hour. Handled here so scrolling never waits on .NET.
    hourScrubber: {
        // Called after every render; sets up once per scrubber element, later only refreshes the thumb.
        attach: (scroller, scrubber) => {
            if (scrubber.vwUpdate) { scrubber.vwUpdate(); return; }
            const track = scrubber.querySelector('.scrub-track');
            const thumb = scrubber.querySelector('.scrub-thumb');

            const update = () => {
                const total = scroller.scrollWidth;
                if (!total) return;
                thumb.style.left = `${scroller.scrollLeft / total * 100}%`;
                thumb.style.width = `${Math.min(1, scroller.clientWidth / total) * 100}%`;
                // The day at the chart's left edge is the current one (56 px per hour column).
                const at = Math.round(scroller.scrollLeft / 56);
                let current = null;
                for (const day of scrubber.querySelectorAll('.scrub-day')) {
                    day.classList.remove('current');
                    if (+day.dataset.column <= at) current = day;
                }
                current?.classList.add('current');
            };
            scrubber.vwUpdate = update;
            scroller.addEventListener('scroll', update, { passive: true });
            // New data or a rotated screen change the widths without a scroll.
            const resized = new ResizeObserver(update);
            resized.observe(scroller);
            resized.observe(scroller.firstElementChild);
            update();

            scrubber.addEventListener('click', e => {
                const day = e.target.closest('.scrub-day');
                if (day) vibeWeather.hourScrubber.scrollToColumn(scroller, +day.dataset.column, true);
            });

            // Dragging the thumb moves the chart by as much of its length as the thumb moves along the track.
            let drag = null;
            thumb.addEventListener('pointerdown', e => {
                drag = { x: e.clientX, left: scroller.scrollLeft };
                thumb.setPointerCapture(e.pointerId);
                e.stopPropagation();
            });
            thumb.addEventListener('pointermove', e => {
                if (drag) scroller.scrollLeft = drag.left + (e.clientX - drag.x) / track.clientWidth * scroller.scrollWidth;
            });
            const release = () => { drag = null; };
            thumb.addEventListener('pointerup', release);
            thumb.addEventListener('pointercancel', release);

            track.addEventListener('pointerdown', e => {
                if (e.target === thumb) return;
                const rect = track.getBoundingClientRect();
                const x = (e.clientX - rect.left) / rect.width * scroller.scrollWidth;
                scroller.scrollTo({ left: x - scroller.clientWidth / 2, behavior: 'smooth' });
            });
        },

        // Puts an hour column at the chart's left edge (56 px per column).
        scrollToColumn: (scroller, column, smooth) =>
            scroller.scrollTo({ left: column * 56, behavior: smooth ? 'smooth' : 'auto' }),
    },
```

Note: functions refer to `vibeWeather.hourScrubber` by name, not `this` — Blazor's JS interop doesn't guarantee `this`.

- [ ] **Step 2: Commit**

```bash
git add VibeWeather/wwwroot/app.js
git commit -m "app.js: hour scrubber (thumb follows the chart, drag, tap to jump)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Behaviour is checked on the emulator in Task 6, once the markup exists.)

---

### Task 4: Hourly page — heading, scrubber, `?day=`

**Files:**
- Modify: `VibeWeather/Components/Pages/Hourly.razor`
- Modify: `VibeWeather/Components/Pages/Hourly.razor.css`

- [ ] **Step 1: Page header and injections**

At the top of `Hourly.razor` replace

```razor
@page "/hourly"

<div class="stack">
    <div class="page-intro">
        <span class="mono-label">PŘÍŠTÍ 3 DNY</span>
```

with

```razor
@page "/hourly"
@using System.Globalization
@inject IJSRuntime JS

<div class="stack">
    <div class="page-intro">
        <span class="mono-label">@Weather.HourHeading</span>
```

- [ ] **Step 2: Split the chart card: scroller inside, legend outside**

The legend used to sit inside the scrolling element and scrolled away with the chart; with 16 days that matters. Replace

```razor
    <section class="card hours-scroll">
        <div class="hours-track" style="width:@(Weather.HourTrackWidth)px">
```

with

```razor
    <section class="card">
        <div class="hours-scroll" @ref="scroller">
        <div class="hours-track" style="width:@(Weather.HourTrackWidth)px">
```

and, after the closing `</svg>` and its track's `</div>`, close the scroller before the legend — i.e. replace

```razor
            </svg>
        </div>
        @* Bar colours: one hue per kind of precipitation, deeper as it gets heavier; then the wind's unit. *@
```

with

```razor
            </svg>
        </div>
        </div>
        @* Bar colours: one hue per kind of precipitation, deeper as it gets heavier; then the wind's unit. *@
```

Then re-indent the track block one level (4 spaces) so it sits inside `.hours-scroll`.

- [ ] **Step 3: Scrubber card**

Directly after that chart card's closing `</section>` (before `<section class="card hour-detail">`), add:

```razor
    @if (Weather.HourDays.Count > 1)
    {
        @* Moves along the chart by day; app.js keeps the thumb on the part of the chart on screen. *@
        <section class="card scrubber" @ref="scrubber">
            <div class="scrub-days">
                @foreach (var day in Weather.HourDays)
                {
                    <button type="button" class="scrub-day" data-column="@day.FirstColumn" style="left:@(day.Left)%;width:@(day.Width)%"
                            aria-label="@day.Weekday @day.DayOfMonth.">
                        @if (day.Labelled)
                        {
                            <span>@day.Weekday</span>
                            <span class="scrub-date">@day.DayOfMonth</span>
                        }
                    </button>
                }
            </div>
            <div class="scrub-track">
                @foreach (var day in Weather.HourDays.Skip(1))
                {
                    <span class="scrub-tick" style="left:@(day.Left)%"></span>
                }
                <div class="scrub-thumb"></div>
            </div>
        </section>
    }
```

- [ ] **Step 4: Code block**

Replace

```razor
@code {
    [CascadingParameter] public ForecastView Weather { get; set; } = default!;
}
```

with

```razor
@code {
    [CascadingParameter] public ForecastView Weather { get; set; } = default!;

    /// <summary>"2026-10-28" (from a day in "16 dní"): the chart opens at that day's first hour.</summary>
    [SupplyParameterFromQuery] public string? Day { get; set; }

    ElementReference scroller, scrubber;
    // The Day the chart was last scrolled for; "" before the first render.
    string? shownDay = "";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Weather.HourDays.Count > 1)
            await JS.InvokeVoidAsync("vibeWeather.hourScrubber.attach", scroller, scrubber);
        if (Day == shownDay) return;
        // A first render starts at now anyway; later, the tab bar after a day link goes back to now too.
        int column = FirstColumn(Day);
        if (column > 0 || !firstRender)
            await JS.InvokeVoidAsync("vibeWeather.hourScrubber.scrollToColumn", scroller, column, !firstRender);
        shownDay = Day;
    }

    /// <summary>The first column of the day "yyyy-MM-dd" when the chart has it; else 0 (now).</summary>
    int FirstColumn(string? day) =>
        DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && Weather.HourDays.FirstOrDefault(d => d.Date == date) is { } found ? found.FirstColumn : 0;
}
```

- [ ] **Step 5: Styles**

In `Hourly.razor.css`, replace the `.legend` rule's padding line `padding: 4px 12px 10px;` with `padding: 4px 18px 16px;` (it no longer sits inside the scroller's 6 px padding).

Append at the end of the file:

```css
/* Under the chart: the days along the whole chart, and the thumb on the part of it on screen (moved by app.js). */
.scrubber {
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding: 12px 18px 16px;
}

.scrub-days {
    position: relative;
    height: 28px;
}

/* Each day over its share of the track; a tap goes to its first hour. */
.scrub-day {
    position: absolute;
    top: 0;
    bottom: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: flex-end;
    gap: 1px;
    padding: 0;
    border: 0;
    background: none;
    font: 500 10px var(--font-mono);
    color: var(--mut);
    white-space: nowrap;
}

    /* The day at the chart's left edge. */
    .scrub-day.current {
        color: var(--ink);
        font-weight: 700;
    }

.scrub-date {
    font-size: 9.5px;
    opacity: .75;
}

/* Taps and drags here move the chart, not the page. */
.scrub-track {
    position: relative;
    height: 20px;
    border-radius: 10px;
    background: var(--dry);
    touch-action: none;
}

/* Midnight between two days. */
.scrub-tick {
    position: absolute;
    top: 5px;
    bottom: 5px;
    width: 1px;
    background: var(--mut);
    opacity: .4;
}

.scrub-thumb {
    position: absolute;
    top: 0;
    bottom: 0;
    min-width: 12px;
    border: 2px solid var(--acc);
    border-radius: 10px;
    background: var(--acc-soft);
    touch-action: none;
}
```

- [ ] **Step 6: Build**

Run from `VibeWeather/`: `dotnet build -c Debug -nologo 2>&1 | tail -3` → `Počet chyb: 0`.

- [ ] **Step 7: Commit**

```bash
git add VibeWeather/Components/Pages/Hourly.razor VibeWeather/Components/Pages/Hourly.razor.css
git commit -m "Po hodinách: day scrubber, heading by day count, open at ?day=

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 16 dní — rows open the hourly chart

**Files:**
- Modify: `VibeWeather/Components/Pages/Daily.razor`
- Modify: `VibeWeather/Components/Pages/Daily.razor.css`

- [ ] **Step 1: Navigation on tap**

At the top of `Daily.razor`, after `@page "/daily"`, add `@inject NavigationManager Navigation`.

Replace the row's opening tag

```razor
            <div class="day @(day.WetColor is null ? "" : "wet") @(day.NewWeek ? "new-week" : "")" style="@DayStyle(day)">
```

with

```razor
            <div class="day @(day.WetColor is null ? "" : "wet") @(day.NewWeek ? "new-week" : "") @(day.HourlyLink is null ? "" : "linked")"
                 style="@DayStyle(day)" @onclick="() => Open(day)">
```

Inside `.day-row`, after `<span class="day-high">@day.High</span>`, add:

```razor
                    @if (day.HourlyLink is not null)
                    {
                        <span class="day-go" aria-hidden="true">›</span>
                    }
```

In `@code`, after the `[CascadingParameter]` line, add:

```csharp
    // Days the hourly forecast reaches open "Po hodinách" there.
    void Open(ForecastView.DayRow day)
    {
        if (day.HourlyLink is { } link) Navigation.NavigateTo(link);
    }
```

- [ ] **Step 2: Styles**

In `Daily.razor.css`, add `position: relative;` to the existing `.day-row` rule, and append:

```css
/* A day the hourly forecast reaches: a tap opens it in "Po hodinách"; the › at the card's edge says so. */
.day.linked {
    cursor: pointer;
}

.day-go {
    position: absolute;
    top: 50%;
    right: -12px;
    transform: translateY(-50%);
    font-size: 16px;
    color: var(--mut);
}
```

- [ ] **Step 3: Build**

Run from `VibeWeather/`: `dotnet build -c Debug -nologo 2>&1 | tail -3` → `Počet chyb: 0`.

- [ ] **Step 4: Commit**

```bash
git add VibeWeather/Components/Pages/Daily.razor VibeWeather/Components/Pages/Daily.razor.css
git commit -m "16 dní: days with hours open Po hodinách at that day

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Check on the emulator

`adb` is at `$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe`; the screen is 1080×2400. Screenshots: `adb -s emulator-5554 exec-out screencap -p > shot.png` (save to the session scratchpad, not the repo).

- [ ] **Step 1: Deploy**

If `adb devices` doesn't list `emulator-5554`, start it in the background: `"$LOCALAPPDATA/Android/Sdk/emulator/emulator.exe" -avd MAUI_Emulator_API_36 -no-snapshot-save -no-boot-anim -no-audio`, and wait until `adb -s emulator-5554 shell getprop sys.boot_completed` prints `1`.

Run from `VibeWeather/`: `dotnet build -t:Run -f net10.0-android -c Debug -p:AdbTarget="-s emulator-5554" -p:RuntimeIdentifier=android-x64 -nologo`
Expected: `Počet chyb: 0`; `adb -s emulator-5554 shell pidof com.companyname.vibeweather` prints a pid.

- [ ] **Step 2: Po hodinách**

Tap the "Po hodinách" tab (x≈580, y≈2244). Screenshot. Expected: heading "PŘÍŠTÍCH 16 DNÍ" (Open-Meteo source); the scrubber card under the chart with day labels, ticks and a thumb at the left; the "Dnes" label (or the first labelled day) bold; the legend under the chart.

- [ ] **Step 3: Scroll sync**

Swipe the chart left a few times (`adb shell input swipe 900 900 200 900 300`), screenshot. Expected: the thumb has moved right and the bold day matches the day shown at the chart's left edge.

- [ ] **Step 4: Drag and tap**

Drag the thumb right (`adb shell input swipe <thumb x> <thumb y> <x+400> <thumb y> 600`), screenshot: the chart moved by the same share. Tap a day label near the right end, screenshot: that day's first hour (00:00 with its day label) is at the chart's left edge.

- [ ] **Step 5: From 16 dní**

Tap the "16 dní" tab (x≈805, y≈2244), tap a row about a week ahead, screenshot. Expected: "Po hodinách" opens with that day's first hour at the left edge, and the "Po hodinách" tab is highlighted. Then tap the "Po hodinách" tab: the chart scrolls back to now.

- [ ] **Step 6: Smoothness**

Fling the chart quickly a few times. It should keep up (no blank areas left for long, no stalls). If it clearly stutters, report it — don't optimise inside this plan.

- [ ] **Step 7: Fix what the checks found, then commit any fixes**

```bash
git add -A VibeWeather
git commit -m "Hourly days: fixes from the emulator check

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Skip the commit when nothing changed.)
