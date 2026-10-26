using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace VibeWeather.Services.OpenMeteo;

/// <summary>GET and deserialize an Open-Meteo endpoint, turning failures into user-facing messages.</summary>
internal static class OpenMeteoHttp
{
    public static async Task<T> GetAsync<T>(HttpClient http, string url, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, cancellationToken);
        }
        // Android's handler reports a cancelled request as WebException("Canceled"); callers expect cancellation.
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
        {
            throw new OperationCanceledException("Požadavek byl zrušen.", ex, cancellationToken);
        }
        // On Android a failed connection comes as WebException rather than HttpRequestException.
        catch (Exception ex) when (ex is HttpRequestException or WebException)
        {
            throw new WeatherProviderException("Nepodařilo se spojit se službou počasí. Zkontrolujte připojení k internetu.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherProviderException("Služba počasí neodpověděla včas.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string? reason = null;
                try { reason = (await response.Content.ReadFromJsonAsync(OpenMeteoJsonContext.Default.ErrorResponse, cancellationToken))?.Reason; }
                catch (JsonException) { }

                // The service's own reason is English: it goes to the error log (as the inner exception), the user
                // gets a Czech message.
                var status = response.StatusCode;
                var detail = reason is null ? null : new HttpRequestException(reason, null, status);
                // 429: the free tier's minutely/hourly/daily request limit. 503: the service is overloaded.
                if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    throw new WeatherProviderException(
                        status == HttpStatusCode.TooManyRequests
                            ? "Příliš mnoho požadavků na službu počasí. Zkuste to prosím za chvíli."
                            : "Služba počasí je přetížená. Zkuste to prosím za chvíli.",
                        detail,
                        isThrottled: true,
                        retryAfter: RetryAfter(response));
                }
                throw new WeatherProviderException($"Služba počasí vrátila chybu ({(int)status}).", detail);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
                    ?? throw new WeatherProviderException("Služba počasí vrátila prázdnou odpověď.");
            }
            catch (JsonException ex)
            {
                throw new WeatherProviderException("Služba počasí vrátila data, která aplikace neumí přečíst.", ex);
            }
        }
    }

    static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } when date > DateTimeOffset.UtcNow => date - DateTimeOffset.UtcNow,
            _ => null,
        };
}
