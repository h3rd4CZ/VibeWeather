using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace VibeWeather.Services.AzureMaps;

/// <summary>GET and deserialize an Azure Maps endpoint with a subscription key, turning failures into user-facing messages.</summary>
internal static class AzureMapsHttp
{
    public const string Endpoint = "https://atlas.microsoft.com";

    public static async Task<T> GetAsync<T>(HttpClient http, string key, string pathAndQuery, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + pathAndQuery);
        // A header rather than the query string, so the key doesn't end up in logged URLs.
        request.Headers.Add("subscription-key", key);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        // Android's handler reports a cancelled request as WebException("Canceled"); callers expect cancellation.
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
        {
            throw new OperationCanceledException("Požadavek byl zrušen.", ex, cancellationToken);
        }
        // On Android a failed connection comes as WebException rather than HttpRequestException.
        catch (Exception ex) when (ex is HttpRequestException or System.Net.WebException)
        {
            throw new WeatherProviderException("Nepodařilo se spojit se službou Azure Maps. Zkontrolujte připojení k internetu.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherProviderException("Služba Azure Maps neodpověděla včas.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string? message = null;
                try { message = (await response.Content.ReadFromJsonAsync(AzureMapsJsonContext.Default.ErrorResponse, cancellationToken))?.Error?.Message; }
                catch (JsonException) { }

                var status = response.StatusCode;
                // The service's own message is English: it goes to the error log (as the inner exception), the user
                // gets a Czech message.
                var detail = message is null ? null : new HttpRequestException(message, null, status);
                if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    throw new WeatherProviderException(
                        status == HttpStatusCode.TooManyRequests
                            ? "Příliš mnoho požadavků na Azure Maps s tímto klíčem předplatného. Zkuste to prosím za chvíli."
                            : "Služba Azure Maps je přetížená. Zkuste to prosím za chvíli.",
                        detail,
                        isThrottled: true,
                        retryAfter: RetryAfter(response));
                }
                if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new AzureMapsKeyException();
                throw new WeatherProviderException($"Služba Azure Maps vrátila chybu ({(int)status}).", detail);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
                    ?? throw new WeatherProviderException("Služba Azure Maps vrátila prázdnou odpověď.");
            }
            catch (JsonException ex)
            {
                throw new WeatherProviderException("Služba Azure Maps vrátila data, která aplikace neumí přečíst.", ex);
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

/// <summary>Azure Maps refused the subscription key (401/403), or there is none.</summary>
public sealed class AzureMapsKeyException(string message = "Služba Azure Maps odmítla klíč předplatného. Zkontrolujte ho v Nastavení → Zdroj počasí.")
    : WeatherProviderException(message);
