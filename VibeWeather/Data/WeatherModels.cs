using System.Globalization;
using System.Text.Json.Serialization;

namespace VibeWeather.Data;

/// <summary>
/// Icon to show for a weather state. Descriptions come from <see cref="WeatherCodes"/>. PartlyNight is a moon behind
/// a cloud, SunRain rain with sunny spells, Sleet rain and snow (or freezing rain).
/// </summary>
public enum WeatherCondition { Clear, Partly, Cloudy, Fog, Rain, Snow, Storm, Moon, PartlyNight, SunRain, Sleet }

/// <summary>Drives the accent colour: amber for clear, blue for wet or overcast, violet for storms.</summary>
public enum WeatherScenario { Clear, Rain, Storm }

/// <summary>IceRain is freezing rain or drizzle; Sleet is a snow and rain mix.</summary>
public enum PrecipType { Rain, IceRain, Sleet, Snow }

public sealed record WeatherLocation(string Name, string Region, double Latitude, double Longitude, bool IsDevice = false)
{
    /// <summary>Stable identifier, used to remember the selected location.</summary>
    [JsonIgnore]
    public string Key => IsDevice ? DeviceKey : string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.00},{Longitude:0.00}");

    public const string DeviceKey = "device";
}

/// <summary>
/// All times are local to the forecast location. Codes are WMO weather codes (other sources are converted).
/// Temperatures are °C, wind km/h, pressure hPa, precipitation mm.
/// PressureTendency is +1 rising, 0 steady, -1 falling when the source reports it; otherwise the hourly pressures decide.
/// </summary>
public sealed record CurrentConditions(
    DateTime Time, double Temp, double FeelsLike, int Code, bool IsDay,
    int Humidity, double DewPoint, double WindSpeed, double WindGusts, int WindDirection,
    double Pressure, double VisibilityKm, double Uv, int? PressureTendency = null);

/// <summary>
/// Precipitation is the water equivalent (mm) and Snow the new snow (cm) of the hour starting at Time; Type is
/// what falls then (only meaningful when Precipitation is above 0). Sun is the share of the hour with sunshine
/// (0–1), when the source tells. WindDirection is where the wind comes from, in degrees (0 = north).
/// </summary>
public sealed record HourForecast(
    DateTime Time, double Temp, double FeelsLike, int Code, bool IsDay, int Pop,
    double Wind, int Humidity, double DewPoint, double Uv, double Pressure, double Precipitation, double Snow = 0,
    PrecipType Type = PrecipType.Rain, double? Sun = null, int WindDirection = 0);

/// <summary>
/// Intensity is the water equivalent in mm/h, whatever falls. SnowCmPerHour is how fast new snow builds up
/// (cm/h); it is 0 unless snow is part of it. Dbz is the radar reflectivity when the source measures it (Azure Maps
/// nowcast), null when the minute is a model forecast.
/// </summary>
public sealed record MinutePrecip(double Intensity, PrecipType Type, double SnowCmPerHour = 0, double? Dbz = null);

/// <summary>
/// PrecipMm is the day's total as water (mm), SnowCm its new snow (cm), and Kinds what falls during the day with
/// how much (rain, ice rain, snow), empty when dry. Sun is the share of daylight with sunshine (0–1), when the source tells.
/// </summary>
public sealed record DayForecast(
    DateOnly Date, int Code, double High, double Low, int Pop, double Wind, int WindDirection,
    double Uv, double PrecipMm, DateTime Sunrise, DateTime Sunset, double SnowCm = 0, IReadOnlyList<DayPrecip>? Kinds = null,
    double? Sun = null)
{
    /// <summary>Below this (mm of water, cm of snow) a kind of precipitation doesn't count for the day.</summary>
    public const double MinAmount = 0.1;
}

/// <summary>One kind of precipitation in a day: Amount is mm of water for rain and ice rain, cm of new snow for snow.</summary>
public sealed record DayPrecip(PrecipType Type, double Amount);

/// <summary>
/// LocalNow is the location's local time when the forecast was fetched. Minutes holds precipitation one entry
/// per minute from LocalNow: 60 entries, or up to 120 where the source forecasts two hours (Azure Maps).
/// </summary>
public sealed record Forecast(
    WeatherLocation Location, DateTime LocalNow, CurrentConditions Current,
    IReadOnlyList<HourForecast> Hours, IReadOnlyList<MinutePrecip> Minutes, IReadOnlyList<DayForecast> Days);

/// <summary>
/// A circular area around a place; the app tracks whether the device is inside it. LastEntered is when the device
/// last arrived in it, null until it has.
/// </summary>
public sealed record GeofencePlace(string Id, WeatherLocation Place, int RadiusMeters, DateTimeOffset? LastEntered = null);

/// <summary>Current temperature and weather for a location list entry.</summary>
public sealed record LocationSummary(double Temp, int Code, bool IsDay);
