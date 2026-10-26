using Android.App;
using Android.Content;
using AndroidX.Core.App;
using VibeWeather.Services.MorningSummary;

namespace VibeWeather.Platforms.Android.MorningSummary;

/// <summary>The morning alarm: loads today's forecast, posts the summary and schedules tomorrow's.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class MorningSummaryReceiver : BroadcastReceiver
{
    const string ChannelId = "morning_summary";
    const int NotificationId = 7104;

    // Broadcast receivers get about 10 seconds; leave room to post a fallback notification.
    static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(8);

    public override void OnReceive(Context? context, Intent? intent)
    {
        var summary = IPlatformApplication.Current?.Services.GetService<MorningSummaryService>();
        if (context is null || summary is null || !summary.Enabled) return;

        var appContext = context.ApplicationContext ?? context;
        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(LoadTimeout);
                Show(appContext, await summary.BuildAsync(timeout.Token));
            }
            finally
            {
                summary.ScheduleNext();
                pending?.Finish();
            }
        });
    }

    /// <param name="note">The summary, or null if the forecast couldn't be loaded in time.</param>
    internal static void Show(Context context, MorningSummaryNote? note)
    {
        var manager = NotificationManagerCompat.From(context);
        // Also false when the user hasn't granted the Android 13+ notification permission.
        if (manager is null || !manager.AreNotificationsEnabled()) return;
        EnsureChannel(context);

        var builder = new NotificationCompat.Builder(context, ChannelId)
            .Brand(context, NotificationKind.MorningSummary)
            .SetAutoCancel(true)!;

        if (note is null)
        {
            var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
            builder.SetContentTitle("Dobré ráno")!
                .SetContentText("Dnešní předpověď se nepodařilo načíst. Klepnutím otevřete aplikaci.")!
                .SetContentIntent(PendingIntent.GetActivity(context, NotificationId, launch,
                    PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
        }
        else
        {
            builder.SetContentTitle(note.Title)!
                .SetContentText(note.Text)!
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(note.Detail))!
                .SetContentIntent(NotificationIntents.OpenLocation(context, note.Location.Key, "", NotificationId));
        }

        // One a day; today's replaces yesterday's.
        manager.Notify(NotificationId, builder.Build());
    }

    static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var channel = new NotificationChannel(ChannelId, "Ranní přehled", NotificationImportance.Default)
        {
            Description = "Jednou denně dnešní předpověď pro vybrané místo",
        };
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(channel);
    }
}
