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
            .Brand(Context, NotificationKind.Warning)
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
