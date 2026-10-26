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
