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
var hour = SelectionRules.DefaultMaxPositionAge;

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
Check("fresh under an hour", SelectionRules.IsFresh(At(TimeSpan.FromMinutes(59)), now, hour));
Check("not fresh at an hour", !SelectionRules.IsFresh(At(TimeSpan.FromHours(1)), now, hour));
Check("newer: found replaces older stored", SelectionRules.Newer(At(TimeSpan.FromHours(2)), At(TimeSpan.Zero)) == At(TimeSpan.Zero));
Check("newer: nothing found keeps stored", SelectionRules.Newer(At(TimeSpan.FromHours(2)), null) == At(TimeSpan.FromHours(2)));
Check("newer: older found doesn't replace", SelectionRules.Newer(At(TimeSpan.Zero), At(TimeSpan.FromHours(3))) == At(TimeSpan.Zero));
Check("resolve: fresh is not stale", SelectionRules.Resolve(At(TimeSpan.FromMinutes(5)), now, hour)?.StaleSince is null);
Check("resolve: old is stale since its fix", SelectionRules.Resolve(At(TimeSpan.FromHours(3)), now, hour)?.StaleSince == now - TimeSpan.FromHours(3));
Check("resolve: never known is null", SelectionRules.Resolve(null, now, hour) is null);
Check("resolve: outdated by a geofence move is stale", SelectionRules.Resolve(At(TimeSpan.FromMinutes(5)), now, hour, outdated: true)?.StaleSince == now - TimeSpan.FromMinutes(5));
Check("lookup: fresh and not outdated, none", !SelectionRules.NeedsLookup(At(TimeSpan.FromMinutes(5)), now, hour, outdated: false));
Check("lookup: fresh but outdated by a geofence move", SelectionRules.NeedsLookup(At(TimeSpan.FromMinutes(5)), now, hour, outdated: true));
Check("lookup: old", SelectionRules.NeedsLookup(At(TimeSpan.FromHours(2)), now, hour, outdated: false));
Check("lookup: nothing stored", SelectionRules.NeedsLookup(null, now, hour, outdated: false));

// Position cache duration chosen in Settings
Check("choices: 5, 15, 30 min, 1 h, 2 h", SelectionRules.PositionAgeChoices.SequenceEqual(
    [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2)]));
Check("default is an hour", hour == TimeSpan.FromHours(1));
Check("5 min: 6 min old needs a lookup", SelectionRules.NeedsLookup(At(TimeSpan.FromMinutes(6)), now, TimeSpan.FromMinutes(5), outdated: false));
Check("5 min: 6 min old is stale", SelectionRules.Resolve(At(TimeSpan.FromMinutes(6)), now, TimeSpan.FromMinutes(5))?.StaleSince is not null);
Check("2 h: 90 min old is fresh", SelectionRules.IsFresh(At(TimeSpan.FromMinutes(90)), now, TimeSpan.FromHours(2)));

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
