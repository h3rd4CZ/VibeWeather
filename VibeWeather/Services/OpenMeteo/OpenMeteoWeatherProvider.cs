using System.Globalization;
using VibeWeather.Data;

namespace VibeWeather.Services.OpenMeteo;

/// <summary>
/// Weather from Open-Meteo (https://open-meteo.com), free for non-commercial use without an API key.
/// Units are the API defaults: °C, km/h, mm, and pressure in hPa.
/// </summary>
public sealed class OpenMeteoWeatherProvider(HttpClient http) : IWeatherProvider
{
    const string Endpoint = "https://api.open-meteo.com/v1/forecast";

    const string CurrentVariables = "temperature_2m,apparent_temperature,relative_humidity_2m,dew_point_2m,is_day,weather_code,pressure_msl,wind_speed_10m,wind_direction_10m,wind_gusts_10m,visibility,uv_index";
    const string Minutely15Variables = "precipitation,rain,snowfall,weather_code";
    const string HourlyVariables = "temperature_2m,apparent_temperature,relative_humidity_2m,dew_point_2m,precipitation_probability,precipitation,rain,snowfall,weather_code,wind_speed_10m,wind_direction_10m,uv_index,is_day,pressure_msl,sunshine_duration";
    const string DailyVariables = "weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,sunshine_duration,daylight_duration,uv_index_max,precipitation_sum,rain_sum,showers_sum,snowfall_sum,precipitation_probability_max,wind_speed_10m_max,wind_direction_10m_dominant";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The API's maximum, for the Hourly tab: 16 days of hours from now, cut to the daily forecast's days.</summary>
    const int HourCount = 384;

    public async Task<Forecast> GetForecastAsync(WeatherLocation location, CancellationToken cancellationToken = default)
    {
        // One 15-minute step back and seven ahead cover the next hour from any point in the current step.
        var url = $"{Endpoint}?latitude={Coord(location.Latitude)}&longitude={Coord(location.Longitude)}&timezone=auto"
            + $"&current={CurrentVariables}"
            + $"&minutely_15={Minutely15Variables}&past_minutely_15=1&forecast_minutely_15=7"
            + $"&hourly={HourlyVariables}&forecast_hours={HourCount}"
            + $"&daily={DailyVariables}&forecast_days=16";

        var response = await SendAsync(url, OpenMeteoJsonContext.Default.ForecastResponse, cancellationToken);
        if (response.Current is null || response.Hourly is null || response.Daily is null || response.Minutely15 is null)
            throw new WeatherProviderException("Služba počasí vrátila neúplnou předpověď.");

        var localNow = DateTime.SpecifyKind(DateTime.UtcNow.AddSeconds(response.UtcOffsetSeconds), DateTimeKind.Unspecified);
        var days = MapDays(response.Daily);
        return new Forecast(
            location,
            localNow,
            MapCurrent(response.Current),
            MapHours(response.Hourly, days.Count > 0 ? days[^1].Date : DateOnly.MaxValue),
            MapMinutes(response.Minutely15, localNow),
            days);
    }

    public async Task<IReadOnlyList<LocationSummary>> GetSummariesAsync(IReadOnlyList<WeatherLocation> locations, CancellationToken cancellationToken = default)
    {
        if (locations.Count == 0) return [];

        var url = $"{Endpoint}?latitude={string.Join(',', locations.Select(l => Coord(l.Latitude)))}"
            + $"&longitude={string.Join(',', locations.Select(l => Coord(l.Longitude)))}"
            + "&timezone=auto&current=temperature_2m,weather_code,is_day";

        // The API returns a single object for one location and an array for several.
        var responses = locations.Count == 1
            ? [await SendAsync(url, OpenMeteoJsonContext.Default.ForecastResponse, cancellationToken)]
            : await SendAsync(url, OpenMeteoJsonContext.Default.ListForecastResponse, cancellationToken);

        return responses.Select(r => new LocationSummary(
            r.Current?.Temperature ?? double.NaN,
            r.Current?.WeatherCode ?? 0,
            (r.Current?.IsDay ?? 1) == 1)).ToList();
    }

    Task<T> SendAsync<T>(string url, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        OpenMeteoHttp.GetAsync(http, url, typeInfo, cancellationToken);

    static CurrentConditions MapCurrent(CurrentBlock c) => new(
        ParseTime(c.Time),
        c.Temperature ?? 0,
        c.ApparentTemperature ?? c.Temperature ?? 0,
        c.WeatherCode ?? 0,
        (c.IsDay ?? 1) == 1,
        (int)Math.Round(c.Humidity ?? 0),
        c.DewPoint ?? 0,
        c.WindSpeed ?? 0,
        c.WindGusts ?? c.WindSpeed ?? 0,
        (int)Math.Round(c.WindDirection ?? 0),
        c.Pressure ?? 0,
        (c.Visibility ?? 0) / 1000,
        c.UvIndex ?? 0);

    // The API's precipitation and snowfall are totals for the hour ending at each time; the app's are for the
    // hour starting at it (as Azure Maps reports them), so they come from the next entry, and the last entry isn't
    // shown itself. The hours reach about a day past the daily forecast, ending without data, so they stop after
    // its last day.
    static List<HourForecast> MapHours(HourlyBlock h, DateOnly lastDay) =>
        h.Time.Take(h.Time.Length - 1).Select(ParseTime)
            .TakeWhile((time, i) => DateOnly.FromDateTime(time) <= lastDay && h.Temperature.ElementAtOrDefault(i) is not null)
            .Select((time, i) => new HourForecast(
            time,
            At(h.Temperature, i),
            At(h.ApparentTemperature, i),
            At(h.WeatherCode, i),
            At(h.IsDay, i, 1) == 1,
            (int)Math.Round(At(h.PrecipitationProbability, i)),
            At(h.WindSpeed, i),
            (int)Math.Round(At(h.Humidity, i)),
            At(h.DewPoint, i),
            At(h.UvIndex, i),
            At(h.Pressure, i),
            At(h.Precipitation, i + 1),
            At(h.Snowfall, i + 1),
            TypeOf(At(h.Rain, i + 1), At(h.Snowfall, i + 1), At(h.WeatherCode, i + 1)),
            Math.Clamp(At(h.SunshineDuration, i + 1) / 3600, 0, 1),
            (int)Math.Round(At(h.WindDirection, i)))).ToList();

    static List<DayForecast> MapDays(DailyBlock d) =>
        d.Time.Select((date, i) => new DayForecast(
            DateOnly.ParseExact(date, "yyyy-MM-dd", Inv),
            At(d.WeatherCode, i),
            At(d.TemperatureMax, i),
            At(d.TemperatureMin, i),
            (int)Math.Round(At(d.PrecipitationProbabilityMax, i)),
            At(d.WindSpeedMax, i),
            (int)Math.Round(At(d.WindDirectionDominant, i)),
            At(d.UvIndexMax, i),
            At(d.PrecipitationSum, i),
            i < d.Sunrise.Length ? ParseTime(d.Sunrise[i]) : default,
            i < d.Sunset.Length ? ParseTime(d.Sunset[i]) : default,
            At(d.SnowfallSum, i),
            DayKinds(At(d.RainSum, i) + At(d.ShowersSum, i), At(d.SnowfallSum, i), At(d.WeatherCode, i)),
            At(d.DaylightDuration, i) > 0 ? Math.Clamp(At(d.SunshineDuration, i) / At(d.DaylightDuration, i), 0, 1) : null)).ToList();

    // Rain counts as ice rain on a day with freezing rain or drizzle.
    static List<DayPrecip> DayKinds(double rainMm, double snowCm, int code)
    {
        var kinds = new List<DayPrecip>();
        if (rainMm >= DayForecast.MinAmount)
            kinds.Add(new(WeatherCodes.IsFreezing(code) ? PrecipType.IceRain : PrecipType.Rain, rainMm));
        if (snowCm >= DayForecast.MinAmount) kinds.Add(new(PrecipType.Snow, snowCm));
        return kinds;
    }

    /// <summary>
    /// Spreads the 15-minute totals over the next 60 minutes. Each total becomes an intensity (mm/h) at the
    /// middle of its step, and the minutes in between are interpolated linearly, so the chart keeps one bar
    /// per minute. Times derived from it ("starting in 7 min") are estimates.
    /// Snowfall (cm per step) is spread the same way into the rate new snow builds up.
    /// </summary>
    static List<MinutePrecip> MapMinutes(Minutely15Block m, DateTime localNow)
    {
        var steps = m.Time.Select((time, i) => (
            Mid: ParseTime(time).AddMinutes(-7.5),
            Rate: At(m.Precipitation, i) * 4,
            SnowRate: At(m.Snowfall, i) * 4,
            Type: TypeOf(At(m.Rain, i), At(m.Snowfall, i), At(m.WeatherCode, i)))).ToList();
        if (steps.Count == 0)
            return Enumerable.Repeat(new MinutePrecip(0, PrecipType.Rain), 60).ToList();

        var points = steps.Select(s => (s.Mid, s.Rate)).ToList();
        var snowPoints = steps.Select(s => (s.Mid, Rate: s.SnowRate)).ToList();
        var wetSteps = steps.Where(s => s.Rate > 0).ToList();
        var minutes = new List<MinutePrecip>(60);
        for (int k = 0; k < 60; k++)
        {
            var t = localNow.AddMinutes(k + 0.5);
            // Type from the nearest step that has precipitation, so interpolated edges keep its colour.
            var type = wetSteps.Count == 0 ? PrecipType.Rain
                : wetSteps.MinBy(s => Math.Abs((s.Mid - t).TotalMinutes)).Type;
            minutes.Add(new MinutePrecip(Interpolate(points, t), type, Interpolate(snowPoints, t)));
        }
        return minutes;
    }

    static double Interpolate(List<(DateTime Mid, double Rate)> points, DateTime t)
    {
        if (t <= points[0].Mid) return points[0].Rate;
        for (int i = 0; i < points.Count - 1; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            if (t <= b.Mid)
            {
                double f = (t - a.Mid).TotalMinutes / (b.Mid - a.Mid).TotalMinutes;
                return a.Rate + (b.Rate - a.Rate) * f;
            }
        }
        return points[^1].Rate;
    }

    static PrecipType TypeOf(double rain, double snowfall, int code)
    {
        if (WeatherCodes.IsFreezing(code)) return PrecipType.IceRain;
        if (snowfall > 0 && rain > 0) return PrecipType.Sleet;
        if (snowfall > 0 || WeatherCodes.IsSnow(code)) return PrecipType.Snow;
        return PrecipType.Rain;
    }

    static DateTime ParseTime(string value) => DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm", Inv);

    static double At(double?[] values, int i) => i < values.Length ? values[i] ?? 0 : 0;

    static int At(int?[] values, int i, int fallback = 0) => i < values.Length ? values[i] ?? fallback : fallback;

    static string Coord(double value) => value.ToString("0.####", Inv);
}
