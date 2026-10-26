using Android.Content;
using VibeWeather.Services.PrecipAlerts;

namespace VibeWeather.Platforms.Android.PrecipAlerts;

/// <summary>The precipitation check alarm: downloads a fresh forecast, posts an alert if due and sets the next alarm.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class PrecipAlertReceiver : BroadcastReceiver
{
    // Broadcast receivers get about 10 seconds; a timed-out check is retried shortly (see PrecipAlertMonitor).
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);

    public override void OnReceive(Context? context, Intent? intent)
    {
        // MauiApplication builds the service container on process start, including starts for a broadcast.
        var monitor = IPlatformApplication.Current?.Services.GetService<PrecipAlertMonitor>();
        if (context is null || monitor is null || !monitor.Enabled) return;

        var appContext = context.ApplicationContext ?? context;
        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(CheckTimeout);
                if (await monitor.RunCheckAsync(timeout.Token) is { } alert)
                    PrecipNotifications.Show(appContext, alert);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
