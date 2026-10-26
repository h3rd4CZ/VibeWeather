#if DEBUG
using Android.App;
using Android.Content;
using VibeWeather.Data;
using VibeWeather.Platforms.Android.Geofencing;
using VibeWeather.Platforms.Android.MorningSummary;
using VibeWeather.Platforms.Android.PrecipAlerts;
using VibeWeather.Services.PrecipAlerts;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather.Platforms.Android;

/// <summary>
/// Debug builds only: posts one notification of each kind with sample data, through the real code, to check their
/// look without waiting for weather. Fire it with
/// <c>adb shell am broadcast -n com.companyname.vibeweather/vibeweather.debug.NotificationPreview</c>, and add
/// <c>--es kind summary|precip|warning|arrival</c> for just one (two or more get grouped by Android 16), and
/// <c>--es type rain|icerain|sleet|snow</c> for the kind of precipitation.
/// </summary>
[BroadcastReceiver(Name = "vibeweather.debug.NotificationPreview", Enabled = true, Exported = true)]
public sealed class NotificationPreviewReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        var kind = intent?.GetStringExtra("kind");
        var praha = new WeatherLocation("Praha", "Hlavní město Praha", 50.08, 14.42);

        // No forecast: the "Dobré ráno" fallback.
        if (kind is null or "summary")
            MorningSummaryReceiver.Show(context, null);
        if (kind is null or "precip")
        {
            // --es type rain|icerain|sleet|snow picks the kind of precipitation (and so the picture).
            var type = Enum.TryParse<PrecipType>(intent?.GetStringExtra("type"), true, out var t) ? t : PrecipType.Rain;
            var name = ForecastView.PrecipName(type).ToLower(Czech.Culture);
            PrecipNotifications.Show(context, new PrecipAlert(praha, $"Slabý {name} za 15 minut · Praha", "Potrvá asi 40 minut.", type));
        }
        if (kind is null or "warning")
            IPlatformApplication.Current?.Services.GetService<ISevereWarningNotifier>()?.Show(praha, new SevereWarning(
                "preview-storm", "Silné bouřky", "New", "Praha", DateTimeOffset.Now, DateTimeOffset.Now.AddHours(6),
                "ČHMÚ", "Náhled výstrahy pro kontrolu vzhledu.", null, 5));
        if (kind is null or "arrival")
            GeofenceNotifications.ShowArrival(context, new GeofencePlace("preview", praha, 500), null);
    }
}
#endif
