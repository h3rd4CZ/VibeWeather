using Android.App;
using Android.Content;
using VibeWeather.Services;
using VibeWeather.Services.MorningSummary;
using VibeWeather.Services.PrecipAlerts;

namespace VibeWeather.Platforms.Android.Geofencing;

/// <summary>
/// Android drops geofences and alarms on reboot (and may on app updates); register geofences again and
/// re-arm the morning summary and precipitation checks.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class GeofenceRestoreReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var services = IPlatformApplication.Current?.Services;
        // Alarms don't survive a reboot either; precipitation-check jobs do, but re-arm them in case.
        services?.GetService<MorningSummaryService>()?.ScheduleNext();
        services?.GetService<PrecipAlertMonitor>()?.EnsureScheduled();
        services?.GetService<VibeWeather.Services.SevereWeather.SevereWeatherMonitor>()?.EnsureScheduled();

        var geofences = services?.GetService<GeofenceManager>();
        if (geofences is null) return;

        bool rebooted = intent?.Action == Intent.ActionBootCompleted;
        // Keep the process alive until registration finishes.
        var pending = GoAsync();
        Task.Run(async () =>
        {
            try { await geofences.RestoreAsync(forgetInside: rebooted); }
            finally { pending?.Finish(); }
        });
    }
}
