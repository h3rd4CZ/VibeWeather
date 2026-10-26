using VibeWeather.Data;

namespace VibeWeather.Services.OpenMeteo;

/// <summary>
/// City search with Open-Meteo's geocoding API. Two characters must match a name exactly; three or more match
/// as a prefix, ignoring case and diacritics.
/// Gentle on the service, which answers "overloaded" (503) when hit too often: recent answers are cached, requests
/// are spaced out, and an overloaded answer is retried once after a short wait.
/// </summary>
public sealed class OpenMeteoLocationSearch(HttpClient http) : ILocationSearch
{
    const string Endpoint = "https://geocoding-api.open-meteo.com/v1/search";
    const int MaxResults = 10;
    const int MaxCached = 50;
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(1500);
    static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(3);

    readonly Dictionary<string, (DateTime FetchedUtc, List<WeatherLocation> Results)> cache = new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim gate = new(1, 1);
    DateTime lastRequestUtc;

    public async Task<IReadOnlyList<WeatherLocation>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length < 2) return [];

        lock (cache)
        {
            if (cache.TryGetValue(query, out var hit) && DateTime.UtcNow - hit.FetchedUtc < CacheFor) return hit.Results;
        }

        List<WeatherLocation> results;
        try
        {
            results = await FetchAsync(query, cancellationToken);
        }
        catch (WeatherProviderException ex) when (ex.IsThrottled)
        {
            // Usually a momentary overload: wait a little (as asked, within reason) and try once more.
            var wait = ex.RetryAfter is { } asked && asked < MaxRetryWait ? asked : RetryWait;
            await Task.Delay(wait, cancellationToken);
            results = await FetchAsync(query, cancellationToken);
        }

        lock (cache)
        {
            if (cache.Count >= MaxCached) cache.Remove(cache.MinBy(e => e.Value.FetchedUtc).Key);
            cache[query] = (DateTime.UtcNow, results);
        }
        return results;
    }

    /// <summary>One request, at least <see cref="MinSpacing"/> after the previous one.</summary>
    async Task<List<WeatherLocation>> FetchAsync(string query, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var wait = lastRequestUtc + MinSpacing - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            gate.Release();
        }

        var url = $"{Endpoint}?name={Uri.EscapeDataString(query)}&count={MaxResults}&language=cs&format=json";
        var response = await OpenMeteoHttp.GetAsync(http, url, OpenMeteoJsonContext.Default.GeocodingResponse, cancellationToken);

        return (response.Results ?? [])
            .Select(r => new WeatherLocation(r.Name, Region(r), r.Latitude, r.Longitude))
            .ToList();
    }

    // Names come in Czech (language=cs): "Jihočeský kraj, Česko". The region is left out when it repeats the place
    // name (Praha, Praha; Vídeň, Vídeň).
    static string Region(GeocodingResult r) =>
        string.Join(", ", new[] { r.Admin1 != r.Name ? r.Admin1 : null, r.Country }.Where(s => !string.IsNullOrEmpty(s)));
}
