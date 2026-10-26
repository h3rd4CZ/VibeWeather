using VibeWeather.Data;

namespace VibeWeather.Services.AzureMaps;

/// <summary>
/// Converts Azure Maps weather icon codes (https://learn.microsoft.com/azure/azure-maps/weather-coverage#weather-icons)
/// to the WMO codes the rest of the app understands. Rain and snow codes are refined with the reported
/// intensity ("Light", "Moderate", "Heavy") when there is one.
/// </summary>
internal static class AzureMapsIcons
{
    public static int ToWmo(int iconCode, string? intensity = null) => iconCode switch
    {
        1 or 30 or 31 or 33 => 0,                     // sunny, hot, cold, clear (night)
        2 or 5 or 32 or 34 or 37 => 1,                // mostly sunny/clear, hazy, windy
        3 or 4 or 35 or 36 => 2,                      // partly sunny/cloudy, intermittent clouds
        6 or 7 or 8 or 38 => 3,                       // mostly cloudy, cloudy, dreary
        11 => 45,                                     // fog
        12 or 13 or 14 or 39 or 40 => ByIntensity(intensity, 80, 81, 82), // showers
        15 or 16 or 17 or 41 or 42 => 95,             // thunderstorms
        18 => ByIntensity(intensity, 61, 63, 65),     // rain
        19 or 20 or 21 or 43 => 71,                   // flurries
        22 or 23 or 44 => ByIntensity(intensity, 71, 73, 75), // snow
        24 or 26 => 67,                               // ice, freezing rain
        25 => 79,                                     // sleet
        29 => 68,                                     // rain and snow
        _ => 3,
    };

    /// <summary>
    /// About how much of the period is sunny by a daytime icon ("partly sunny with showers" is half), or null where
    /// the icon doesn't say (fog, hot, windy, night icons).
    /// </summary>
    public static double? SunOf(int iconCode) => iconCode switch
    {
        1 => 1,                                       // sunny
        2 => 0.8,                                     // mostly sunny
        5 => 0.7,                                     // hazy sunshine
        3 or 4 or 14 or 17 or 21 => 0.5,              // partly sunny, intermittent clouds, partly sunny with …
        6 or 13 or 16 or 20 or 23 => 0.25,            // mostly cloudy, mostly cloudy with …
        7 or 8 or 12 or 15 or 18 or 19 or 22 or 24 or 25 or 26 or 29 => 0, // cloudy, dreary, overcast precipitation
        _ => null,
    };

    /// <summary>What an icon shows falling, or null for no precipitation.</summary>
    public static PrecipType? PrecipTypeOf(int iconCode) => iconCode switch
    {
        12 or 13 or 14 or 15 or 16 or 17 or 18 or 39 or 40 or 41 or 42 => PrecipType.Rain,
        19 or 20 or 21 or 22 or 23 or 43 or 44 => PrecipType.Snow,
        24 or 25 or 26 => PrecipType.IceRain,     // ice, sleet (ice pellets), freezing rain
        29 => PrecipType.Sleet,                   // rain and snow
        _ => null,
    };

    static int ByIntensity(string? intensity, int light, int moderate, int heavy) => intensity switch
    {
        "Light" => light,
        "Heavy" => heavy,
        _ => moderate,
    };
}
