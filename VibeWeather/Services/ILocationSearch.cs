using VibeWeather.Data;

namespace VibeWeather.Services;

public interface ILocationSearch
{
    /// <summary>Places matching a name or postal code, best match first. Empty for queries under two characters.</summary>
    Task<IReadOnlyList<WeatherLocation>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
