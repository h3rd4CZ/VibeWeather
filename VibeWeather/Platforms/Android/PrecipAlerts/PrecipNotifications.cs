using Android.App;
using Android.Content;
using AndroidX.Core.App;
using VibeWeather.Services.PrecipAlerts;

namespace VibeWeather.Platforms.Android.PrecipAlerts;

/// <summary>"Slabý déšť právě teď · Praha" notifications.</summary>
internal static class PrecipNotifications
{
    const string ChannelId = "precip_alerts";
    const int NotificationId = 7102;

    public static void Show(Context context, PrecipAlert alert)
    {
        var manager = NotificationManagerCompat.From(context);
        // Also false when the user hasn't granted the Android 13+ notification permission.
        if (manager is null || !manager.AreNotificationsEnabled()) return;
        EnsureChannel(context);

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .Brand(context, NotificationKind.Precipitation, NotificationBrand.PrecipPicture(alert.Type))
            .SetContentTitle(alert.Title)!
            .SetContentText(alert.Text)!
            // Expandable, for the longer text with the expected snow.
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(alert.Text))!
            .SetContentIntent(NotificationIntents.OpenLocation(context, alert.Location.Key, "next-hour", NotificationId))!
            .SetAutoCancel(true)!
            .Build();

        // One at a time: each check's alert replaces the previous one.
        manager.Notify(NotificationId, notification);
    }

    static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var channel = new NotificationChannel(ChannelId, "Upozornění na srážky", NotificationImportance.High)
        {
            Description = "Když na vybraném místě začne pršet, sněžit nebo padat mrznoucí déšť",
        };
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(channel);
    }
}
