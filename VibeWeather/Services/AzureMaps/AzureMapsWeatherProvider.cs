using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using VibeWeather.Data;

namespace VibeWeather.Services.AzureMaps;

/// <summary>
/// Weather from the Azure Maps Weather API (https://learn.microsoft.com/rest/api/maps/weather), authenticated
/// with the subscription key the user enters in Settings. A forecast is four billable requests (current
/// conditions, 72 hours, 15 days, and the minute forecast); a location summary is one.
/// </summary>
public sealed class AzureMapsWeatherProvider(HttpClient http, WeatherSourceSettings settings) : IWeatherProvider
{
    const string ApiVersion = "api-version=1.1";

    // Czech for the texts the service localizes. Only on current conditions, where the app reads nothing but numbers
    // and codes (the pressure tendency code is the same in every language). The hourly, daily and minute forecasts
    // stay in the default English: the app parses their precipitationType ("Rain", "Snow", "Ice", "Mix") and
    // precipitationIntensity ("Light", "Heavy"), which the docs don't promise to keep unlocalized.
    const string Language = "language=cs-CZ";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<Forecast> GetForecastAsync(WeatherLocation location, CancellationToken cancellationToken = default)
    {
        var key = await RequireKeyAsync();
        var query = Query(location);

        var currentTask = GetAsync(key, $"/weather/currentConditions/json?{ApiVersion}&{query}&unit=metric&details=true&{Language}",
            AzureMapsJsonContext.Default.CurrentConditionsResponse, cancellationToken);
        var hourlyTask = GetAsync(key, $"/weather/forecast/hourly/json?{ApiVersion}&{query}&unit=metric&duration=72",
            AzureMapsJsonContext.Default.HourlyForecastResponse, cancellationToken);
        var dailyTask = GetAsync(key, $"/weather/forecast/daily/json?{ApiVersion}&{query}&unit=metric&duration=15",
            AzureMapsJsonContext.Default.DailyForecastResponse, cancellationToken);
        var minuteTask = GetMinutesAsync(key, query, cancellationToken);

        // Wait for all four, so a failure in one doesn't leave the others' exceptions unobserved.
        await Task.WhenAll(currentTask, hourlyTask, dailyTask, minuteTask);
        var current = (await currentTask).Results.FirstOrDefault()
            ?? throw new WeatherProviderException("Služba Azure Maps nevrátila pro toto místo aktuální počasí.");
        var hourly = (await hourlyTask).Forecasts;
        var daily = (await dailyTask).Forecasts;
        var intervals = await minuteTask;

        var offset = current.DateTime.Offset;
        var localNow = DateTime.SpecifyKind(DateTime.UtcNow + offset, DateTimeKind.Unspecified);
        var minutes = intervals is null
            ? MinutesFromHour(current, hourly.FirstOrDefault(), current.Temperature?.Value ?? 0)
            : MapMinutes(intervals, DateTimeOffset.UtcNow, current.Temperature?.Value ?? 0,
                AzureMapsIcons.PrecipTypeOf(current.IconCode) ?? PrecipType.Rain);

        var cur = MapCurrent(current);
        return new Forecast(
            location,
            localNow,
            cur,
            MapHours(cur, current, hourly, minutes, localNow),
            minutes,
            MapDays(daily, location));
    }

    public async Task<IReadOnlyList<LocationSummary>> GetSummariesAsync(IReadOnlyList<WeatherLocation> locations, CancellationToken cancellationToken = default)
    {
        if (locations.Count == 0) return [];
        var key = await RequireKeyAsync();

        // No multi-location request; one (billable) call per place.
        var results = await Task.WhenAll(locations.Select(l => GetAsync(key,
            $"/weather/currentConditions/json?{ApiVersion}&{Query(l)}&unit=metric&details=false&{Language}",
            AzureMapsJsonContext.Default.CurrentConditionsResponse, cancellationToken)));

        return results.Select(r => r.Results.FirstOrDefault() is { } c
            ? new LocationSummary(c.Temperature?.Value ?? double.NaN, AzureMapsIcons.ToWmo(c.IconCode), c.IsDayTime)
            : new LocationSummary(double.NaN, 3, true)).ToList();
    }

    /// <summary>Checks a key with one small request before it is saved. Throws <see cref="AzureMapsKeyException"/> if refused.</summary>
    public Task ValidateKeyAsync(string key, CancellationToken cancellationToken = default) =>
        GetAsync(key, $"/weather/currentConditions/json?{ApiVersion}&query=50.08,14.42&unit=metric&details=false&{Language}",
            AzureMapsJsonContext.Default.CurrentConditionsResponse, cancellationToken);

    async Task<string> RequireKeyAsync() =>
        await settings.GetAzureKeyAsync() is { Length: > 0 } key
            ? key
            : throw new AzureMapsKeyException("Zadejte klíč předplatného Azure Maps v Nastavení → Zdroj počasí.");

    Task<T> GetAsync<T>(string key, string pathAndQuery, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        AzureMapsHttp.GetAsync(http, key, pathAndQuery, typeInfo, cancellationToken);

    /// <summary>
    /// The minute forecast isn't available everywhere. Without it the next hour is estimated from the hourly
    /// forecast, so only a refused key or throttling fails the whole forecast.
    /// </summary>
    async Task<MinuteInterval[]?> GetMinutesAsync(string key, string query, CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetAsync(key, $"/weather/forecast/minute/json?{ApiVersion}&{query}&interval=1",
                AzureMapsJsonContext.Default.MinuteForecastResponse, cancellationToken);
            return response.Intervals.Length > 0 ? response.Intervals : null;
        }
        catch (WeatherProviderException ex) when (ex is not AzureMapsKeyException && !ex.IsThrottled)
        {
            return null;
        }
    }

    static CurrentConditions MapCurrent(CurrentResult c) => new(
        c.DateTime.DateTime,
        c.Temperature?.Value ?? 0,
        c.RealFeelTemperature?.Value ?? c.Temperature?.Value ?? 0,
        AzureMapsIcons.ToWmo(c.IconCode),
        c.IsDayTime,
        (int)Math.Round(c.RelativeHumidity ?? 0),
        c.DewPoint?.Value ?? 0,
        c.Wind?.Speed?.Value ?? 0,
        c.WindGust?.Speed?.Value ?? c.Wind?.Speed?.Value ?? 0,
        (int)Math.Round(c.Wind?.Direction?.Degrees ?? 0),
        c.Pressure?.Value ?? 0,
        c.Visibility?.Value ?? 0,
        c.UvIndex ?? 0,
        c.PressureTendency?.Code switch { "R" => 1, "F" => -1, "S" => 0, _ => null });

    /// <summary>
    /// The hourly forecast starts at the next full hour, so the current hour comes from current conditions,
    /// with its precipitation taken from the first 60 minutes. The service has no hourly pressure; every hour
    /// carries the current value and the trend comes from the reported tendency.
    /// </summary>
    static List<HourForecast> MapHours(CurrentConditions cur, CurrentResult current, HourlyForecast[] hourly,
        IReadOnlyList<MinutePrecip> minutes, DateTime localNow)
    {
        var thisHour = localNow.Date.AddHours(localNow.Hour);
        // Three days in all, for the Hourly tab.
        var next = hourly.Where(h => h.Date.DateTime > thisHour).Take(71).ToList();
        var wettest = minutes.Take(60).MaxBy(m => m.Intensity);
        var nowHour = new HourForecast(
            thisHour, cur.Temp, cur.FeelsLike, cur.Code, cur.IsDay,
            current.HasPrecipitation ? 100 : (int)Math.Round(next.FirstOrDefault()?.PrecipitationProbability ?? 0),
            cur.WindSpeed, cur.Humidity, cur.DewPoint, cur.Uv, cur.Pressure,
            minutes.Count > 0 ? minutes.Take(60).Average(m => m.Intensity) : 0,
            minutes.Count > 0 ? minutes.Take(60).Average(m => m.SnowCmPerHour) : 0,
            wettest?.Type ?? PrecipType.Rain,
            AzureMapsIcons.SunOf(current.IconCode),
            cur.WindDirection);

        return [nowHour, .. next.Select(h => new HourForecast(
            h.Date.DateTime,
            h.Temperature?.Value ?? 0,
            h.RealFeelTemperature?.Value ?? h.Temperature?.Value ?? 0,
            AzureMapsIcons.ToWmo(h.IconCode, h.PrecipitationIntensity),
            h.IsDaylight,
            (int)Math.Round(h.PrecipitationProbability ?? 0),
            h.Wind?.Speed?.Value ?? 0,
            (int)Math.Round(h.RelativeHumidity ?? 0),
            h.DewPoint?.Value ?? 0,
            h.UvIndex ?? 0,
            cur.Pressure,
            h.TotalLiquid?.Value ?? 0,
            h.Snow?.Value ?? 0,
            TypeOf(h.PrecipitationType, AzureMapsIcons.PrecipTypeOf(h.IconCode) ?? PrecipType.Rain),
            AzureMapsIcons.SunOf(h.IconCode),
            (int)Math.Round(h.Wind?.Direction?.Degrees ?? 0)))];
    }

    static List<DayForecast> MapDays(DailyForecast[] daily, WeatherLocation location) =>
        daily.Select(d =>
        {
            var date = DateOnly.FromDateTime(d.Date.DateTime);
            var (sunrise, sunset) = SunTimes.For(date, location.Latitude, location.Longitude, d.Date.Offset);
            var windier = (d.Night?.Wind?.Speed?.Value ?? 0) > (d.Day?.Wind?.Speed?.Value ?? 0) ? d.Night : d.Day;
            return new DayForecast(
                date,
                AzureMapsIcons.ToWmo(d.Day?.IconCode ?? 3, d.Day?.PrecipitationIntensity),
                d.Temperature?.Maximum?.Value ?? 0,
                d.Temperature?.Minimum?.Value ?? 0,
                (int)Math.Round(Math.Max(d.Day?.PrecipitationProbability ?? 0, d.Night?.PrecipitationProbability ?? 0)),
                windier?.Wind?.Speed?.Value ?? 0,
                (int)Math.Round(windier?.Wind?.Direction?.Degrees ?? 0),
                d.AirAndPollen.FirstOrDefault(a => a.Name == "UVIndex")?.Value ?? 0,
                (d.Day?.TotalLiquid?.Value ?? 0) + (d.Night?.TotalLiquid?.Value ?? 0),
                sunrise,
                sunset,
                Sum(d, p => p.Snow),
                DayKinds(Sum(d, p => p.Rain), Sum(d, p => p.Ice), Sum(d, p => p.Snow)),
                d.Day is null ? null : AzureMapsIcons.SunOf(d.Day.IconCode));
        }).ToList();

    // Day and night together.
    static double Sum(DailyForecast d, Func<DayPart, UnitValue?> amount) =>
        (d.Day is null ? 0 : amount(d.Day)?.Value ?? 0) + (d.Night is null ? 0 : amount(d.Night)?.Value ?? 0);

    static List<DayPrecip> DayKinds(double rainMm, double iceMm, double snowCm)
    {
        var kinds = new List<DayPrecip>();
        if (rainMm >= DayForecast.MinAmount) kinds.Add(new(PrecipType.Rain, rainMm));
        if (iceMm >= DayForecast.MinAmount) kinds.Add(new(PrecipType.IceRain, iceMm));
        if (snowCm >= DayForecast.MinAmount) kinds.Add(new(PrecipType.Snow, snowCm));
        return kinds;
    }

    /// <summary>
    /// One entry per minute from now, each the interval covering the middle of that minute, for as far as the
    /// nowcast reaches (two hours, so 60–120 entries depending on how old its first interval is).
    /// </summary>
    /// <param name="usualType">The kind falling now, for intervals that don't say.</param>
    static List<MinutePrecip> MapMinutes(MinuteInterval[] intervals, DateTimeOffset nowUtc, double temperature, PrecipType usualType)
    {
        int count = Math.Clamp((int)(intervals[^1].StartTime.AddMinutes(1) - nowUtc).TotalMinutes, 60, 120);
        var minutes = new List<MinutePrecip>(count);
        for (int k = 0; k < count; k++)
        {
            var t = nowUtc.AddMinutes(k + 0.5);
            var interval = intervals.LastOrDefault(i => i.StartTime <= t);
            minutes.Add(interval is null || t - interval.StartTime > TimeSpan.FromMinutes(1) || interval.Dbz <= 0
                ? new MinutePrecip(0, PrecipType.Rain)
                : FromDbz(interval.Dbz, TypeOf(interval.PrecipitationType, usualType), temperature));
        }
        return minutes;
    }

    /// <summary>
    /// Water equivalent from radar reflectivity (Z = 10^(dBZ/10)): Marshall–Palmer (Z = 200 R^1.6) for rain and ice,
    /// the NWS snow relation (Z = 2000 R^2) for snow. A rain and snow mix uses the rain relation and counts half as snow.
    /// </summary>
    static MinutePrecip FromDbz(double dbz, PrecipType type, double temperature)
    {
        double z = Math.Pow(10, dbz / 10);
        double water = type == PrecipType.Snow ? Math.Sqrt(z / 2000) : Math.Pow(z / 200, 1 / 1.6);
        // mm of water × snow-to-water ratio = mm of snow; ÷ 10 for cm.
        return new MinutePrecip(water, type, water * SnowShare(type) * SnowRatio(temperature) / 10, dbz);
    }

    /// <summary>
    /// Typical snow-to-water ratio by air temperature: wet, dense snow near freezing (8:1), the usual 10:1, and
    /// lighter, fluffier snow in the cold (up to 17:1).
    /// </summary>
    static double SnowRatio(double temperature) => temperature switch
    {
        >= 0 => 8,
        > -5 => 10,
        > -10 => 13,
        _ => 17,
    };

    /// <summary>
    /// Without the minute forecast (not available everywhere): while it's precipitating now, the next hour's
    /// total spread evenly. The kind comes from what's observed now (the current icon), else from the hour;
    /// new snow from the hour's snow depth, else estimated from the water amount.
    /// </summary>
    static List<MinutePrecip> MinutesFromHour(CurrentResult current, HourlyForecast? hour, double temperature)
    {
        if (!current.HasPrecipitation) return Enumerable.Repeat(new MinutePrecip(0, PrecipType.Rain), 60).ToList();
        double water = Math.Max(hour?.TotalLiquid?.Value ?? 0, 0.5);
        var type = AzureMapsIcons.PrecipTypeOf(current.IconCode) ?? TypeOf(hour?.PrecipitationType, PrecipType.Rain);
        double snow = hour?.Snow?.Value is > 0 and var cm ? cm : water * SnowShare(type) * SnowRatio(temperature) / 10;
        return Enumerable.Repeat(new MinutePrecip(water, type, snow), 60).ToList();
    }

    /// <summary>How much of it counts as snow: all of snow, half of a rain and snow mix, none of rain or ice.</summary>
    static double SnowShare(PrecipType type) => type switch { PrecipType.Snow => 1, PrecipType.Sleet => 0.5, _ => 0 };

    static PrecipType TypeOf(string? precipitationType, PrecipType otherwise) => precipitationType switch
    {
        "Rain" => PrecipType.Rain,
        "Snow" => PrecipType.Snow,
        "Ice" => PrecipType.IceRain,
        "Mix" => PrecipType.Sleet,
        _ => otherwise,
    };

    static string Query(WeatherLocation location) =>
        $"query={location.Latitude.ToString("0.####", Inv)},{location.Longitude.ToString("0.####", Inv)}";
}
