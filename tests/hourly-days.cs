#:include ../VibeWeather/Data/ForecastView.cs
#:include ../VibeWeather/Data/WeatherCodes.cs
#:include ../VibeWeather/Data/WeatherIcons.cs
#:include ../VibeWeather/Data/WeatherModels.cs
#:include ../VibeWeather/Data/DisplayUnits.cs
#:include ../VibeWeather/Data/Czech.cs
// Checks ForecastView's hourly days (scrubber segments, heading, links from the daily rows) and the daily rows' chance: run with
// `dotnet run hourly-days.cs` from this folder. Exit code 1 on failure.
using VibeWeather.Data;

int failures = 0;
void Check(string what, bool ok) { Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

// Monday 26 October 2026, 20:30; the hours start at 20:00.
var now = new DateTime(2026, 10, 26, 20, 30, 0);
var start = new DateTime(2026, 10, 26, 20, 0, 0);

ForecastView View(int hourCount, int dayCount, Func<int, double>? precipitation = null, Func<DayForecast, DayForecast>? day = null)
{
    var hours = Enumerable.Range(0, hourCount).Select(i => new HourForecast(
        start.AddHours(i), 10, 9, 1, true, 0, 5, 80, 5, 0, 1015, precipitation?.Invoke(i) ?? 0)).ToList();
    var days = Enumerable.Range(0, dayCount).Select(i =>
    {
        var date = DateOnly.FromDateTime(now).AddDays(i);
        var d = new DayForecast(date, 1, 12, 4, 0, 10, 180, 1, 0,
            date.ToDateTime(new TimeOnly(7, 0)), date.ToDateTime(new TimeOnly(17, 0)));
        return day?.Invoke(d) ?? d;
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

// Wet hours are drawn along the scrubber's track: column, top and height in a 20-unit-high track, colour by kind.
v = View(52, 4, i => i switch { 5 => 0.5, 30 => 6, _ => 0 });
Check("a bar per wet hour", v.HourScrubBars.Count == 2);
Check("light rain: short bar", v.HourScrubBars[0] is { Column: 5, Y: "13.2", Height: "6.8" });
Check("heavy rain: full bar", v.HourScrubBars[1] is { Column: 30, Y: "4.0", Height: "16.0" });
Check("bars in the rain colour", v.HourScrubBars.All(b => b.Color.Contains(" 252)")));
Check("dry hours: no bars", View(52, 4).HourScrubBars.Count == 0);

// The daily rows give the chance of precipitation only for days when some falls.
v = View(0, 3, day: d => d.Date.Day switch { 26 => d with { Pop = 40 }, 27 => d with { Pop = 40, PrecipMm = 1.2 }, _ => d with { Pop = 10, PrecipMm = 0.1 } });
Check("dry day with a chance: no chance shown", v.DayRows[0].Pop == "");
Check("wet day: chance shown", v.DayRows[1].Pop == "40%");
Check("wet day with a low chance: shown too", v.DayRows[2].Pop == "10%");

v = View(0, 3);
Check("no hours: no days, no links", v.HourDays.Count == 0 && v.DayRows.All(d => d.HourlyLink is null));

Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
