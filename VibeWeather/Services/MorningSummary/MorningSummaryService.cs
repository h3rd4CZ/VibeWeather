using System.Globalization;
using Microsoft.Extensions.Logging;
using VibeWeather.Data;
using VibeWeather.Services.Diagnostics;

namespace VibeWeather.Services.MorningSummary;

/// <summary>Fires the morning summary at a given time, also when the app isn't open.</summary>
public interface IMorningSummaryScheduler
{
    /// <summary>Replaces any pending summary with one at <paramref name="at"/> (device time).</summary>
    void Schedule(DateTime at);

    void Cancel();

    /// <summary>False when the platform may deliver the summary late (up to an hour on Android without exact alarms).</summary>
    bool CanBeExact { get; }

    /// <summary>Lets the user allow exact timing, e.g. by opening the system permission page.</summary>
    void RequestExactTiming();
}

/// <summary>The notification to post.</summary>
public sealed record MorningSummaryNote(WeatherLocation Location, string Title, string Text, string Detail);

/// <summary>
/// A daily forecast notification for the selected weather location, at a configurable time for weekdays
/// (Monday–Friday) and for weekends. Times follow the device's clock.
/// </summary>
public sealed class MorningSummaryService
{
    public static readonly TimeOnly DefaultWeekdayTime = new(7, 0);
    public static readonly TimeOnly DefaultWeekendTime = new(9, 0);

    // An hour counts as wet from this chance of precipitation.
    const int LikelyPop = 40;

    const string EnabledPreference = "morning_summary_enabled";
    const string WeekdayPreference = "morning_summary_weekday";
    const string WeekendPreference = "morning_summary_weekend";


    readonly IWeatherProvider provider;
    readonly LocationSelection selection;
    readonly IMorningSummaryScheduler scheduler;
    readonly DisplaySettings display;
    readonly ErrorReporter errors;
    readonly ILogger<MorningSummaryService> logger;

    public MorningSummaryService(IWeatherProvider provider, LocationSelection selection, IMorningSummaryScheduler scheduler,
        DisplaySettings display, ErrorReporter errors, ILogger<MorningSummaryService> logger)
    {
        this.provider = provider;
        this.selection = selection;
        this.scheduler = scheduler;
        this.display = display;
        this.errors = errors;
        this.logger = logger;
    }

    public event Action? Changed;

    public bool Enabled => Preferences.Default.Get(EnabledPreference, false);

    public TimeOnly WeekdayTime => Load(WeekdayPreference, DefaultWeekdayTime);

    public TimeOnly WeekendTime => Load(WeekendPreference, DefaultWeekendTime);

    /// <summary>Turns the summary on (asking for notification permission) and schedules the next one.</summary>
    public async Task EnableAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        });
        Preferences.Default.Set(EnabledPreference, true);
        ScheduleNext();
        Changed?.Invoke();
    }

    public void Disable()
    {
        Preferences.Default.Set(EnabledPreference, false);
        scheduler.Cancel();
        Changed?.Invoke();
    }

    public Task ToggleAsync()
    {
        if (!Enabled) return EnableAsync();
        Disable();
        return Task.CompletedTask;
    }

    public void SetWeekdayTime(TimeOnly time) => SetTime(WeekdayPreference, time);

    public void SetWeekendTime(TimeOnly time) => SetTime(WeekendPreference, time);

    /// <summary>Schedules the next summary, e.g. at app start or after a reboot. Does nothing when disabled.</summary>
    public void ScheduleNext()
    {
        if (Enabled) scheduler.Schedule(NextOccurrence(DateTime.Now));
    }

    /// <summary>Whether the summary arrives on time; false means it may be up to an hour late.</summary>
    public bool OnTime => scheduler.CanBeExact;

    public void RequestOnTime() => scheduler.RequestExactTiming();

    /// <summary>
    /// Re-schedules with the current permissions and tells the UI, e.g. when the app returns from the
    /// system settings page where exact timing may have been allowed.
    /// </summary>
    public void Refresh()
    {
        ScheduleNext();
        Changed?.Invoke();
    }

    /// <summary>The first configured time strictly after <paramref name="now"/>.</summary>
    public DateTime NextOccurrence(DateTime now)
    {
        for (int days = 0; days < 8; days++)
        {
            var date = DateOnly.FromDateTime(now).AddDays(days);
            var at = date.ToDateTime(TimeFor(date));
            if (at > now) return at;
        }
        return now.AddDays(1);
    }

    public TimeOnly TimeFor(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? WeekendTime : WeekdayTime;

    /// <summary>Today's summary for the selected location, or null if it can't be loaded (the failure is logged).</summary>
    public async Task<MorningSummaryNote?> BuildAsync(CancellationToken cancellationToken = default)
    {
        WeatherLocation? location;
        try
        {
            // The same location as the app shows; "Aktuální poloha" is found again here (at most an hour old).
            location = (await selection.ResolveAsync(background: true, cancellationToken))?.Location;
        }
        catch (OperationCanceledException ex)
        {
            errors.Report(ex, "Zjišťování polohy pro ranní přehled", popup: false);
            return null;
        }
        if (location is null)
        {
            logger.LogInformation("Morning summary skipped: no known position for the selected location");
            return null;
        }

        try
        {
            var forecast = await provider.GetForecastAsync(location, cancellationToken);
            return Describe(location, forecast, display.Units);
        }
        catch (Exception ex) when (ex is WeatherProviderException or OperationCanceledException)
        {
            errors.Report(ex, $"Načítání ranního přehledu pro místo {location.Name}", popup: false);
            return null;
        }
    }

    static MorningSummaryNote Describe(WeatherLocation location, Forecast forecast, DisplayUnits units)
    {
        var view = new ForecastView(forecast, units);
        // "Dobré ráno · Praha 6°" / "Polojasno · max. 14° min. 6°"
        var title = $"Dobré ráno · {location.Name} {view.CurrentTemp}°";
        var text = $"{view.CurrentDescription} · max. {view.TodayHigh} min. {view.TodayLow}";
        // "Polojasno, teď 6° (pocitově 4°). Dnes max. 14°, min. 6°. Pravděpodobně déšť 14:00–17:00 (až 70 %)."
        var detail = $"{view.CurrentDescription}, teď {view.CurrentTemp}° ({view.FeelsLike.ToLower(Czech.Culture)}). "
            + $"Dnes max. {view.TodayHigh}, min. {view.TodayLow}. {PrecipOutlook(forecast, units)}";
        return new MorningSummaryNote(location, title, text, detail);
    }

    /// <summary>"Pravděpodobně déšť 14:00–17:00 (až 70 %)." for the rest of today, from the hourly chances.</summary>
    static string PrecipOutlook(Forecast forecast, DisplayUnits units)
    {
        var today = DateOnly.FromDateTime(forecast.LocalNow);
        var hours = forecast.Hours.Where(h => DateOnly.FromDateTime(h.Time) == today).ToList();
        var wet = hours.Where(h => h.Pop >= LikelyPop).ToList();
        if (wet.Count == 0)
        {
            int maxPop = hours.Count == 0 ? 0 : hours.Max(h => h.Pop);
            // Czech puts a (non-breaking) space before the percent sign.
            return $"Dnes bez srážek (pravděpodobnost nejvýše {maxPop} %).";
        }

        var kind = wet.Any(h => WeatherCodes.IsSnow(h.Code)) ? "sněžení" : "déšť";
        var span = wet.Count == 1
            ? $"kolem {units.Hour(wet[0].Time)}"
            : $"{units.Hour(wet[0].Time)}–{units.Hour(wet[^1].Time.AddHours(1))}";
        return $"Pravděpodobně {kind} {span} (až {wet.Max(h => h.Pop)} %).";
    }

    void SetTime(string preference, TimeOnly time)
    {
        Preferences.Default.Set(preference, time.ToString("HH:mm", CultureInfo.InvariantCulture));
        ScheduleNext();
        Changed?.Invoke();
    }

    static TimeOnly Load(string preference, TimeOnly fallback) =>
        TimeOnly.TryParseExact(Preferences.Default.Get(preference, ""), "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time) ? time : fallback;
}
