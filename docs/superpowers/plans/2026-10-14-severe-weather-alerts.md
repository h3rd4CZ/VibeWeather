# Severe Weather Alerts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Notify about official severe-weather warnings (Azure Maps) for the selected place, hourly within the alert hours, and show active warnings in the app (banner on Teď + detail page), replacing the placeholder "Nebezpečné počasí" switch.

**Architecture:** A `SevereWeatherMonitor` service (own hourly alarm, like `PrecipAlertMonitor`) fetches warnings through a small Azure Maps client, compares them with the stored list (pure `WarningDiff`), stores the result in Preferences, and tells a platform notifier which notifications to post or remove. Android parts: alarm scheduler, broadcast receiver, notifier. UI: banner component, detail page, Settings switch.

**Tech Stack:** .NET 10 MAUI Blazor Hybrid (Android), System.Text.Json source generation, Android AlarmManager via the existing `ExactAlarms` helper, NotificationCompat.

**Spec:** `docs/superpowers/specs/2026-10-14-severe-weather-alerts-design.md`

**Conventions (read first):**
- App root: `C:\dev\misc\scratchpad\mauiapp\VibeWeather` (paths below are relative to it unless they start with `tests/` or `docs/`, which are relative to `C:\dev\misc\scratchpad\mauiapp`).
- All user-visible text is Czech. Comments and log templates stay English. Times shown to the user go through `DisplayUnits` (`units.Time(...)`); dates use `Czech.Culture`.
- Build: `dotnet build C:\dev\misc\scratchpad\mauiapp\VibeWeather -f net10.0-android -c Debug` → expect `Počet chyb: 0` (Czech MSBuild output; "chyb" = errors).
- Emulator deploy: `dotnet build -t:Run -f net10.0-android -c Debug -p:AdbTarget="-s emulator-5554" -p:RuntimeIdentifier=android-x64` from the app root. Never `EmbedAssembliesIntoApk`.
- The folder is not a git repository: there are no commit steps.
- Files are UTF-8 with LF line endings; keep it that way.

---

## File map

| File | Responsibility |
|---|---|
| Create `Services/SevereWeather/SevereWarning.cs` | The stored warning record. |
| Create `Services/SevereWeather/WarningDiff.cs` | Pure comparison: which warnings are active, which to notify, which notifications to remove. |
| Create `Services/SevereWeather/WarningText.cs` | Czech texts shared by notifications and UI (validity period, status). |
| Create `tests/severe-warning-diff.cs` | File-based test for `WarningDiff` + `WarningText.Period` (no test project exists). |
| Modify `Services/AzureMaps/AzureMapsDtos.cs` | DTOs for the severe alerts response + JSON context entry. |
| Create `Services/AzureMaps/AzureMapsSevereAlerts.cs` | Fetch warnings for a place and map them to `SevereWarning`. |
| Create `Services/SevereWeather/SevereWeatherMonitor.cs` | Feature logic: on/off, checks, store, plan, notifier calls, `Changed`. Also the two platform interfaces. |
| Modify `Services/AppJsonContext.cs` | Serializer for `List<SevereWarning>`. |
| Create `Platforms/Android/SevereWeather/AndroidSevereWeatherScheduler.cs` | The hourly alarm. |
| Create `Platforms/Android/SevereWeather/SevereWeatherReceiver.cs` | Runs a check when the alarm fires. |
| Create `Platforms/Android/SevereWeather/AndroidSevereWarningNotifier.cs` | Channel "Výstrahy", one notification per warning, removal. |
| Create `Platforms/Android/Resources/drawable/ic_stat_warning.xml` | Notification icon (triangle with !). |
| Modify `MauiProgram.cs`, `App.xaml.cs`, `Platforms/Android/Geofencing/GeofenceRestoreReceiver.cs` | Registration, re-arming on start/resume/reboot. |
| Modify `Services/WeatherState.cs`, `Components/Layout/MainLayout.razor` | Keep a notification's route when the app starts cold. |
| Modify `Data/WeatherIcons.cs` | `Warning` icon path. |
| Create `Components/Shared/WarningBanner.razor` + `.razor.css` | Banner(s) on Teď. |
| Modify `Components/Pages/Home.razor` | Show the banner. |
| Create `Components/Pages/WarningDetail.razor` + `.razor.css` | `/warning/{Id}` detail page. |
| Modify `Components/Pages/Settings.razor` | Real "Nebezpečné počasí" switch with plan rows; remove placeholder toggles. |

---

### Task 1: Warning record and comparison (test first)

**Files:**
- Create: `Services/SevereWeather/SevereWarning.cs`
- Create: `Services/SevereWeather/WarningDiff.cs`
- Test: `tests/severe-warning-diff.cs`

- [ ] **Step 1: Write the failing test**

Create `C:\dev\misc\scratchpad\mauiapp\tests\severe-warning-diff.cs`:

```csharp
#:include ../VibeWeather/Services/SevereWeather/SevereWarning.cs
#:include ../VibeWeather/Services/SevereWeather/WarningDiff.cs
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

Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
```

- [ ] **Step 2: Run it to see it fail**

Run (from `C:\dev\misc\scratchpad\mauiapp\tests`): `dotnet run severe-warning-diff.cs`
Expected: build error — the included files don't exist yet.

- [ ] **Step 3: Write `SevereWarning.cs`**

```csharp
namespace VibeWeather.Services.SevereWeather;

/// <summary>
/// An official warning for the selected place, as stored between checks. Times keep the place's UTC offset, so their
/// clock is the place's. Status is the source's English keyword: New, Continue, Update, Correct, Extend, Upgrade,
/// Cancel or Expire. Id combines the source and its alert number.
/// </summary>
public sealed record SevereWarning(
    string Id, string Name, string Status, string Area, DateTimeOffset Start, DateTimeOffset End,
    string Source, string Details, string? Disclaimer, int Priority)
{
    /// <summary>Priority 1 is the most important; the scale is the provider's, so only the top band shows as severe (red).</summary>
    public bool IsSevere => Priority <= 10;
}
```

- [ ] **Step 4: Write `WarningDiff.cs`**

```csharp
namespace VibeWeather.Services.SevereWeather;

/// <summary>What a check changes: the warnings now active, those to notify about, and ids whose notifications go.</summary>
public sealed record WarningChanges(IReadOnlyList<SevereWarning> Active, IReadOnlyList<SevereWarning> Notify, IReadOnlyList<string> Remove);

/// <summary>Compares a check's warnings with those stored from the previous check of the same place.</summary>
public static class WarningDiff
{
    static readonly string[] Ended = ["Cancel", "Expire"];
    static readonly string[] Changed = ["Update", "Upgrade", "Extend", "Correct"];

    /// <summary>
    /// A warning not seen before notifies; a known one notifies again only when its status says it changed and its
    /// status, end or name differ from what was stored. Cancelled, expired, ended or missing warnings are removed.
    /// </summary>
    public static WarningChanges Compare(IReadOnlyList<SevereWarning> stored, IReadOnlyList<SevereWarning> current, DateTimeOffset now)
    {
        var before = stored.DistinctBy(w => w.Id).ToDictionary(w => w.Id);
        var active = current
            .DistinctBy(w => w.Id)
            .Where(w => !Ended.Contains(w.Status) && w.End > now)
            .ToList();
        var notify = active
            .Where(w => !before.TryGetValue(w.Id, out var old)
                || (Changed.Contains(w.Status) && (w.Status != old.Status || w.End != old.End || w.Name != old.Name)))
            .ToList();
        var activeIds = active.Select(w => w.Id).ToHashSet();
        var remove = before.Keys.Where(id => !activeIds.Contains(id)).ToList();
        return new WarningChanges(active, notify, remove);
    }
}
```

- [ ] **Step 5: Run the test to see it pass**

Run: `dotnet run severe-warning-diff.cs`
Expected: ten `ok` lines and `ALL OK`.

---

### Task 2: Czech texts for warnings (test first)

**Files:**
- Create: `Services/SevereWeather/WarningText.cs`
- Modify: `tests/severe-warning-diff.cs`

- [ ] **Step 1: Extend the test**

At the top of `tests/severe-warning-diff.cs`, add the includes (after the existing two):

```csharp
#:include ../VibeWeather/Services/SevereWeather/WarningText.cs
#:include ../VibeWeather/Data/DisplayUnits.cs
#:include ../VibeWeather/Data/Czech.cs
#:include ../VibeWeather/Data/WeatherModels.cs
```

and, before the final `Console.WriteLine(failures == 0 ...`, add:

```csharp
var units = new VibeWeather.Data.DisplayUnits(VibeWeather.Data.TemperatureUnit.Celsius,
    VibeWeather.Data.WindSpeedUnit.KilometersPerHour, VibeWeather.Data.TimeFormat.TwentyFourHour);
var started = new SevereWarning("9", "Vítr", "New", "Praha", now.AddHours(-1), now.AddHours(9), "ČHMÚ", "", null, 20);
Check("started: 'do 21:00'", WarningText.Period(started, units, now) == "do 21:00");
var later = started with { Start = now.AddHours(3), End = now.AddHours(20) };
Check("later: 'od 15:00 do čt 08:00'", WarningText.Period(later, units, now) == "od 15:00 do čt 08:00");
Check("status Czech", WarningText.Status("Upgrade") == "Zvýšená" && WarningText.Status("Whatever") == "Platí");
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet run severe-warning-diff.cs`
Expected: build error — `WarningText.cs` doesn't exist.

- [ ] **Step 3: Write `WarningText.cs`**

```csharp
using VibeWeather.Data;

namespace VibeWeather.Services.SevereWeather;

/// <summary>Czech texts about a warning, shared by notifications and pages.</summary>
public static class WarningText
{
    /// <summary>
    /// When it applies, in the place's clock: "do 21:00" once it has started, otherwise "od 15:00 do čt 08:00".
    /// A time on another day than today gets its weekday.
    /// </summary>
    public static string Period(SevereWarning warning, DisplayUnits units, DateTimeOffset now)
    {
        string When(DateTimeOffset t)
        {
            var today = now.ToOffset(t.Offset).Date;
            var time = units.Time(t.DateTime);
            return t.Date == today ? time : $"{t.DateTime.ToString("ddd", Czech.Culture)} {time}";
        }
        return warning.Start <= now ? $"do {When(warning.End)}" : $"od {When(warning.Start)} do {When(warning.End)}";
    }

    /// <summary>The source's status keyword in Czech, for the detail page.</summary>
    public static string Status(string status) => status switch
    {
        "New" => "Nová",
        "Update" => "Aktualizovaná",
        "Upgrade" => "Zvýšená",
        "Extend" => "Prodloužená",
        "Correct" => "Opravená",
        "Continue" => "Trvá",
        _ => "Platí",
    };
}
```

- [ ] **Step 4: Run the test to see it pass**

Run: `dotnet run severe-warning-diff.cs`
Expected: 13 `ok` lines and `ALL OK`. (2026-10-14 is a Wednesday, so 08:00 the next day is "čt".)

---

### Task 3: Azure Maps severe alerts client

**Files:**
- Modify: `Services/AzureMaps/AzureMapsDtos.cs` (end of file: JSON context)
- Create: `Services/AzureMaps/AzureMapsSevereAlerts.cs`

- [ ] **Step 1: Add the DTOs**

In `Services/AzureMaps/AzureMapsDtos.cs`, insert before the `[JsonSerializable(typeof(CurrentConditionsResponse))]` line:

```csharp
// Get Severe Weather Alerts (/weather/severe/alerts/json). Phrases come in the requested language (localized) and in
// English; alertDetails is in whatever language the issuing agency writes (Czech for ČHMÚ).
internal sealed class SevereAlertsResponse
{
    [JsonPropertyName("results")] public SevereAlert[] Results { get; set; } = [];
}

internal sealed class SevereAlert
{
    [JsonPropertyName("alertId")] public int AlertId { get; set; }
    [JsonPropertyName("sourceId")] public int SourceId { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    /// <summary>1 is the most important; the scale is the provider's.</summary>
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("description")] public LocalizedPhrase? Description { get; set; }
    [JsonPropertyName("disclaimer")] public string? Disclaimer { get; set; }
    [JsonPropertyName("alertAreas")] public SevereAlertArea[] AlertAreas { get; set; } = [];
}

internal sealed class LocalizedPhrase
{
    [JsonPropertyName("english")] public string? English { get; set; }
    [JsonPropertyName("localized")] public string? Localized { get; set; }
}

internal sealed class SevereAlertArea
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("startTime")] public DateTimeOffset StartTime { get; set; }
    [JsonPropertyName("endTime")] public DateTimeOffset EndTime { get; set; }
    [JsonPropertyName("latestStatus")] public LocalizedPhrase? LatestStatus { get; set; }
    [JsonPropertyName("alertDetails")] public string? AlertDetails { get; set; }
}

```

and add one attribute to the context list (after `[JsonSerializable(typeof(MinuteForecastResponse))]`):

```csharp
[JsonSerializable(typeof(SevereAlertsResponse))]
```

- [ ] **Step 2: Write the client**

Create `Services/AzureMaps/AzureMapsSevereAlerts.cs`:

```csharp
using System.Globalization;
using VibeWeather.Data;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather.Services.AzureMaps;

/// <summary>
/// Official severe weather warnings for one place from Azure Maps (one billable request), with the key saved in
/// Settings, also when Open-Meteo is the forecast source.
/// </summary>
public sealed class AzureMapsSevereAlerts(HttpClient http, WeatherSourceSettings settings)
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public Task<bool> HasKeyAsync() => settings.HasAzureKeyAsync();

    /// <summary>
    /// Warnings in effect or announced at the place, names in Czech where the service translates them.
    /// Throws <see cref="AzureMapsKeyException"/> without a key or with a rejected one, otherwise
    /// <see cref="WeatherProviderException"/> or <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<IReadOnlyList<SevereWarning>> GetAsync(WeatherLocation place, CancellationToken cancellationToken)
    {
        var key = await settings.GetAzureKeyAsync();
        if (string.IsNullOrEmpty(key))
            throw new AzureMapsKeyException("Výstrahy potřebují klíč Azure Maps. Zadejte ho v Nastavení → Zdroj počasí.");

        var query = $"query={place.Latitude.ToString("0.####", Inv)},{place.Longitude.ToString("0.####", Inv)}";
        var response = await AzureMapsHttp.GetAsync(http, key,
            $"/weather/severe/alerts/json?api-version=1.1&{query}&language=cs-CZ&details=true",
            AzureMapsJsonContext.Default.SevereAlertsResponse, cancellationToken);
        return response.Results.Select(Map).OfType<SevereWarning>().DistinctBy(w => w.Id).ToList();
    }

    // The first area is the one at the requested point; the full text falls back to the short summary.
    static SevereWarning? Map(SevereAlert alert)
    {
        var area = alert.AlertAreas.FirstOrDefault();
        if (area is null) return null;
        return new SevereWarning(
            $"{alert.SourceId}-{alert.AlertId}",
            alert.Description?.Localized ?? alert.Description?.English ?? "Výstraha",
            area.LatestStatus?.English ?? "New",
            area.Name ?? "",
            area.StartTime,
            area.EndTime,
            alert.Source ?? "",
            (area.AlertDetails ?? area.Summary ?? "").Trim(),
            alert.Disclaimer,
            alert.Priority);
    }
}
```

- [ ] **Step 3: Build**

Run the build command. Expected: `Počet chyb: 0`.

---

### Task 4: The monitor

**Files:**
- Create: `Services/SevereWeather/SevereWeatherMonitor.cs`
- Modify: `Services/AppJsonContext.cs`

- [ ] **Step 1: Serializer for the stored list**

In `Services/AppJsonContext.cs`, add after `[JsonSerializable(typeof(List<GeofencePlace>))]`:

```csharp
[JsonSerializable(typeof(List<SevereWeather.SevereWarning>))]
```

- [ ] **Step 2: Write the monitor**

Create `Services/SevereWeather/SevereWeatherMonitor.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeWeather.Data;
using VibeWeather.Services.AzureMaps;
using VibeWeather.Services.Diagnostics;
using VibeWeather.Services.PrecipAlerts;

namespace VibeWeather.Services.SevereWeather;

/// <summary>Runs <see cref="SevereWeatherMonitor.RunCheckAsync"/> at a set time, also when the app isn't open.</summary>
public interface ISevereWeatherScheduler
{
    /// <summary>Replaces any pending check with one at <paramref name="atUtc"/>.</summary>
    void Schedule(DateTime atUtc);

    void Cancel();

    bool IsScheduled { get; }
}

/// <summary>Posts and removes warning notifications.</summary>
public interface ISevereWarningNotifier
{
    /// <summary>Posts or replaces the notification of <paramref name="warning"/> at <paramref name="place"/>.</summary>
    void Show(WeatherLocation place, SevereWarning warning);

    void Remove(string warningId);
}

/// <summary>
/// Official severe weather warnings for the selected place (Azure Maps). Checks hourly within the alert hours of
/// precipitation alerts, notifies about new or changed warnings, removes ended ones, and keeps the active ones for the
/// banner on Teď. Needs the Azure Maps key; without it (or when the key is rejected) it switches itself off.
/// </summary>
public sealed class SevereWeatherMonitor
{
    static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    static readonly TimeSpan TimeoutRetry = TimeSpan.FromMinutes(2);
    static readonly TimeSpan ErrorRetry = TimeSpan.FromMinutes(15);
    static readonly TimeSpan MinDelay = TimeSpan.FromMinutes(1);

    const string EnabledPreference = "severe_alerts_enabled";
    const string StoredPreference = "severe_alerts_active";
    const string PlaceKeyPreference = "severe_alerts_place";
    const string NextAtPreference = "severe_alerts_next_at";
    const string NextReasonPreference = "severe_alerts_next_reason";
    const string LastCheckPreference = "severe_alerts_last_check";

    readonly AzureMapsSevereAlerts alerts;
    readonly WeatherState weather;
    readonly PrecipAlertMonitor precip;
    readonly ISevereWeatherScheduler scheduler;
    readonly ISevereWarningNotifier notifier;
    readonly ErrorReporter errors;
    readonly ILogger<SevereWeatherMonitor> logger;
    readonly SemaphoreSlim checkLock = new(1, 1);

    public SevereWeatherMonitor(AzureMapsSevereAlerts alerts, WeatherState weather, PrecipAlertMonitor precip,
        ISevereWeatherScheduler scheduler, ISevereWarningNotifier notifier, ErrorReporter errors,
        ILogger<SevereWeatherMonitor> logger)
    {
        this.alerts = alerts;
        this.weather = weather;
        this.precip = precip;
        this.scheduler = scheduler;
        this.notifier = notifier;
        this.errors = errors;
        this.logger = logger;
        // Another place has other warnings: check it straight away (the banner hides the old ones meanwhile).
        weather.SelectionChanged += () =>
        {
            if (!Enabled) return;
            Changed?.Invoke();
            CheckNow();
        };
    }

    public event Action? Changed;

    public bool Enabled => Preferences.Default.Get(EnabledPreference, false);

    /// <summary>Active warnings of the selected place, most important first; empty when off or not yet checked there.</summary>
    public IReadOnlyList<SevereWarning> Active =>
        Enabled && Preferences.Default.Get(PlaceKeyPreference, "") == weather.SelectedKey
            ? Load().Where(w => w.End > DateTimeOffset.Now).OrderBy(w => w.Priority).ToList()
            : [];

    /// <summary>A stored warning by id, also an ended one still stored; null when unknown.</summary>
    public SevereWarning? Find(string id) => Load().FirstOrDefault(w => w.Id == id);

    /// <summary>When the next check is planned, in device time.</summary>
    public DateTime? NextCheck => Preferences.Default.Get(NextAtPreference, 0L) is > 0 and var ticks
        ? new DateTime(ticks, DateTimeKind.Utc).ToLocalTime()
        : null;

    /// <summary>Why the next check is then, or what the last one found ("Žádné výstrahy", "Aktivní výstrahy: 2").</summary>
    public string NextCheckReason => Preferences.Default.Get(NextReasonPreference, "");

    /// <summary>When the last check got an answer, in device time.</summary>
    public DateTime? LastCheck => Preferences.Default.Get(LastCheckPreference, 0L) is > 0 and var ticks
        ? new DateTime(ticks, DateTimeKind.Utc).ToLocalTime()
        : null;

    /// <summary>Turns warnings on (asking for notification permission) and checks right away. Returns why it can't, or null.</summary>
    public async Task<string?> EnableAsync()
    {
        if (!await alerts.HasKeyAsync())
            return "Výstrahy potřebují klíč Azure Maps. Zadejte ho výše v části Zdroj počasí.";
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        });
        Preferences.Default.Set(EnabledPreference, true);
        CheckNow();
        return null;
    }

    /// <summary>Turns warnings off: no more checks, and their notifications and stored warnings go.</summary>
    public void Disable()
    {
        Preferences.Default.Set(EnabledPreference, false);
        scheduler.Cancel();
        ClearStored();
        Preferences.Default.Remove(NextAtPreference);
        Preferences.Default.Remove(NextReasonPreference);
        Changed?.Invoke();
    }

    /// <summary>Switches on or off; returns why it couldn't switch on, or null.</summary>
    public async Task<string?> ToggleAsync()
    {
        if (!Enabled) return await EnableAsync();
        Disable();
        return null;
    }

    /// <summary>Makes sure the planned check is pending (after a reboot or app update). Does nothing when off or pending.</summary>
    public void EnsureScheduled()
    {
        if (!Enabled) return;
        var planned = PlannedUtc;
        if (scheduler.IsScheduled && planned > DateTime.UtcNow - MinDelay) return;
        scheduler.Schedule(planned > DateTime.UtcNow ? planned : DateTime.UtcNow);
    }

    /// <summary>Re-arms the planned check and updates the UI, e.g. when the app comes back to the front.</summary>
    public void Refresh()
    {
        if (!Enabled) return;
        var planned = PlannedUtc;
        scheduler.Schedule(planned > DateTime.UtcNow ? planned : DateTime.UtcNow);
        Changed?.Invoke();
    }

    /// <summary>One check of the selected place, run by the alarm: updates notifications and plans the next check. Never throws.</summary>
    public async Task RunCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled) return;
        await checkLock.WaitAsync(CancellationToken.None);
        try
        {
            var (nextUtc, reason) = await CheckAsync(cancellationToken);
            Plan(nextUtc, reason);
        }
        catch (OperationCanceledException)
        {
            Plan(DateTime.UtcNow + TimeoutRetry, "Nový pokus: poslední kontrola nestihla doběhnout");
        }
        catch (AzureMapsKeyException ex)
        {
            // Without a working key there is nothing to check; Settings shows the switch off.
            errors.Report(ex, "Zjišťování výstrah", popup: false);
            Disable();
        }
        catch (WeatherProviderException ex)
        {
            errors.Report(ex, "Zjišťování výstrah", popup: false);
            var retry = ex.RetryAfter is { } wait && wait > ErrorRetry ? wait : ErrorRetry;
            Plan(DateTime.UtcNow + retry, $"Nový pokus po chybě: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Anything unexpected must not stop the checks for good.
            errors.Report(ex, "Zjišťování výstrah", popup: false);
            Plan(DateTime.UtcNow + ErrorRetry, "Nový pokus po chybě");
        }
        finally
        {
            checkLock.Release();
        }
    }

    async Task<(DateTime NextUtc, string Reason)> CheckAsync(CancellationToken cancellationToken)
    {
        var place = weather.SelectedLocation;
        if (place is null)
        {
            logger.LogInformation("Severe weather check skipped: no known position for the selected location");
            return (DateTime.UtcNow + ErrorRetry, "Poloha vybraného místa zatím není známá");
        }

        var current = await alerts.GetAsync(place, cancellationToken);
        Preferences.Default.Set(LastCheckPreference, DateTime.UtcNow.Ticks);

        // Another place than last time: its warnings start from scratch, the old place's notifications go.
        var stored = Preferences.Default.Get(PlaceKeyPreference, "") == place.Key ? Load() : ClearStored();
        var changes = WarningDiff.Compare(stored, current, DateTimeOffset.Now);
        Store(changes.Active, place.Key);
        foreach (var id in changes.Remove) notifier.Remove(id);
        foreach (var warning in changes.Notify) notifier.Show(place, warning);
        logger.LogInformation("Severe weather check: {Active} active, {Notify} notified, {Removed} removed",
            changes.Active.Count, changes.Notify.Count, changes.Remove.Count);

        var reason = changes.Active.Count == 0
            ? "Žádné výstrahy"
            : $"Aktivní výstrahy: {changes.Active.Count}";
        return (DateTime.UtcNow + Interval, reason);
    }

    /// <summary>Stores the plan for Settings and sets the alarm; outside the alert hours the check waits for their start.</summary>
    void Plan(DateTime atUtc, string reason)
    {
        if (!Enabled) return;
        var local = atUtc.ToLocalTime();
        var allowed = precip.Hours.NextAllowed(local);
        if (allowed != local)
        {
            atUtc = DateTime.SpecifyKind(allowed, DateTimeKind.Local).ToUniversalTime();
            reason = $"{reason} · další kontrola na začátku doby upozornění";
        }
        Preferences.Default.Set(NextAtPreference, atUtc.Ticks);
        Preferences.Default.Set(NextReasonPreference, reason);
        scheduler.Schedule(atUtc);
        logger.LogInformation("Next severe weather check at {At:u}: {Reason}", atUtc, reason);
        Changed?.Invoke();
    }

    /// <summary>Plans a check right away (the alarm fires immediately).</summary>
    void CheckNow() => Plan(DateTime.UtcNow, "Kontrola právě probíhá");

    DateTime PlannedUtc => new(Preferences.Default.Get(NextAtPreference, 0L), DateTimeKind.Utc);

    List<SevereWarning> Load()
    {
        var json = Preferences.Default.Get<string?>(StoredPreference, null);
        try { return json is null ? [] : JsonSerializer.Deserialize(json, AppJsonContext.Default.ListSevereWarning) ?? []; }
        catch (JsonException) { return []; }
    }

    void Store(IReadOnlyList<SevereWarning> warnings, string placeKey)
    {
        Preferences.Default.Set(StoredPreference, JsonSerializer.Serialize(warnings.ToList(), AppJsonContext.Default.ListSevereWarning));
        Preferences.Default.Set(PlaceKeyPreference, placeKey);
    }

    /// <summary>Removes the stored warnings and their notifications; returns the now empty list.</summary>
    List<SevereWarning> ClearStored()
    {
        foreach (var warning in Load()) notifier.Remove(warning.Id);
        Preferences.Default.Remove(StoredPreference);
        Preferences.Default.Remove(PlaceKeyPreference);
        return [];
    }
}
```

- [ ] **Step 3: Build**

Run the build command. Expected: `Počet chyb: 0`. (Nothing uses the monitor yet; DI comes in Task 6.)

---

### Task 5: Android scheduler, receiver, notifier and icon

**Files:**
- Create: `Platforms/Android/SevereWeather/AndroidSevereWeatherScheduler.cs`
- Create: `Platforms/Android/SevereWeather/SevereWeatherReceiver.cs`
- Create: `Platforms/Android/SevereWeather/AndroidSevereWarningNotifier.cs`
- Create: `Platforms/Android/Resources/drawable/ic_stat_warning.xml`

- [ ] **Step 1: Scheduler**

```csharp
using Android.App;
using Android.Content;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather.Platforms.Android.SevereWeather;

/// <summary>One pending alarm at a time (see <see cref="ExactAlarms"/>), received by <see cref="SevereWeatherReceiver"/>.</summary>
public sealed class AndroidSevereWeatherScheduler : ISevereWeatherScheduler
{
    const int RequestCode = 7201;

    static Context Context => Platform.AppContext;

    public bool IsScheduled =>
        PendingIntent.GetBroadcast(Context, RequestCode, NewIntent(), PendingIntentFlags.Immutable | PendingIntentFlags.NoCreate) is not null;

    public void Schedule(DateTime atUtc) => ExactAlarms.Set(atUtc, PendingAlarm());

    public void Cancel()
    {
        var alarm = PendingAlarm();
        ExactAlarms.Cancel(alarm);
        // Also drop the PendingIntent itself, so IsScheduled turns false.
        alarm.Cancel();
    }

    static Intent NewIntent() => new(Context, typeof(SevereWeatherReceiver));

    static PendingIntent PendingAlarm() =>
        PendingIntent.GetBroadcast(Context, RequestCode, NewIntent(), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}
```

- [ ] **Step 2: Receiver**

```csharp
using Android.App;
using Android.Content;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather.Platforms.Android.SevereWeather;

/// <summary>The warning check alarm: downloads the warnings, updates notifications and sets the next alarm.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class SevereWeatherReceiver : BroadcastReceiver
{
    // Broadcast receivers get about 10 seconds; a timed-out check is retried shortly (see SevereWeatherMonitor).
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);

    public override void OnReceive(Context? context, Intent? intent)
    {
        // MauiApplication builds the service container on process start, including starts for a broadcast.
        var monitor = IPlatformApplication.Current?.Services.GetService<SevereWeatherMonitor>();
        if (monitor is null || !monitor.Enabled) return;

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(CheckTimeout);
                await monitor.RunCheckAsync(timeout.Token);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
```

- [ ] **Step 3: Notifier**

```csharp
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using VibeWeather.Data;
using VibeWeather.Services;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather.Platforms.Android.SevereWeather;

/// <summary>"Silné bouřky · Praha" notifications, one per warning, replaced when it changes.</summary>
public sealed class AndroidSevereWarningNotifier(DisplaySettings display) : ISevereWarningNotifier
{
    const string ChannelId = "severe_weather";

    static Context Context => Platform.AppContext;

    public void Show(WeatherLocation place, SevereWarning warning)
    {
        var manager = NotificationManagerCompat.From(Context);
        // Also false when the user hasn't granted the Android 13+ notification permission.
        if (manager is null || !manager.AreNotificationsEnabled()) return;
        EnsureChannel();

        var period = WarningText.Period(warning, display.Units, DateTimeOffset.Now);
        var summary = string.IsNullOrEmpty(warning.Area) ? period : $"{period} · {warning.Area}";
        var full = $"{summary}\n\n{warning.Details}\n\n{warning.Source}".Trim();
        int id = NotificationId(warning.Id);

        var notification = new NotificationCompat.Builder(Context, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_stat_warning)!
            .SetContentTitle($"{warning.Name} · {place.Name}")!
            .SetContentText(summary)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(full))!
            .SetContentIntent(NotificationIntents.OpenLocation(Context, place.Key, $"warning/{Uri.EscapeDataString(warning.Id)}", id))!
            .SetAutoCancel(true)!
            .Build();
        manager.Notify(id, notification);
    }

    public void Remove(string warningId) => NotificationManagerCompat.From(Context)?.Cancel(NotificationId(warningId));

    // Stable across processes (string.GetHashCode isn't), so a later check replaces or removes the same notification.
    static int NotificationId(string warningId)
    {
        unchecked
        {
            int hash = 7203;
            foreach (char c in warningId) hash = hash * 31 + c;
            return hash;
        }
    }

    static void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var channel = new NotificationChannel(ChannelId, "Výstrahy", NotificationImportance.High)
        {
            Description = "Oficiální výstrahy před nebezpečným počasím pro vybrané místo",
        };
        ((NotificationManager)Context.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(channel);
    }
}
```

- [ ] **Step 4: Icon** — `Platforms/Android/Resources/drawable/ic_stat_warning.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<!-- Notification icon: a warning triangle with "!", white on transparent as Android requires. -->
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="24dp"
    android:height="24dp"
    android:viewportWidth="24"
    android:viewportHeight="24">
    <!-- evenOdd cuts the bar and dot of the "!" out of the filled triangle. -->
    <path
        android:fillColor="#FFFFFFFF"
        android:fillType="evenOdd"
        android:pathData="M12,2.8L1.6,20.8h20.8L12,2.8zM11,9.5h2v5.5h-2zM11,16.6h2v2h-2z" />
</vector>
```

- [ ] **Step 5: Build**

Run the build command. Expected: `Počet chyb: 0`.

---

### Task 6: Registration, re-arming, and routes from cold start

**Files:**
- Modify: `MauiProgram.cs`
- Modify: `App.xaml.cs`
- Modify: `Platforms/Android/Geofencing/GeofenceRestoreReceiver.cs`
- Modify: `Services/WeatherState.cs` (`ShowAsync`)
- Modify: `Components/Layout/MainLayout.razor` (`OnInitializedAsync`)

- [ ] **Step 1: DI** — in `MauiProgram.cs` add after `builder.Services.AddSingleton<MorningSummaryService>();`:

```csharp
		builder.Services.AddSingleton<AzureMapsSevereAlerts>();
		builder.Services.AddSingleton<SevereWeatherMonitor>();
```

and inside the `#if ANDROID` block after the `IMorningSummaryScheduler` line:

```csharp
		builder.Services.AddSingleton<ISevereWeatherScheduler, Platforms.Android.SevereWeather.AndroidSevereWeatherScheduler>();
		builder.Services.AddSingleton<ISevereWarningNotifier, Platforms.Android.SevereWeather.AndroidSevereWarningNotifier>();
```

Add `using VibeWeather.Services.SevereWeather;` at the top.

- [ ] **Step 2: App start/resume** — in `App.xaml.cs`:
  - add `using VibeWeather.Services.SevereWeather;`
  - add field `readonly SevereWeatherMonitor severe;`
  - constructor parameter `SevereWeatherMonitor severe` (after `MorningSummaryService morning`) and `this.severe = severe;`
  - in `window.Resumed` after `alerts.Refresh();` add `severe.Refresh();`
  - after `morning.ScheduleNext();` add `severe.EnsureScheduled();`

- [ ] **Step 3: Reboot** — in `GeofenceRestoreReceiver.OnReceive`, after `services?.GetService<PrecipAlertMonitor>()?.EnsureScheduled();` add:

```csharp
        services?.GetService<VibeWeather.Services.SevereWeather.SevereWeatherMonitor>()?.EnsureScheduled();
```

- [ ] **Step 4: Keep a route when the app starts from a notification** — in `Services/WeatherState.cs` replace `ShowAsync`:

```csharp
    /// <summary>Makes <paramref name="location"/> the weather location and shows it on the tab at <paramref name="route"/>.</summary>
    public Task ShowAsync(WeatherLocation location, string route = "")
    {
        // Started from a notification, no page listens yet: MainLayout takes the route when it starts.
        if (ShowForecastRequested is null) pendingRoute = route;
        else ShowForecastRequested.Invoke(route);
        return SelectAsync(location);
    }

    string? pendingRoute;

    /// <summary>A route asked for before any page could show it (see <see cref="ShowAsync"/>); null once taken.</summary>
    public string? TakePendingRoute()
    {
        var route = pendingRoute;
        pendingRoute = null;
        return route;
    }
```

In `Components/Layout/MainLayout.razor` `OnInitializedAsync`, after `Theme.Changed += OnStateChanged;` add:

```csharp
        if (State.TakePendingRoute() is { Length: > 0 } route) Navigation.NavigateTo(route);
```

- [ ] **Step 5: Build**

Run the build command. Expected: `Počet chyb: 0`.

---

### Task 7: Banner on Teď

**Files:**
- Modify: `Data/WeatherIcons.cs`
- Create: `Components/Shared/WarningBanner.razor`, `Components/Shared/WarningBanner.razor.css`
- Modify: `Components/Pages/Home.razor`

- [ ] **Step 1: Icon** — in `Data/WeatherIcons.cs`, after the `WindArrow` constant add:

```csharp
    /// <summary>Warning triangle for severe weather warnings.</summary>
    public const string Warning = "M12 3.5L2.5 20h19L12 3.5zM12 10v4.5M12 17.5v.01";
```

- [ ] **Step 2: Banner component** — `Components/Shared/WarningBanner.razor`:

```razor
@* Active official warnings for the selected place, most important first; each opens its detail page. *@
@implements IDisposable
@inject SevereWeatherMonitor Severe
@inject DisplaySettings Display

@foreach (var warning in Severe.Active)
{
    <a href="warning/@Uri.EscapeDataString(warning.Id)" class="warning-banner @(warning.IsSevere ? "severe" : "")">
        <Icon Path="@WeatherIcons.Warning" Size="22" StrokeWidth="1.9" Class="warning-icon" />
        <span class="warning-text">
            <span class="warning-name">Výstraha · @warning.Name</span>
            <span class="warning-when">@WarningText.Period(warning, Display.Units, DateTimeOffset.Now)</span>
        </span>
        <span class="warning-arrow">→</span>
    </a>
}

@code {
    protected override void OnInitialized() => Severe.Changed += OnChanged;

    void OnChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => Severe.Changed -= OnChanged;
}
```

Add `@using VibeWeather.Services.SevereWeather` to `Components/_Imports.razor` (after `@using VibeWeather.Services.PrecipAlerts`).

- [ ] **Step 3: Banner styles** — `Components/Shared/WarningBanner.razor.css`:

```css
/* Orange for warnings, red for the most important ones; tinted so it reads in light and dark mode. */
.warning-banner {
    --warn: oklch(0.72 0.17 55);
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 12px 16px;
    border-radius: 20px;
    background: color-mix(in oklch, var(--warn) 18%, var(--surf));
    box-shadow: inset 4px 0 0 var(--warn);
    color: var(--ink);
    text-decoration: none;
}

    .warning-banner.severe {
        --warn: oklch(0.6 0.2 27);
    }

    .warning-banner ::deep .warning-icon {
        stroke: var(--warn);
        flex: none;
    }

.warning-text {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
    flex: 1;
}

.warning-name {
    font-size: 15px;
    font-weight: 600;
}

.warning-when {
    font-size: 13px;
    color: var(--mut);
}

.warning-arrow {
    color: var(--mut);
}
```

- [ ] **Step 4: Show it on Teď** — in `Components/Pages/Home.razor`, insert as the first child of `<div class="stack">`:

```razor
    <WarningBanner />
```

- [ ] **Step 5: Build**

Run the build command. Expected: `Počet chyb: 0`.

---

### Task 8: Detail page

**Files:**
- Create: `Components/Pages/WarningDetail.razor`, `Components/Pages/WarningDetail.razor.css`

- [ ] **Step 1: Page**

```razor
@page "/warning/{Id}"
@inject SevereWeatherMonitor Severe
@inject DisplaySettings Display

<div class="stack">
    @if (Severe.Find(Id) is { } warning)
    {
        <div class="page-intro">
            <span class="mono-label">VÝSTRAHA · @warning.Source.ToUpper(Czech.Culture)</span>
            <h1 class="page-title">@warning.Name</h1>
            <span class="page-sub">@WarningText.Period(warning, Display.Units, DateTimeOffset.Now)</span>
        </div>

        <section class="card warning-facts">
            <div class="fact"><span class="fact-key">Oblast</span><span class="fact-value">@warning.Area</span></div>
            <div class="fact"><span class="fact-key">Od</span><span class="fact-value">@When(warning.Start)</span></div>
            <div class="fact"><span class="fact-key">Do</span><span class="fact-value">@When(warning.End)</span></div>
            <div class="fact"><span class="fact-key">Stav</span><span class="fact-value">@WarningText.Status(warning.Status)</span></div>
            <div class="fact"><span class="fact-key">Vydal</span><span class="fact-value">@warning.Source</span></div>
        </section>

        @if (!string.IsNullOrWhiteSpace(warning.Details))
        {
            <section class="card warning-details">@warning.Details</section>
        }
        @if (!string.IsNullOrWhiteSpace(warning.Disclaimer))
        {
            <p class="warning-disclaimer">@warning.Disclaimer</p>
        }
    }
    else
    {
        <div class="page-intro">
            <span class="mono-label">VÝSTRAHA</span>
            <h1 class="page-title">Výstraha už neplatí</h1>
            <a class="page-sub" href="">Zpět na předpověď</a>
        </div>
    }
</div>

@code {
    [Parameter] public string Id { get; set; } = "";

    // "st 14. 10. 15:00", in the place's clock.
    string When(DateTimeOffset t) => $"{t.DateTime.ToString("ddd d. M.", Czech.Culture)} {Display.Units.Time(t.DateTime)}";
}
```

- [ ] **Step 2: Styles** — `Components/Pages/WarningDetail.razor.css`:

```css
.warning-facts {
    display: flex;
    flex-direction: column;
    padding: 4px 18px;
}

.fact {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    padding: 12px 0;
}

    .fact + .fact {
        border-top: 1px solid var(--line);
    }

.fact-key {
    color: var(--mut);
}

.fact-value {
    font-weight: 500;
    text-align: right;
}

/* The agency's own text, with its line breaks. */
.warning-details {
    padding: 16px 18px;
    font-size: 15px;
    line-height: 1.5;
    white-space: pre-line;
}

.warning-disclaimer {
    margin: 0 4px;
    font-size: 12.5px;
    color: var(--mut);
}
```

- [ ] **Step 3: Build**

Run the build command. Expected: `Počet chyb: 0`.

---

### Task 9: Settings switch

**Files:**
- Modify: `Components/Pages/Settings.razor`

- [ ] **Step 1: Inject** — add `@inject SevereWeatherMonitor Severe` after `@inject MorningSummaryService Morning`.

- [ ] **Step 2: Replace the placeholder toggles** — replace the whole block

```razor
                @foreach (var toggle in Toggles)
                {
                    ...
                }
```

with:

```razor
                <button type="button" class="row toggle-row toggle-btn" @onclick="ToggleSevereAsync">
                    <div class="row-text">
                        <span class="toggle-label">Nebezpečné počasí</span>
                        <span class="row-sub">@SevereSubtitle</span>
                    </div>
                    <div class="switch @(Severe.Enabled ? "on" : "")" role="switch" aria-checked="@(Severe.Enabled ? "true" : "false")">
                        <div class="switch-knob"></div>
                    </div>
                </button>
                @if (severeError is not null)
                {
                    <div class="row plan-row"><span class="row-sub key-error">@severeError</span></div>
                }
                @if (Severe.Enabled)
                {
                    @* When the warnings are checked next, and what the last check found. *@
                    <div class="row plan-row">
                        <div class="plan-line">
                            <span class="mono-label plan-key">DALŠÍ KONTROLA</span>
                            <span class="plan-time">@(Severe.NextCheck is { } nextSevere ? FormatTime(nextSevere) : "–")</span>
                        </div>
                        <span class="row-sub">@Severe.NextCheckReason</span>
                        @if (Severe.LastCheck is { } lastSevere)
                        {
                            <span class="row-sub plan-last">Poslední kontrola @FormatTime(lastSevere)</span>
                        }
                    </div>
                }
```

- [ ] **Step 3: Code** — in `@code`:
  - delete `record Toggle(string Label, string Sub, bool On);` and the `Toggles` array;
  - add:

```csharp
    // Why warnings couldn't be switched on (no Azure Maps key), shown under the switch.
    string? severeError;

    async Task ToggleSevereAsync() => severeError = await Severe.ToggleAsync();

    string SevereSubtitle => Severe.Enabled
        ? $"Oficiální výstrahy pro {State.SelectedLocation?.Name ?? "vybrané místo"} · Azure Maps"
        : "Oficiální výstrahy pro vybrané místo · potřebuje klíč Azure Maps";
```

  - in `OnInitializedAsync` add `Severe.Changed += OnStateChanged;` and in `Dispose` add `Severe.Changed -= OnStateChanged;`.

- [ ] **Step 4: Build**

Run the build command. Expected: `Počet chyb: 0`.

---

### Task 10: End-to-end on the emulator

- [ ] **Step 1: Re-run the logic test** — `dotnet run severe-warning-diff.cs` in `tests/` → `ALL OK`.

- [ ] **Step 2: Deploy** — start the emulator if needed (`%LOCALAPPDATA%\Android\Sdk\emulator\emulator.exe -avd MAUI_Emulator_API_36 -no-snapshot-save -no-boot-anim -no-audio`), then the emulator deploy command. Expected: `Počet chyb: 0`, app running (`adb -s emulator-5554 shell pidof com.companyname.vibeweather` prints a pid).

- [ ] **Step 3: Find a place with an active warning** — US warnings are public:

```bash
curl -s -H "User-Agent: vibeweather-test" "https://api.weather.gov/alerts/active?status=actual&message_type=alert" \
  | py -c "import json,sys; f=json.load(sys.stdin)['features']; [print(x['properties']['event'],'|',x['properties']['areaDesc'][:80]) for x in f[:15]]"
```

Pick a town from an `areaDesc` (e.g. a county seat), add it in the app (Nastavení → Přidat nebo odebrat místa → search), select it.

- [ ] **Step 4: Switch on** — Nastavení → "Nebezpečné počasí". Expected within ~20 s: a notification "{name} · {place}" with the period and area; Settings shows "Aktivní výstrahy: N" and the next check (+1 h, or the start of the alert hours).

```bash
adb -s emulator-5554 shell dumpsys notification --noredact | grep -A30 "pkg=com.companyname.vibeweather" | grep -E "android.title=|android.text="
```

- [ ] **Step 5: Banner and detail** — Teď shows the banner(s); tapping opens `/warning/...` with Oblast/Od/Do/Stav/Vydal and the agency's text. Tapping the notification (also with the app closed: `adb shell am force-stop com.companyname.vibeweather` first) opens the same page.

- [ ] **Step 6: No repeat** — run a second check without changing anything (switching off/on or changing place would clear the store, so they're not this test). Fire the receiver directly as root on the emulator:

```bash
adb -s emulator-5554 root
adb -s emulator-5554 shell dumpsys package com.companyname.vibeweather | grep -o "crc64[0-9a-f]*\.SevereWeatherReceiver" | head -1
# use the printed name:
adb -s emulator-5554 shell am broadcast -n com.companyname.vibeweather/<printed name>
```

Expected: Settings' "Poslední kontrola" moves to now, and the notification's `when=` in `dumpsys notification` is unchanged (no new notification for unchanged warnings).

- [ ] **Step 7: Off** — switch off: its notifications disappear, banner gone, Settings shows no plan rows.

- [ ] **Step 8: No key** — (optional) remove the Azure key and try switching on: the message "Výstrahy potřebují klíč Azure Maps…" appears and the switch stays off.
