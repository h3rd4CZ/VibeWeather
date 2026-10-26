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
    readonly LocationSelection selection;
    readonly PrecipAlertMonitor precip;
    readonly ISevereWeatherScheduler scheduler;
    readonly ISevereWarningNotifier notifier;
    readonly ErrorReporter errors;
    readonly ILogger<SevereWeatherMonitor> logger;
    readonly SemaphoreSlim checkLock = new(1, 1);

    public SevereWeatherMonitor(AzureMapsSevereAlerts alerts, WeatherState weather, LocationSelection selection,
        PrecipAlertMonitor precip, ISevereWeatherScheduler scheduler, ISevereWarningNotifier notifier, ErrorReporter errors,
        ILogger<SevereWeatherMonitor> logger)
    {
        this.alerts = alerts;
        this.weather = weather;
        this.selection = selection;
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
        var place = (await selection.ResolveAsync(background: true, cancellationToken))?.Location;
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
