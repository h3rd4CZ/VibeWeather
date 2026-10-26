using Android.Content;
using Android.Graphics;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using VibeWeather.Data;

namespace VibeWeather.Platforms.Android;

/// <summary>What a notification is about; each kind has its own status-bar icon, accent colour and picture.</summary>
internal enum NotificationKind { MorningSummary, Precipitation, Warning, Arrival }

/// <summary>The Vibe Weather look of a notification (art in Resources/Images, colours in values/colors.xml).</summary>
internal static class NotificationBrand
{
    /// <summary>
    /// Sets the small icon, the accent colour (small-icon circle and app name) and the large picture; <paramref name="picture"/>
    /// replaces the kind's usual picture (e.g. <see cref="PrecipPicture"/>).
    /// </summary>
    public static NotificationCompat.Builder Brand(this NotificationCompat.Builder builder, Context context, NotificationKind kind,
        int? picture = null)
    {
        var (small, color, large) = kind switch
        {
            NotificationKind.MorningSummary => (Resource.Drawable.ic_stat_sun, Resource.Color.vw_notif_summary, Resource.Drawable.ic_large_sun),
            NotificationKind.Precipitation => (Resource.Drawable.ic_stat_precip, Resource.Color.vw_notif_precip, Resource.Drawable.ic_large_rain),
            NotificationKind.Warning => (Resource.Drawable.ic_stat_warning, Resource.Color.vw_notif_warning, Resource.Drawable.ic_large_warning),
            _ => (Resource.Drawable.ic_stat_place, Resource.Color.vw_notif_arrival, Resource.Drawable.ic_large_place),
        };
        return builder.SetSmallIcon(small)!
            .SetColor(ContextCompat.GetColor(context, color))!
            .SetLargeIcon(BitmapFactory.DecodeResource(context.Resources, picture ?? large))!;
    }

    /// <summary>The precipitation alert's picture for the kind of precipitation.</summary>
    public static int PrecipPicture(PrecipType type) => type switch
    {
        PrecipType.Snow => Resource.Drawable.ic_large_snow,
        PrecipType.Sleet => Resource.Drawable.ic_large_sleet,
        PrecipType.IceRain => Resource.Drawable.ic_large_icerain,
        _ => Resource.Drawable.ic_large_rain,
    };
}
