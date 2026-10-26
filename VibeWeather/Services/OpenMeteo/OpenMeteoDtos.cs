using System.Text.Json.Serialization;

namespace VibeWeather.Services.OpenMeteo;

// Response shape of https://api.open-meteo.com/v1/forecast. Times are local ISO 8601 ("2026-10-11T18:45")
// because requests use timezone=auto. Series values can be null where a model has no data.

internal sealed class ForecastResponse
{
    [JsonPropertyName("utc_offset_seconds")] public int UtcOffsetSeconds { get; set; }
    [JsonPropertyName("timezone")] public string? Timezone { get; set; }
    [JsonPropertyName("current")] public CurrentBlock? Current { get; set; }
    [JsonPropertyName("minutely_15")] public Minutely15Block? Minutely15 { get; set; }
    [JsonPropertyName("hourly")] public HourlyBlock? Hourly { get; set; }
    [JsonPropertyName("daily")] public DailyBlock? Daily { get; set; }
}

internal sealed class CurrentBlock
{
    [JsonPropertyName("time")] public string Time { get; set; } = "";
    [JsonPropertyName("temperature_2m")] public double? Temperature { get; set; }
    [JsonPropertyName("apparent_temperature")] public double? ApparentTemperature { get; set; }
    [JsonPropertyName("relative_humidity_2m")] public double? Humidity { get; set; }
    [JsonPropertyName("dew_point_2m")] public double? DewPoint { get; set; }
    [JsonPropertyName("is_day")] public int? IsDay { get; set; }
    [JsonPropertyName("weather_code")] public int? WeatherCode { get; set; }
    [JsonPropertyName("pressure_msl")] public double? Pressure { get; set; }
    [JsonPropertyName("wind_speed_10m")] public double? WindSpeed { get; set; }
    [JsonPropertyName("wind_direction_10m")] public double? WindDirection { get; set; }
    [JsonPropertyName("wind_gusts_10m")] public double? WindGusts { get; set; }
    [JsonPropertyName("visibility")] public double? Visibility { get; set; }
    [JsonPropertyName("uv_index")] public double? UvIndex { get; set; }
}

/// <summary>Each value is the sum over the 15 minutes ending at its time.</summary>
internal sealed class Minutely15Block
{
    [JsonPropertyName("time")] public string[] Time { get; set; } = [];
    [JsonPropertyName("precipitation")] public double?[] Precipitation { get; set; } = [];
    [JsonPropertyName("rain")] public double?[] Rain { get; set; } = [];
    [JsonPropertyName("snowfall")] public double?[] Snowfall { get; set; } = [];
    [JsonPropertyName("weather_code")] public int?[] WeatherCode { get; set; } = [];
}

internal sealed class HourlyBlock
{
    [JsonPropertyName("time")] public string[] Time { get; set; } = [];
    [JsonPropertyName("temperature_2m")] public double?[] Temperature { get; set; } = [];
    [JsonPropertyName("apparent_temperature")] public double?[] ApparentTemperature { get; set; } = [];
    [JsonPropertyName("relative_humidity_2m")] public double?[] Humidity { get; set; } = [];
    [JsonPropertyName("dew_point_2m")] public double?[] DewPoint { get; set; } = [];
    [JsonPropertyName("precipitation_probability")] public double?[] PrecipitationProbability { get; set; } = [];
    [JsonPropertyName("weather_code")] public int?[] WeatherCode { get; set; } = [];
    [JsonPropertyName("wind_speed_10m")] public double?[] WindSpeed { get; set; } = [];
    [JsonPropertyName("wind_direction_10m")] public double?[] WindDirection { get; set; } = [];
    [JsonPropertyName("uv_index")] public double?[] UvIndex { get; set; } = [];
    [JsonPropertyName("is_day")] public int?[] IsDay { get; set; } = [];
    [JsonPropertyName("pressure_msl")] public double?[] Pressure { get; set; } = [];
    /// <summary>Sum over the hour ending at each time, in mm. The app shifts it to the hour starting at each time.</summary>
    [JsonPropertyName("precipitation")] public double?[] Precipitation { get; set; } = [];
    /// <summary>Rain (not snow) over the hour ending at each time, in mm.</summary>
    [JsonPropertyName("rain")] public double?[] Rain { get; set; } = [];
    /// <summary>New snow over the hour ending at each time, in cm.</summary>
    [JsonPropertyName("snowfall")] public double?[] Snowfall { get; set; } = [];
    /// <summary>Seconds of sunshine in the hour ending at each time.</summary>
    [JsonPropertyName("sunshine_duration")] public double?[] SunshineDuration { get; set; } = [];
}

internal sealed class DailyBlock
{
    [JsonPropertyName("time")] public string[] Time { get; set; } = [];
    [JsonPropertyName("weather_code")] public int?[] WeatherCode { get; set; } = [];
    [JsonPropertyName("temperature_2m_max")] public double?[] TemperatureMax { get; set; } = [];
    [JsonPropertyName("temperature_2m_min")] public double?[] TemperatureMin { get; set; } = [];
    [JsonPropertyName("sunrise")] public string[] Sunrise { get; set; } = [];
    [JsonPropertyName("sunset")] public string[] Sunset { get; set; } = [];
    /// <summary>Seconds of sunshine and of daylight in the day.</summary>
    [JsonPropertyName("sunshine_duration")] public double?[] SunshineDuration { get; set; } = [];
    [JsonPropertyName("daylight_duration")] public double?[] DaylightDuration { get; set; } = [];
    [JsonPropertyName("uv_index_max")] public double?[] UvIndexMax { get; set; } = [];
    [JsonPropertyName("precipitation_sum")] public double?[] PrecipitationSum { get; set; } = [];
    [JsonPropertyName("rain_sum")] public double?[] RainSum { get; set; } = [];
    [JsonPropertyName("showers_sum")] public double?[] ShowersSum { get; set; } = [];
    /// <summary>New snow in cm.</summary>
    [JsonPropertyName("snowfall_sum")] public double?[] SnowfallSum { get; set; } = [];
    [JsonPropertyName("precipitation_probability_max")] public double?[] PrecipitationProbabilityMax { get; set; } = [];
    [JsonPropertyName("wind_speed_10m_max")] public double?[] WindSpeedMax { get; set; } = [];
    [JsonPropertyName("wind_direction_10m_dominant")] public double?[] WindDirectionDominant { get; set; } = [];
}

internal sealed class ErrorResponse
{
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

// Response shape of https://geocoding-api.open-meteo.com/v1/search. "results" is omitted when nothing matches,
// and empty fields such as admin1 are left out of each result.

internal sealed class GeocodingResponse
{
    [JsonPropertyName("results")] public List<GeocodingResult>? Results { get; set; }
}

internal sealed class GeocodingResult
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("latitude")] public double Latitude { get; set; }
    [JsonPropertyName("longitude")] public double Longitude { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
    [JsonPropertyName("admin1")] public string? Admin1 { get; set; }
}

// Source-generated serializers keep the DTOs working when Release builds trim unused code.
[JsonSerializable(typeof(ForecastResponse))]
[JsonSerializable(typeof(List<ForecastResponse>))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(GeocodingResponse))]
internal sealed partial class OpenMeteoJsonContext : JsonSerializerContext;
