using System.Text.Json.Serialization;
using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>Source-generated serializers for data the app keeps in Preferences (trim-safe in Release builds).</summary>
[JsonSerializable(typeof(WeatherLocation))]
[JsonSerializable(typeof(List<WeatherLocation>))]
[JsonSerializable(typeof(List<GeofencePlace>))]
[JsonSerializable(typeof(List<SevereWeather.SevereWarning>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
