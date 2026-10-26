#:include ../VibeWeather/Services/SevereWeather/SevereWarning.cs
#:include ../VibeWeather/Services/SevereWeather/WarningDiff.cs
#:include ../VibeWeather/Services/SevereWeather/WarningText.cs
#:include ../VibeWeather/Data/DisplayUnits.cs
#:include ../VibeWeather/Data/Czech.cs
#:include ../VibeWeather/Data/WeatherModels.cs
// Checks WarningDiff: run with `dotnet run severe-warning-diff.cs` from this folder. Exit code 1 on failure.
using VibeWeather.Services.SevereWeather;

var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(2));
SevereWarning W(string id, string status = "New", int endHours = 6, string name = "Silné bouřky") =>
    new(id, name, status, "Praha", now.AddHours(-1), now.AddHours(endHours), "ČHMÚ", "Text", null, 20);

int failures = 0;
void Check(string what, bool ok) { Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

var r = WarningDiff.Compare([], [W("1")], now);
Check("new warning notifies", r.Notify.Count == 1 && r.Active.Count == 1 && r.Remove.Count == 0);

r = WarningDiff.Compare([W("1")], [W("1", "Continue")], now);
Check("continuing warning is silent", r.Notify.Count == 0 && r.Active.Count == 1);

r = WarningDiff.Compare([W("1")], [W("1", "Upgrade")], now);
Check("upgrade notifies again", r.Notify.Count == 1);

r = WarningDiff.Compare([W("1", "Upgrade")], [W("1", "Upgrade")], now);
Check("same upgrade twice is silent", r.Notify.Count == 0);

r = WarningDiff.Compare([W("1", "Extend")], [W("1", "Extend", endHours: 9)], now);
Check("extend with a new end notifies", r.Notify.Count == 1);

r = WarningDiff.Compare([W("1")], [W("1", "Cancel")], now);
Check("cancel removes", r.Active.Count == 0 && r.Remove.SequenceEqual(["1"]) && r.Notify.Count == 0);

r = WarningDiff.Compare([W("1"), W("2")], [W("2", "Continue")], now);
Check("missing warning removed", r.Remove.SequenceEqual(["1"]) && r.Active.Count == 1);

r = WarningDiff.Compare([W("1")], [W("1", "Continue", endHours: -1)], now);
Check("ended by time removed", r.Active.Count == 0 && r.Remove.SequenceEqual(["1"]));

r = WarningDiff.Compare([], [W("1"), W("1")], now);
Check("duplicates collapse", r.Active.Count == 1 && r.Notify.Count == 1);

r = WarningDiff.Compare([], [W("1", "Cancel")], now);
Check("cancelled unknown warning ignored", r.Active.Count == 0 && r.Notify.Count == 0 && r.Remove.Count == 0);

var units = new VibeWeather.Data.DisplayUnits(VibeWeather.Data.TemperatureUnit.Celsius,
    VibeWeather.Data.WindSpeedUnit.KilometersPerHour, VibeWeather.Data.TimeFormat.TwentyFourHour);
var started = new SevereWarning("9", "Vítr", "New", "Praha", now.AddHours(-1), now.AddHours(9), "ČHMÚ", "", null, 20);
Check("started: 'do 21:00'", WarningText.Period(started, units, now) == "do 21:00");
var later = started with { Start = now.AddHours(3), End = now.AddHours(20) };
Check("later: 'od 15:00 do čt 08:00'", WarningText.Period(later, units, now) == "od 15:00 do čt 08:00");
Check("status Czech", WarningText.Status("Upgrade") == "Zvýšená" && WarningText.Status("Whatever") == "Platí");

Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
