using System.Globalization;
using System.Text.RegularExpressions;
using VibeWeather.Data;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather.Services.AzureMaps;

/// <summary>
/// Official severe weather warnings for one place from Azure Maps (one billable request), with the key saved in
/// Settings, also when Open-Meteo is the forecast source.
/// </summary>
public sealed class AzureMapsSevereAlerts(HttpClient http, WeatherSourceSettings settings)
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public Task<bool> HasKeyAsync() => settings.HasAzureKeyAsync();

    /// <summary>
    /// Warnings in effect or announced at the place, names in Czech where the service translates them.
    /// Throws <see cref="AzureMapsKeyException"/> without a key or with a rejected one, otherwise
    /// <see cref="WeatherProviderException"/> or <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<IReadOnlyList<SevereWarning>> GetAsync(WeatherLocation place, CancellationToken cancellationToken)
    {
        var key = await settings.GetAzureKeyAsync();
        if (string.IsNullOrEmpty(key))
            throw new AzureMapsKeyException("Výstrahy potřebují klíč Azure Maps. Zadejte ho v Nastavení → Zdroj počasí.");

        var query = $"query={place.Latitude.ToString("0.####", Inv)},{place.Longitude.ToString("0.####", Inv)}";
        var response = await AzureMapsHttp.GetAsync(http, key,
            $"/weather/severe/alerts/json?api-version=1.1&{query}&language=cs-CZ&details=true",
            AzureMapsJsonContext.Default.SevereAlertsResponse, cancellationToken);
        return response.Results.Select(Map).OfType<SevereWarning>().DistinctBy(w => w.Id).ToList();
    }

    /// <summary>
    /// Some agencies (e.g. the US National Weather Service) wrap their text at a fixed width. Single line breaks join
    /// into spaces so the text flows on a phone; blank lines between paragraphs stay.
    /// </summary>
    static string Unwrap(string text) =>
        Regex.Replace(text.Replace("\r\n", "\n").Trim(), @"(?<!\n)\n(?!\n)", " ");

    // The first area is the one at the requested point; the full text falls back to the short summary.
    static SevereWarning? Map(SevereAlert alert)
    {
        var area = alert.AlertAreas.FirstOrDefault();
        if (area is null) return null;
        return new SevereWarning(
            $"{alert.SourceId}-{alert.AlertId}",
            alert.Description?.Localized ?? alert.Description?.English ?? "Výstraha",
            area.LatestStatus?.English ?? "New",
            area.Name ?? "",
            area.StartTime,
            area.EndTime,
            alert.Source ?? "",
            Unwrap(area.AlertDetails ?? area.Summary ?? ""),
            alert.Disclaimer,
            alert.Priority);
    }
}
