namespace VibeWeather.Data;

/// <summary>
/// WMO weather interpretation codes, as returned by Open-Meteo's weather_code. Other sources are converted
/// to these (see AzureMapsIcons); 68 (rain and snow) and 79 (ice pellets) only come from such conversions.
/// </summary>
public static class WeatherCodes
{
    // Shares of sunshine (of an hour, or of a day's daylight) from which the sky counts as sunny or as having sunny spells.
    const double MostlySunny = 0.75;
    const double SunnySpells = 0.2;

    /// <summary>
    /// The icon for a code. A code stands for the period's most significant weather, so an hour or day that is
    /// "overcast" or "rain" can still be largely sunny; given the share of sunshine (<paramref name="sun"/>, 0–1),
    /// such periods get a sunnier icon.
    /// </summary>
    public static WeatherCondition Condition(int code, bool isDay, double? sun = null)
    {
        var condition = code switch
        {
            0 or 1 => isDay ? WeatherCondition.Clear : WeatherCondition.Moon,
            2 => isDay ? WeatherCondition.Partly : WeatherCondition.PartlyNight,
            3 => WeatherCondition.Cloudy,
            45 or 48 => WeatherCondition.Fog,
            // Freezing drizzle or rain, rain and snow, ice pellets.
            56 or 57 or 66 or 67 or 68 or 79 => WeatherCondition.Sleet,
            >= 51 and <= 65 or >= 80 and <= 82 => WeatherCondition.Rain,
            >= 71 and <= 77 or 85 or 86 => WeatherCondition.Snow,
            >= 95 => WeatherCondition.Storm,
            _ => WeatherCondition.Cloudy,
        };
        if (!isDay || sun is not { } share) return condition;
        return condition switch
        {
            WeatherCondition.Partly or WeatherCondition.Cloudy or WeatherCondition.Fog when share >= MostlySunny => WeatherCondition.Clear,
            WeatherCondition.Cloudy or WeatherCondition.Fog when share >= SunnySpells => WeatherCondition.Partly,
            WeatherCondition.Rain when share >= SunnySpells => WeatherCondition.SunRain,
            _ => condition,
        };
    }

    /// <summary>Like <see cref="Describe(int)"/>, but matching <see cref="Condition"/> when sunshine makes it sunnier.</summary>
    public static string Describe(int code, bool isDay, double? sun)
    {
        var condition = Condition(code, isDay, sun);
        if (condition == Condition(code, isDay)) return Describe(code);
        return condition switch
        {
            WeatherCondition.Clear => "Převážně slunečno",
            WeatherCondition.Partly => "Polojasno",
            WeatherCondition.SunRain => $"{Describe(code)}, občas slunečno",
            _ => Describe(code),
        };
    }

    public static string Describe(int code) => code switch
    {
        0 => "Jasno",
        1 => "Skoro jasno",
        2 => "Polojasno",
        3 => "Zataženo",
        45 => "Mlha",
        48 => "Mlha s námrazou",
        51 => "Slabé mrholení",
        53 => "Mrholení",
        55 => "Silné mrholení",
        56 => "Slabé mrznoucí mrholení",
        57 => "Mrznoucí mrholení",
        61 => "Slabý déšť",
        63 => "Déšť",
        65 => "Silný déšť",
        66 => "Slabý mrznoucí déšť",
        67 => "Mrznoucí déšť",
        68 => "Déšť se sněhem",
        71 => "Slabé sněžení",
        73 => "Sněžení",
        75 => "Silné sněžení",
        77 => "Sněhová zrna",
        79 => "Zmrzlý déšť",
        80 => "Slabé přeháňky",
        81 => "Přeháňky",
        82 => "Silné přeháňky",
        85 => "Sněhové přeháňky",
        86 => "Silné sněhové přeháňky",
        95 => "Bouřka",
        96 or 99 => "Bouřka s kroupami",
        97 => "Silná bouřka",
        _ => "Neznámé",
    };

    public static WeatherScenario Scenario(int code) => code switch
    {
        >= 95 => WeatherScenario.Storm,
        >= 45 or 3 => WeatherScenario.Rain,
        _ => WeatherScenario.Clear,
    };

    public static bool IsFreezing(int code) => code is 56 or 57 or 66 or 67;

    public static bool IsSnow(int code) => code is >= 71 and <= 77 or 85 or 86;
}
