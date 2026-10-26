using System.Text.Json.Serialization;

namespace VibeWeather.Services.AzureMaps;

// Response shapes of the Azure Maps Weather API 1.1 (https://learn.microsoft.com/rest/api/maps/weather).
// Requests use unit=metric: °C, km/h, km, mb (= hPa), mm (snow in cm). Times are ISO 8601 with the
// location's UTC offset ("2026-10-12T08:00:00+02:00"). Only the fields the app uses are declared.

internal sealed class UnitValue
{
    [JsonPropertyName("value")] public double? Value { get; set; }
}

internal sealed class WindDirection
{
    [JsonPropertyName("degrees")] public double? Degrees { get; set; }
}

internal sealed class Wind
{
    [JsonPropertyName("direction")] public WindDirection? Direction { get; set; }
    [JsonPropertyName("speed")] public UnitValue? Speed { get; set; }
}

internal sealed class PressureTendency
{
    /// <summary>"R" rising, "F" falling, "S" steady.</summary>
    [JsonPropertyName("code")] public string? Code { get; set; }
}

internal sealed class CurrentConditionsResponse
{
    [JsonPropertyName("results")] public CurrentResult[] Results { get; set; } = [];
}

internal sealed class CurrentResult
{
    [JsonPropertyName("dateTime")] public DateTimeOffset DateTime { get; set; }
    [JsonPropertyName("phrase")] public string? Phrase { get; set; }
    [JsonPropertyName("iconCode")] public int IconCode { get; set; }
    [JsonPropertyName("hasPrecipitation")] public bool HasPrecipitation { get; set; }
    [JsonPropertyName("isDayTime")] public bool IsDayTime { get; set; }
    [JsonPropertyName("temperature")] public UnitValue? Temperature { get; set; }
    [JsonPropertyName("realFeelTemperature")] public UnitValue? RealFeelTemperature { get; set; }
    [JsonPropertyName("relativeHumidity")] public double? RelativeHumidity { get; set; }
    [JsonPropertyName("dewPoint")] public UnitValue? DewPoint { get; set; }
    [JsonPropertyName("wind")] public Wind? Wind { get; set; }
    [JsonPropertyName("windGust")] public Wind? WindGust { get; set; }
    [JsonPropertyName("uvIndex")] public double? UvIndex { get; set; }
    [JsonPropertyName("visibility")] public UnitValue? Visibility { get; set; }
    [JsonPropertyName("pressure")] public UnitValue? Pressure { get; set; }
    [JsonPropertyName("pressureTendency")] public PressureTendency? PressureTendency { get; set; }
}

internal sealed class HourlyForecastResponse
{
    [JsonPropertyName("forecasts")] public HourlyForecast[] Forecasts { get; set; } = [];
}

internal sealed class HourlyForecast
{
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    [JsonPropertyName("iconCode")] public int IconCode { get; set; }
    [JsonPropertyName("hasPrecipitation")] public bool HasPrecipitation { get; set; }
    [JsonPropertyName("precipitationType")] public string? PrecipitationType { get; set; }
    [JsonPropertyName("precipitationIntensity")] public string? PrecipitationIntensity { get; set; }
    [JsonPropertyName("isDaylight")] public bool IsDaylight { get; set; }
    [JsonPropertyName("temperature")] public UnitValue? Temperature { get; set; }
    [JsonPropertyName("realFeelTemperature")] public UnitValue? RealFeelTemperature { get; set; }
    [JsonPropertyName("dewPoint")] public UnitValue? DewPoint { get; set; }
    [JsonPropertyName("wind")] public Wind? Wind { get; set; }
    [JsonPropertyName("relativeHumidity")] public double? RelativeHumidity { get; set; }
    [JsonPropertyName("uvIndex")] public double? UvIndex { get; set; }
    [JsonPropertyName("precipitationProbability")] public double? PrecipitationProbability { get; set; }
    [JsonPropertyName("totalLiquid")] public UnitValue? TotalLiquid { get; set; }
    /// <summary>Snow depth in cm (metric).</summary>
    [JsonPropertyName("snow")] public UnitValue? Snow { get; set; }
}

internal sealed class DailyForecastResponse
{
    [JsonPropertyName("forecasts")] public DailyForecast[] Forecasts { get; set; } = [];
}

internal sealed class DailyForecast
{
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    [JsonPropertyName("temperature")] public TemperatureRange? Temperature { get; set; }
    [JsonPropertyName("airAndPollen")] public AirAndPollen[] AirAndPollen { get; set; } = [];
    [JsonPropertyName("day")] public DayPart? Day { get; set; }
    [JsonPropertyName("night")] public DayPart? Night { get; set; }
}

internal sealed class TemperatureRange
{
    [JsonPropertyName("minimum")] public UnitValue? Minimum { get; set; }
    [JsonPropertyName("maximum")] public UnitValue? Maximum { get; set; }
}

internal sealed class AirAndPollen
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("value")] public double? Value { get; set; }
}

internal sealed class DayPart
{
    [JsonPropertyName("iconCode")] public int IconCode { get; set; }
    [JsonPropertyName("precipitationIntensity")] public string? PrecipitationIntensity { get; set; }
    [JsonPropertyName("precipitationProbability")] public double? PrecipitationProbability { get; set; }
    [JsonPropertyName("wind")] public Wind? Wind { get; set; }
    [JsonPropertyName("totalLiquid")] public UnitValue? TotalLiquid { get; set; }
    [JsonPropertyName("rain")] public UnitValue? Rain { get; set; }
    /// <summary>New snow in cm (metric).</summary>
    [JsonPropertyName("snow")] public UnitValue? Snow { get; set; }
    [JsonPropertyName("ice")] public UnitValue? Ice { get; set; }
}

internal sealed class MinuteForecastResponse
{
    [JsonPropertyName("intervals")] public MinuteInterval[] Intervals { get; set; } = [];
}

internal sealed class MinuteInterval
{
    [JsonPropertyName("startTime")] public DateTimeOffset StartTime { get; set; }
    /// <summary>Radar reflectivity; 0 means no precipitation.</summary>
    [JsonPropertyName("dbz")] public double Dbz { get; set; }
    /// <summary>"Rain", "Snow", "Ice" or "Mix"; present only when dbz &gt; 0.</summary>
    [JsonPropertyName("precipitationType")] public string? PrecipitationType { get; set; }
}

internal sealed class ErrorResponse
{
    [JsonPropertyName("error")] public ErrorDetail? Error { get; set; }
}

internal sealed class ErrorDetail
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

// Get Severe Weather Alerts (/weather/severe/alerts/json). Phrases come in the requested language (localized) and in
// English; alertDetails is in whatever language the issuing agency writes (Czech for ČHMÚ).
internal sealed class SevereAlertsResponse
{
    [JsonPropertyName("results")] public SevereAlert[] Results { get; set; } = [];
}

internal sealed class SevereAlert
{
    [JsonPropertyName("alertId")] public int AlertId { get; set; }
    [JsonPropertyName("sourceId")] public int SourceId { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    /// <summary>1 is the most important; the scale is the provider's.</summary>
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("description")] public LocalizedPhrase? Description { get; set; }
    [JsonPropertyName("disclaimer")] public string? Disclaimer { get; set; }
    [JsonPropertyName("alertAreas")] public SevereAlertArea[] AlertAreas { get; set; } = [];
}

internal sealed class LocalizedPhrase
{
    [JsonPropertyName("english")] public string? English { get; set; }
    [JsonPropertyName("localized")] public string? Localized { get; set; }
}

internal sealed class SevereAlertArea
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("startTime")] public DateTimeOffset StartTime { get; set; }
    [JsonPropertyName("endTime")] public DateTimeOffset EndTime { get; set; }
    [JsonPropertyName("latestStatus")] public LocalizedPhrase? LatestStatus { get; set; }
    [JsonPropertyName("alertDetails")] public string? AlertDetails { get; set; }
}

[JsonSerializable(typeof(CurrentConditionsResponse))]
[JsonSerializable(typeof(HourlyForecastResponse))]
[JsonSerializable(typeof(DailyForecastResponse))]
[JsonSerializable(typeof(MinuteForecastResponse))]
[JsonSerializable(typeof(SevereAlertsResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class AzureMapsJsonContext : JsonSerializerContext;
