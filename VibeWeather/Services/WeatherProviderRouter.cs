using System.Globalization;
using VibeWeather.Data;
using VibeWeather.Services.AzureMaps;
using VibeWeather.Services.OpenMeteo;

namespace VibeWeather.Services;

/// <summary>
/// The app's <see cref="IWeatherProvider"/>: forwards to the source chosen in Settings, and briefly caches
/// forecasts so the screens, precipitation checks and the morning summary don't fetch the same place twice
/// (Azure Maps bills every request; one forecast is four).
/// </summary>
public sealed class WeatherProviderRouter : IWeatherProvider
{
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(3);

    readonly OpenMeteoWeatherProvider openMeteo;
    readonly AzureMapsWeatherProvider azureMaps;
    readonly WeatherSourceSettings settings;
    readonly Lock gate = new();
    readonly Dictionary<(WeatherSource, string), (DateTime FetchedUtc, Forecast Forecast)> cache = [];

    public WeatherProviderRouter(OpenMeteoWeatherProvider openMeteo, AzureMapsWeatherProvider azureMaps, WeatherSourceSettings settings)
    {
        this.openMeteo = openMeteo;
        this.azureMaps = azureMaps;
        this.settings = settings;
        // A new key may see different data (or none); don't serve forecasts fetched with the old one.
        settings.Changed += () => { lock (gate) cache.Clear(); };
    }

    IWeatherProvider Active => settings.Source == WeatherSource.AzureMaps ? azureMaps : openMeteo;

    public async Task<Forecast> GetForecastAsync(WeatherLocation location, CancellationToken cancellationToken = default)
    {
        var key = CacheKey(location);
        lock (gate)
        {
            if (cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.FetchedUtc < CacheFor)
                return hit.Forecast with { Location = location };
        }

        var forecast = await Active.GetForecastAsync(location, cancellationToken);
        lock (gate) cache[key] = (DateTime.UtcNow, forecast);
        return forecast;
    }

    public async Task<Forecast> GetFreshForecastAsync(WeatherLocation location, CancellationToken cancellationToken = default)
    {
        var forecast = await Active.GetForecastAsync(location, cancellationToken);
        lock (gate) cache[CacheKey(location)] = (DateTime.UtcNow, forecast);
        return forecast;
    }

    (WeatherSource, string) CacheKey(WeatherLocation location) =>
        // By position, not Key: the device location keeps its key as the device moves.
        (settings.Source, string.Create(CultureInfo.InvariantCulture, $"{location.Latitude:0.000},{location.Longitude:0.000}"));

    public Task<IReadOnlyList<LocationSummary>> GetSummariesAsync(IReadOnlyList<WeatherLocation> locations, CancellationToken cancellationToken = default) =>
        Active.GetSummariesAsync(locations, cancellationToken);
}
