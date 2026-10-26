using VibeWeather.Data;

namespace VibeWeather.Services;

public interface IWeatherProvider
{
    /// <summary>Full forecast: current conditions, the next 60 (or 120) minutes, next 72 hours and 16 days (15 from Azure Maps).</summary>
    Task<Forecast> GetForecastAsync(WeatherLocation location, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="GetForecastAsync"/>, but never a cached copy: for checks that must use data from right now.</summary>
    Task<Forecast> GetFreshForecastAsync(WeatherLocation location, CancellationToken cancellationToken = default) =>
        GetForecastAsync(location, cancellationToken);

    /// <summary>Current temperature and weather for several locations, in the same order.</summary>
    Task<IReadOnlyList<LocationSummary>> GetSummariesAsync(IReadOnlyList<WeatherLocation> locations, CancellationToken cancellationToken = default);
}

/// <summary>A weather request failed; Message is suitable for showing to the user.</summary>
/// <param name="isThrottled">The service refused because of request limits or load, not because of a fault.</param>
/// <param name="retryAfter">How long the service asked to wait, when it said.</param>
public class WeatherProviderException(string message, Exception? inner = null, bool isThrottled = false, TimeSpan? retryAfter = null)
    : Exception(message, inner)
{
    public bool IsThrottled { get; } = isThrottled;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
