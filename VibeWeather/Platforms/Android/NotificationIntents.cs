using Android.App;
using Android.Content;
using VibeWeather.Services;

namespace VibeWeather.Platforms.Android;

/// <summary>Notification taps that open the app on a location's forecast.</summary>
internal static class NotificationIntents
{
    const string LocationKeyExtra = "vibeweather.location_key";
    const string RouteExtra = "vibeweather.route";

    /// <summary>Opens the app (or brings it forward) with <paramref name="locationKey"/> selected, on the tab at <paramref name="route"/>.</summary>
    /// <param name="requestCode">Distinct per notification, so their extras don't overwrite each other.</param>
    public static PendingIntent OpenLocation(Context context, string locationKey, string route, int requestCode)
    {
        var intent = new Intent(context, typeof(MainActivity))
            .SetAction(Intent.ActionView)!
            .PutExtra(LocationKeyExtra, locationKey)!
            .PutExtra(RouteExtra, route)!
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(context, requestCode, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    /// <summary>Applies an intent from <see cref="OpenLocation"/>; returns false for other intents.</summary>
    public static bool TryHandle(Intent intent, WeatherState weather)
    {
        if (intent.GetStringExtra(LocationKeyExtra) is not { } key) return false;
        var route = intent.GetStringExtra(RouteExtra) ?? "";
        intent.RemoveExtra(LocationKeyExtra);
        intent.RemoveExtra(RouteExtra);
        _ = weather.ShowAsync(key, route);
        return true;
    }
}
