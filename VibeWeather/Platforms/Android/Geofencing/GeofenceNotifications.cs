using Android.App;
using Android.Content;
using AndroidX.Core.App;
using VibeWeather.Data;

namespace VibeWeather.Platforms.Android.Geofencing;

/// <summary>"Dorazili jste: …" notifications when the device enters a geofence.</summary>
internal static class GeofenceNotifications
{
    /// <summary>Intent extra with the geofence id; MainActivity selects that place when opened from the notification.</summary>
    public const string GeofenceIdExtra = "vibeweather.geofence_id";

    const string ChannelId = "geofence_arrivals";

    /// <param name="weather">The place's forecast, or null if it couldn't be loaded in time.</param>
    public static void ShowArrival(Context context, GeofencePlace geofence, ForecastView? weather)
    {
        var manager = NotificationManagerCompat.From(context);
        // Also false when the user hasn't granted the Android 13+ notification permission.
        if (manager is null || !manager.AreNotificationsEnabled()) return;
        EnsureChannel(context);

        var builder = new NotificationCompat.Builder(context, ChannelId)
            .Brand(context, NotificationKind.Arrival)
            .SetContentIntent(OpenPlaceIntent(context, geofence))!
            .SetAutoCancel(true)!;

        if (weather is null)
        {
            // The place name after a colon, so it never needs a Czech case ending.
            builder.SetContentTitle($"Dorazili jste: {geofence.Place.Name}")!
                .SetContentText("Klepnutím zobrazíte počasí na tomto místě.");
        }
        else
        {
            // "Dorazili jste: Praha · 12°" / "Polojasno · max. 15° min. 8°", plus the next hour when expanded.
            var brief = $"{weather.CurrentDescription} · max. {weather.TodayHigh} min. {weather.TodayLow}";
            builder.SetContentTitle($"Dorazili jste: {geofence.Place.Name} · {weather.CurrentTemp}°")!
                .SetContentText(brief)!
                .SetStyle(new NotificationCompat.BigTextStyle()
                    .BigText($"{brief}. {weather.FeelsLike}.\n{weather.SummaryTitle}."));
        }

        // One notification per geofence; a later arrival replaces the earlier one.
        manager.Notify(geofence.Id.GetHashCode(), builder.Build());
    }

    // Opens the app (or brings it forward) with this geofence's place selected as the weather location.
    static PendingIntent OpenPlaceIntent(Context context, GeofencePlace geofence)
    {
        var intent = new Intent(context, typeof(MainActivity))
            .SetAction(Intent.ActionView)!
            .PutExtra(GeofenceIdExtra, geofence.Id)!
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        // A distinct request code per geofence keeps each notification's extras from overwriting another's.
        return PendingIntent.GetActivity(
            context, geofence.Id.GetHashCode(), intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var channel = new NotificationChannel(ChannelId, "Příjezd do zóny", NotificationImportance.Default)
        {
            Description = "Když dorazíte do zóny kolem některého z vašich míst",
        };
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(channel);
    }
}
