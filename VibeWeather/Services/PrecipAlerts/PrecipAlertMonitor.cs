using System.Globalization;
using Microsoft.Extensions.Logging;
using VibeWeather.Data;
using VibeWeather.Services.Diagnostics;

namespace VibeWeather.Services.PrecipAlerts;

/// <summary>Runs <see cref="PrecipAlertMonitor.RunCheckAsync"/> at a set time, also when the app isn't open.</summary>
public interface IPrecipAlertScheduler
{
    /// <summary>Replaces any pending check with one at <paramref name="atUtc"/>; to the minute when <see cref="CanBeExact"/>.</summary>
    void Schedule(DateTime atUtc);

    void Cancel();

    bool IsScheduled { get; }

    /// <summary>False when the platform may run checks late (Android without the "Alarms &amp; reminders" permission).</summary>
    bool CanBeExact { get; }

    /// <summary>Lets the user allow exact timing, e.g. by opening the system permission page.</summary>
    void RequestExactTiming();
}

/// <summary>
/// A notification to post: precipitation has started, or is about to, at <see cref="Location"/>. <see cref="Type"/> is
/// the kind it starts as, which picks the notification's picture.
/// </summary>
public sealed record PrecipAlert(WeatherLocation Location, string Title, string Text, PrecipType Type);

/// <summary>
/// Precipitation alerts for the selected weather location, timed from the minute forecast. Every check downloads a
/// fresh forecast and plans the next one:
/// <list type="bullet">
/// <item>Nothing expected in the forecast window: check again <see cref="DryWindowMargin"/> before the window runs
/// out, but no sooner than after <see cref="DryInterval"/>.</item>
/// <item>A spell expected later in the window: alert, then check again exactly <see cref="LeadTime"/> before it
/// starts.</item>
/// <item>A spell under way or starting within <see cref="LeadTime"/>: alert, then check exactly when it's expected
/// to end; that check finds the next spell, or goes back to the regular interval.</item>
/// </list>
/// Every check that finds precipitation in the window alerts; each alert replaces the previous one.
/// Checks run only within <see cref="Hours"/>; one planned outside them waits until they next start.
/// The plan is kept in preferences so Settings can show it and it can be restored after a reboot.
/// </summary>
public sealed class PrecipAlertMonitor
{
    /// <summary>Above this a minute counts as wet: the same as the Next hour chart, so every wet section it shows alerts.</summary>
    public const double ThresholdMmPerHour = ForecastView.WetMmPerHour;

    /// <summary>How long before a spell starts the alert comes.</summary>
    public static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(10);

    /// <summary>Shortest wait between checks while nothing is expected in the forecast window; also the retry interval.</summary>
    public static readonly TimeSpan DryInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// While nothing is expected, the next check comes this long before the forecast window runs out, so each
    /// forecast is used for as long as it reaches (fewer requests). At least <see cref="LeadTime"/>, so a spell
    /// starting just past the window still gets its alert in time; the rest covers an alarm firing late.
    /// </summary>
    public static readonly TimeSpan DryWindowMargin = LeadTime + TimeSpan.FromMinutes(5);

    // Dry breaks shorter than this don't split a spell; the Next hour summary splits sections the same way.
    const int MergeGapMinutes = ForecastView.MergeGapMinutes;
    // A spell starting this much later than LeadTime still counts as due (the alarm itself fires a few seconds late).
    static readonly TimeSpan DueTolerance = TimeSpan.FromMinutes(1);
    // Never plan the next check sooner than this.
    static readonly TimeSpan MinDelay = TimeSpan.FromMinutes(1);
    static readonly TimeSpan TimeoutRetry = TimeSpan.FromMinutes(2);
    // How far ahead the hourly forecast is searched for the end of a spell; a longer one is re-checked then.
    static readonly TimeSpan MaxSpellLookahead = TimeSpan.FromHours(6);

    const string EnabledPreference = "precip_alerts_enabled";
    const string NextAtPreference = "precip_alert_next_at";
    const string NextReasonPreference = "precip_alert_next_reason";
    const string OffsetPreference = "precip_alert_location_offset";
    const string LastCheckPreference = "precip_alert_last_check";
    const string WeekdayFromPreference = "precip_alert_weekday_from";
    const string WeekdayToPreference = "precip_alert_weekday_to";
    const string WeekendFromPreference = "precip_alert_weekend_from";
    const string WeekendToPreference = "precip_alert_weekend_to";

    readonly IWeatherProvider provider;
    readonly WeatherState weather;
    readonly LocationSelection selection;
    readonly IPrecipAlertScheduler scheduler;
    readonly DisplaySettings display;
    readonly ErrorReporter errors;
    readonly ILogger<PrecipAlertMonitor> logger;
    readonly SemaphoreSlim checkLock = new(1, 1);

    public PrecipAlertMonitor(IWeatherProvider provider, WeatherState weather, LocationSelection selection,
        IPrecipAlertScheduler scheduler, WeatherSourceSettings source, DisplaySettings display, ErrorReporter errors,
        ILogger<PrecipAlertMonitor> logger)
    {
        this.provider = provider;
        this.weather = weather;
        this.selection = selection;
        this.scheduler = scheduler;
        this.display = display;
        this.errors = errors;
        this.logger = logger;
        // A different place, or another weather service, may see it wet or dry right now: check straight away.
        weather.SelectionChanged += () =>
        {
            if (!Enabled) return;
            CheckNow();
        };
        source.Changed += () =>
        {
            if (Enabled) CheckNow();
        };
    }

    public event Action? Changed;

    public bool Enabled => Preferences.Default.Get(EnabledPreference, false);

    /// <summary>Whether checks run on time; false means Android may run them late.</summary>
    public bool OnTime => scheduler.CanBeExact;

    /// <summary>When the next check is planned, in the selected location's time (as forecast times are shown).</summary>
    public DateTime? NextCheck => Preferences.Default.Get(NextAtPreference, 0L) is > 0 and var ticks
        ? LocationTime(new DateTime(ticks, DateTimeKind.Utc))
        : null;

    /// <summary>Why the next check is at <see cref="NextCheck"/>, e.g. "10 min před začátkem srážek: déšť v 17:28".</summary>
    public string NextCheckReason => Preferences.Default.Get(NextReasonPreference, "");

    /// <summary>When the last check ran, in the selected location's time.</summary>
    public DateTime? LastCheck => Preferences.Default.Get(LastCheckPreference, 0L) is > 0 and var ticks
        ? LocationTime(new DateTime(ticks, DateTimeKind.Utc))
        : null;

    /// <summary>When checks (and so alerts) may happen; none outside them, e.g. at night.</summary>
    public AlertHours Hours => new(
        LoadTime(WeekdayFromPreference, AlertHours.Default.WeekdayFrom),
        LoadTime(WeekdayToPreference, AlertHours.Default.WeekdayTo),
        LoadTime(WeekendFromPreference, AlertHours.Default.WeekendFrom),
        LoadTime(WeekendToPreference, AlertHours.Default.WeekendTo));

    /// <summary>Saves new alert hours and plans the next check by them.</summary>
    public void SetHours(AlertHours hours)
    {
        StoreTime(WeekdayFromPreference, hours.WeekdayFrom);
        StoreTime(WeekdayToPreference, hours.WeekdayTo);
        StoreTime(WeekendFromPreference, hours.WeekendFrom);
        StoreTime(WeekendToPreference, hours.WeekendTo);
        // Within the new hours this checks now; outside them it waits for their start.
        if (Enabled) CheckNow();
        Changed?.Invoke();
    }

    /// <summary>Turns alerts on (asking for notification permission) and checks right away.</summary>
    public async Task EnableAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        });
        Preferences.Default.Set(EnabledPreference, true);
        CheckNow();
    }

    public void Disable()
    {
        Preferences.Default.Set(EnabledPreference, false);
        scheduler.Cancel();
        Preferences.Default.Remove(NextAtPreference);
        Preferences.Default.Remove(NextReasonPreference);
        Changed?.Invoke();
    }

    public Task ToggleAsync()
    {
        if (!Enabled) return EnableAsync();
        Disable();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Makes sure the planned check is pending, e.g. after a reboot (which clears alarms) or an app update.
    /// Does nothing when disabled or already pending.
    /// </summary>
    public void EnsureScheduled()
    {
        if (!Enabled) return;
        var planned = PlannedUtc;
        if (scheduler.IsScheduled && planned > DateTime.UtcNow - MinDelay) return;
        scheduler.Schedule(planned > DateTime.UtcNow ? planned : DateTime.UtcNow);
    }

    /// <summary>Re-arms the planned check, e.g. after the user allowed exact alarms, and updates Settings.</summary>
    public void Refresh()
    {
        if (!Enabled) return;
        var planned = PlannedUtc;
        scheduler.Schedule(planned > DateTime.UtcNow ? planned : DateTime.UtcNow);
        Changed?.Invoke();
    }

    /// <summary>
    /// One check of the selected location, run by the scheduled alarm: plans and schedules the next check and
    /// returns the alert to post, if any. Never throws; failures are logged and retried.
    /// </summary>
    public async Task<PrecipAlert?> RunCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled) return null;
        await checkLock.WaitAsync(CancellationToken.None);
        try
        {
            var (nextUtc, reason, alert) = await CheckAsync(cancellationToken);
            Plan(nextUtc, reason);
            return alert;
        }
        catch (OperationCanceledException)
        {
            Plan(DateTime.UtcNow + TimeoutRetry, "Nový pokus: poslední kontrola nestihla doběhnout");
            return null;
        }
        catch (Exception ex)
        {
            // Anything unexpected must not stop the alerts for good.
            errors.Report(ex, "Zjišťování srážek", popup: false);
            Plan(DateTime.UtcNow + DryInterval, "Nový pokus po chybě");
            return null;
        }
        finally
        {
            checkLock.Release();
        }
    }

    async Task<(DateTime NextUtc, string Reason, PrecipAlert? Alert)> CheckAsync(CancellationToken cancellationToken)
    {
        // The same location as the app shows; "Aktuální poloha" is found again here (at most an hour old).
        var location = (await selection.ResolveAsync(background: true, cancellationToken))?.Location;
        if (location is null)
        {
            logger.LogInformation("Precipitation check skipped: no known position for the selected location");
            return (DateTime.UtcNow + DryInterval, "Poloha vybraného místa zatím není známá", null);
        }

        Forecast forecast;
        try
        {
            // Fresh, not cached: the alert must reflect the forecast at this very moment.
            forecast = await provider.GetFreshForecastAsync(location, cancellationToken);
        }
        catch (WeatherProviderException ex)
        {
            errors.Report(ex, $"Zjišťování srážek pro místo {location.Name}", popup: false);
            var retry = ex.RetryAfter is { } wait && wait > DryInterval ? wait : DryInterval;
            return (DateTime.UtcNow + retry, $"Nový pokus po chybě: {ex.Message}", null);
        }

        Preferences.Default.Set(LastCheckPreference, DateTime.UtcNow.Ticks);
        // Forecast times are the location's local time; this converts them to UTC for the alarm.
        var offset = TimeSpan.FromMinutes(Math.Round((forecast.LocalNow - DateTime.UtcNow).TotalMinutes));
        Preferences.Default.Set(OffsetPreference, (int)offset.TotalMinutes);
        DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);

        var window = forecast.LocalNow.AddMinutes(forecast.Minutes.Count);
        var spell = Spells(forecast).FirstOrDefault();
        if (spell is null)
        {
            // A short window (or none) still waits the regular interval.
            var dryNext = Max(ToUtc(window) - DryWindowMargin, DateTime.UtcNow + DryInterval);
            return (dryNext, $"Do {display.Units.Time(window)} se srážky nečekají", null);
        }

        // "déšť", "sníh", "mrznoucí déšť", "déšť se sněhem"
        var kind = ForecastView.PrecipName(spell.Type).ToLower(Czech.Culture);
        // Precipitation anywhere in the window is announced at every check, also when it's still far off.
        var alert = Describe(location, forecast, spell);
        if (spell.Start - forecast.LocalNow > LeadTime + DueTolerance)
        {
            // Look again exactly LeadTime before it, with a fresh forecast then.
            return (ToUtc(spell.Start - LeadTime),
                $"{LeadTime.TotalMinutes:0} min před začátkem srážek: {kind} v {display.Units.Time(spell.Start)}", alert);
        }

        // Falling now or about to: check again when it's expected to end.
        var endUtc = ToUtc(spell.End);
        var next = endUtc > DateTime.UtcNow + MinDelay ? endUtc : DateTime.UtcNow + MinDelay;
        var reason = spell.OpenEnded
            ? $"Opakovaná kontrola: {kind} má trvat déle než {HoursText(MaxSpellLookahead)}"
            : $"Až má skončit {kind}";
        return (next, reason, alert);
    }

    /// <summary>A spell of precipitation, in the location's local time.</summary>
    /// <param name="OpenEnded">Still going at the end of the hourly search (<see cref="MaxSpellLookahead"/>).</param>
    /// <param name="WaterMm">What falls in the part of the spell inside the minute forecast, as water (mm).</param>
    /// <param name="SnowCm">New snow (cm) in that same part.</param>
    /// <param name="PastWindow">Still going where the minute forecast ends, so the amounts are up to then.</param>
    sealed record Spell(DateTime Start, DateTime End, PrecipType Type, double PeakMmPerHour, bool OpenEnded,
        double WaterMm, double SnowCm, bool PastWindow);

    /// <summary>
    /// Spells in the minute forecast, with dry breaks under <see cref="MergeGapMinutes"/> joined. A spell still going
    /// at the end of the minute forecast continues into the hourly forecast until an hour that's dry.
    /// </summary>
    static List<Spell> Spells(Forecast forecast)
    {
        var minutes = forecast.Minutes;
        var runs = new List<(int Start, int End)>();
        for (int i = 0; i < minutes.Count; i++)
        {
            if (minutes[i].Intensity <= ThresholdMmPerHour) continue;
            if (runs.Count > 0 && i - runs[^1].End < MergeGapMinutes) runs[^1] = (runs[^1].Start, i + 1);
            else runs.Add((i, i + 1));
        }

        return runs.Select(run =>
        {
            var (end, open) = run.End >= minutes.Count
                ? EndAfterMinutes(forecast)
                : (forecast.LocalNow.AddMinutes(run.End), false);
            var wet = minutes.Skip(run.Start).Take(run.End - run.Start).ToList();
            return new Spell(forecast.LocalNow.AddMinutes(run.Start), end, wet[0].Type, wet.Max(m => m.Intensity), open,
                wet.Sum(m => m.Intensity) / 60, ForecastView.NewSnowCm(wet), run.End >= minutes.Count);
        }).ToList();
    }

    /// <summary>
    /// For a spell running past the minute forecast: the start of the first dry hour after it (hourly totals are for
    /// the hour starting at each time), or the lookahead limit.
    /// </summary>
    static (DateTime End, bool OpenEnded) EndAfterMinutes(Forecast forecast)
    {
        var afterMinutes = forecast.LocalNow.AddMinutes(forecast.Minutes.Count);
        var limit = forecast.LocalNow + MaxSpellLookahead;
        foreach (var hour in forecast.Hours)
        {
            if (hour.Time.AddHours(1) <= afterMinutes) continue;
            if (hour.Time >= limit) break;
            if (hour.Precipitation <= ThresholdMmPerHour) return (Max(hour.Time, afterMinutes), false);
        }
        return (limit, true);
    }

    PrecipAlert Describe(WeatherLocation location, Forecast forecast, Spell spell)
    {
        // "Slabý déšť právě teď · Praha" / "Mírný sníh od 17:28 · Inuvik"; the strength is the spell's peak in the
        // minute forecast. The place name comes last, so it never needs a Czech case ending ("v Praze").
        // Text: "Celkem asi 1,2 mm srážek (asi 2 cm nového sněhu). Začne asi za 8 min. Má skončit kolem 18:10."
        var kind = $"{ForecastView.Category(spell.PeakMmPerHour)} {ForecastView.PrecipName(spell.Type).ToLower(Czech.Culture)}";
        kind = $"{char.ToUpper(kind[0], Czech.Culture)}{kind[1..]}";
        bool now = spell.Start <= forecast.LocalNow;
        var title = now
            ? $"{kind} právě teď · {location.Name}"
            : $"{kind} od {display.Units.Time(spell.Start)} · {location.Name}";
        var text = spell.OpenEnded
            ? $"Má trvat déle než {HoursText(MaxSpellLookahead)}."
            : $"Má skončit kolem {display.Units.Time(spell.End)}.";
        if (!now) text = $"Začne asi za {(int)Math.Round((spell.Start - forecast.LocalNow).TotalMinutes)} min. " + text;

        // The amount first, so it shows on the collapsed notification: everything in the spell's part of the minute
        // forecast (what the Next hour chart shows) as water, plus the new snow when there is some. Snow may also
        // come later in the spell (rain turning to snow).
        bool snow = spell.SnowCm >= ForecastView.MinSnowCm;
        var water = spell.WaterMm < 0.05 ? "méně než 0,1 mm" : $"asi {Czech.Number(spell.WaterMm)} mm";
        var window = forecast.LocalNow.AddMinutes(forecast.Minutes.Count);
        var amount = (spell.PastWindow ? $"Do {display.Units.Time(window)} {water}" : $"Celkem {water}") + " srážek"
            + (snow ? $" (asi {ForecastView.SnowAmount(spell.SnowCm)} nového sněhu)." : ".");
        if (snow && spell.Type is PrecipType.Rain or PrecipType.IceRain) amount += " Přejde ve sněžení.";
        return new PrecipAlert(location, title, $"{amount} {text}", spell.Type);
    }

    /// <summary>Stores the plan for Settings and sets the alarm.</summary>
    void Plan(DateTime atUtc, string reason)
    {
        if (!Enabled) return;
        // Outside alert hours: skip the checks in between and start again when they do.
        var local = atUtc.ToLocalTime();
        var allowed = Hours.NextAllowed(local);
        if (allowed != local)
        {
            atUtc = DateTime.SpecifyKind(allowed, DateTimeKind.Local).ToUniversalTime();
            reason = "Začátek doby upozornění";
        }
        Preferences.Default.Set(NextAtPreference, atUtc.Ticks);
        Preferences.Default.Set(NextReasonPreference, reason);
        scheduler.Schedule(atUtc);
        logger.LogInformation("Next precipitation check at {At:u}: {Reason}", atUtc, reason);
        Changed?.Invoke();
    }

    /// <summary>Plans a check right away (the alarm fires immediately).</summary>
    void CheckNow() => Plan(DateTime.UtcNow, "Kontrola právě probíhá");

    DateTime PlannedUtc => new(Preferences.Default.Get(NextAtPreference, 0L), DateTimeKind.Utc);

    // The last checked location's UTC offset; the device's until a check has run.
    static DateTime LocationTime(DateTime utc) =>
        Preferences.Default.ContainsKey(OffsetPreference)
            ? DateTime.SpecifyKind(utc.AddMinutes(Preferences.Default.Get(OffsetPreference, 0)), DateTimeKind.Unspecified)
            : utc.ToLocalTime();

    /// <summary>"6 hodin", "2 hodiny", "1 hodinu" (after "déle než").</summary>
    static string HoursText(TimeSpan span)
    {
        int n = (int)Math.Round(span.TotalHours);
        return Czech.Count(n, "hodinu", "hodiny", "hodin");
    }

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    static TimeOnly LoadTime(string preference, TimeOnly fallback) =>
        TimeOnly.TryParseExact(Preferences.Default.Get(preference, ""), "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time) ? time : fallback;

    static void StoreTime(string preference, TimeOnly time) =>
        Preferences.Default.Set(preference, time.ToString("HH:mm", CultureInfo.InvariantCulture));
}
