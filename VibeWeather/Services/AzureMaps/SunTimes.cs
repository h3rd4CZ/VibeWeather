namespace VibeWeather.Services.AzureMaps;

/// <summary>
/// Sunrise and sunset from NOAA's general solar position equations (accurate to a minute or two away from the poles).
/// Azure Maps' daily forecast doesn't include them.
/// </summary>
internal static class SunTimes
{
    /// <summary>Local sunrise and sunset; in polar day or night both fall at local midnight or noon.</summary>
    public static (DateTime Sunrise, DateTime Sunset) For(DateOnly date, double latitude, double longitude, TimeSpan utcOffset)
    {
        double gamma = 2 * Math.PI / 365 * (date.DayOfYear - 1);
        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
            - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        double lat = latitude * Math.PI / 180;
        // 90.833°: the sun's upper edge on the horizon, with refraction.
        double cosHa = Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(lat) * Math.Cos(decl)) - Math.Tan(lat) * Math.Tan(decl);
        double ha = Math.Acos(Math.Clamp(cosHa, -1, 1)) * 180 / Math.PI;

        var midnightUtc = date.ToDateTime(TimeOnly.MinValue);
        DateTime Local(double utcMinutes) => midnightUtc.AddMinutes(utcMinutes) + utcOffset;
        return (Local(720 - 4 * (longitude + ha) - eqTime), Local(720 - 4 * (longitude - ha) - eqTime));
    }
}
