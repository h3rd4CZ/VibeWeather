using System.Globalization;

namespace VibeWeather.Data;

/// <summary>What a layer of a weather icon is, which decides its colour (see .wicon in app.css).</summary>
public enum IconTone { Cloud, CloudBack, Sun, SunDisc, Moon, Star, Rain, Snow, Bolt, Fog }

/// <param name="Behind">Drawn behind the front cloud, which cuts it with a small gap.</param>
public sealed record IconLayer(string D, IconTone Tone, bool Behind = false);

/// <param name="Cutter">The front cloud's outline, which hides the layers behind it; null when nothing is behind.</param>
public sealed record IconArt(IReadOnlyList<IconLayer> Layers, string? Cutter = null);

/// <summary>
/// 24×24 stroke icons. UI icons are single paths from the design prototype; weather icons are layered, so the sun,
/// moon and precipitation get their own colours and a sun or moon can sit behind a cloud.
/// </summary>
public static class WeatherIcons
{
    public const string Settings = "M4 7h9M17 7h3M4 17h3M11 17h9M15 5v4M9 15v4";
    public const string Plus = "M12 5v14M5 12h14";
    public const string Search = "M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14M20 20l-4-4";
    // Home tiles.
    public const string TileWind = "M3 8.5h10a2.5 2.5 0 1 0-2.5-2.5M3 12.5h15a3 3 0 1 1-3 3M3 16.5h7";
    public const string TileHumidity = "M12 3.5c3.2 4.1 6 7.3 6 10.6a6 6 0 0 1-12 0c0-3.3 2.8-6.5 6-10.6zM9.4 14.6a2.7 2.7 0 0 0 2.2 2.3";
    public const string TileUv = "M12 8.5a3.5 3.5 0 1 0 0 7a3.5 3.5 0 1 0 0-7M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4";
    public const string TilePressure = "M4.2 17.5a8.5 8.5 0 1 1 15.6 0M12 13.5l3.5-4M12 12.5a1 1 0 1 0 0 2a1 1 0 1 0 0-2";
    public const string TileVisibility = "M2.5 12s3.5-6.5 9.5-6.5s9.5 6.5 9.5 6.5s-3.5 6.5-9.5 6.5S2.5 12 2.5 12zM12 9.25a2.75 2.75 0 1 0 0 5.5a2.75 2.75 0 1 0 0-5.5";
    public const string TileSunset = "M3 19h18M7 19a5 5 0 0 1 10 0M12 3.5v6M9.5 7l2.5 2.5L14.5 7M4.4 13.6l1.3 1M19.6 13.6l-1.3 1";

    /// <summary>Points up; rotated to show where the wind blows.</summary>
    public const string WindArrow = "M12 20V5M6.5 10.5L12 5l5.5 5.5";
    /// <summary>Warning triangle for severe weather warnings.</summary>
    public const string Warning = "M12 3.5L2.5 20h19L12 3.5zM12 10v4.5M12 17.5v.01";

    public const string TabNow = "M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6l1.4 1.4M17 17l1.4 1.4M5.6 18.4L7 17M17 7l1.4-1.4M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8";
    public const string TabNextHour = "M4 20v-4M8 20v-8M12 20V8M16 20v-7M20 20v-3";
    public const string TabHourly = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M12 7.5V12l3 2";
    public const string TabClose = "M6.5 6.5l11 11M17.5 6.5l-11 11";
    public const string TabDaily = "M4.5 5.5h15v14h-15zM4.5 10h15M8.5 3v4M15.5 3v4";

    // Clouds. Large one for a cloud on its own or over precipitation (raised), small one with a sun or moon behind.
    const string CloudLarge = "M7 18h10a4 4 0 0 0 .5-7.97A6 6 0 0 0 6.1 11.1A3.5 3.5 0 0 0 7 18z";
    const string CloudRaised = "M7 14h10a4 4 0 0 0 .5-7.97A6 6 0 0 0 6.1 7.1A3.5 3.5 0 0 0 7 14z";
    const string CloudSmall = "M9 20h8a3.5 3.5 0 0 0 .4-6.97A5 5 0 0 0 8 13.6A3.2 3.2 0 0 0 9 20z";
    const string CloudSmallRaised = "M9 15h8a3.5 3.5 0 0 0 .4-6.97A5 5 0 0 0 8 8.6A3.2 3.2 0 0 0 9 15z";
    // Overcast: a front cloud with a second, fainter one behind it to the upper right.
    const string CloudFront = "M7 20h8a3.5 3.5 0 0 0 .4-6.97A5 5 0 0 0 6 13.6A3.2 3.2 0 0 0 7 20z";
    const string CloudBack = "M13.5 12h5.6a2.45 2.45 0 0 0 .28-4.88A3.5 3.5 0 0 0 12.8 7.52A2.24 2.24 0 0 0 13.5 12z";
    const string CloudFog = "M9 13h8a3.5 3.5 0 0 0 .4-6.97A5 5 0 0 0 8 6.6A3.2 3.2 0 0 0 9 13z";

    // Under the raised cloud.
    const string Drops = "M8.5 17.25l-1 2.75M12.5 17.25l-1.25 3.75M16.5 17.25l-1 2.75";
    const string SideDrops = "M8.5 17.25l-1 2.75M16.5 17.25l-1 2.75";
    const string Bolt = "M12.8 15l-2.6 3.8h3.4l-2.4 3.7";
    const string FogLines = "M4 16.5h16M7 20h10";

    static readonly IconArt ClearArt = new([.. Sun(12, 12, 4, 6.5, 9)]);

    static readonly IconArt MoonArt = new(
    [
        new(Crescent(12, 12, 1), IconTone.Moon),
        new("M18 4v3M16.5 5.5h3", IconTone.Star),
    ]);

    static readonly IconArt PartlyArt = new(
        [.. Behind(Sun(8.5, 8.5, 2.75, 4.4, 5.9)), new(CloudSmall, IconTone.Cloud)], CloudSmall);

    static readonly IconArt PartlyNightArt = new(
        [new(Crescent(8.25, 8.25, 0.66), IconTone.Moon, Behind: true), new(CloudSmall, IconTone.Cloud)], CloudSmall);

    static readonly IconArt CloudyArt = new(
        [new(CloudBack, IconTone.CloudBack, Behind: true), new(CloudFront, IconTone.Cloud)], CloudFront);

    static readonly IconArt FogArt = new([new(CloudFog, IconTone.Cloud), new(FogLines, IconTone.Fog)]);

    static readonly IconArt RainArt = new([new(CloudRaised, IconTone.Cloud), new(Drops, IconTone.Rain)]);

    static readonly IconArt SunRainArt = new(
    [
        .. Behind(Sun(7.5, 7, 2.5, 4, 5.4)),
        new(CloudSmallRaised, IconTone.Cloud),
        new("M10 17.5l-1 2.5M13.5 17.5l-1.25 3.75M17 17.5l-1 2.5", IconTone.Rain),
    ], CloudSmallRaised);

    static readonly IconArt SnowArt = new(
    [
        new(CloudRaised, IconTone.Cloud),
        new(Flake(8.25, 18.25, 1.6) + Flake(12.25, 20.75, 1.6) + Flake(16.25, 18.25, 1.6), IconTone.Snow),
    ]);

    static readonly IconArt SleetArt = new(
    [
        new(CloudRaised, IconTone.Cloud),
        new(SideDrops, IconTone.Rain),
        new(Flake(12.25, 19.75, 1.6), IconTone.Snow),
    ]);

    static readonly IconArt StormArt = new(
        [new(CloudRaised, IconTone.Cloud), new(SideDrops, IconTone.Rain), new(Bolt, IconTone.Bolt)]);

    public static IconArt For(WeatherCondition condition) => condition switch
    {
        WeatherCondition.Clear => ClearArt,
        WeatherCondition.Partly => PartlyArt,
        WeatherCondition.PartlyNight => PartlyNightArt,
        WeatherCondition.Cloudy => CloudyArt,
        WeatherCondition.Fog => FogArt,
        WeatherCondition.Rain => RainArt,
        WeatherCondition.SunRain => SunRainArt,
        WeatherCondition.Sleet => SleetArt,
        WeatherCondition.Snow => SnowArt,
        WeatherCondition.Storm => StormArt,
        _ => MoonArt,
    };

    /// <summary>A sun at (cx, cy): a disc of radius r and eight rays from rayIn to rayOut.</summary>
    static IconLayer[] Sun(double cx, double cy, double r, double rayIn, double rayOut)
    {
        var rays = string.Concat(Enumerable.Range(0, 8).Select(k =>
        {
            double a = k * Math.PI / 4, dx = Math.Cos(a), dy = Math.Sin(a);
            return $"M{N(cx + dx * rayIn)} {N(cy + dy * rayIn)}L{N(cx + dx * rayOut)} {N(cy + dy * rayOut)}";
        }));
        return
        [
            new($"M{N(cx)} {N(cy - r)}a{N(r)} {N(r)} 0 1 0 0 {N(2 * r)}a{N(r)} {N(r)} 0 1 0 0 {N(-2 * r)}", IconTone.SunDisc),
            new(rays, IconTone.Sun),
        ];
    }

    /// <summary>A crescent moon whose full disc (radius 7.5 × scale) is centred on (cx, cy), lit side lower left.</summary>
    static string Crescent(double cx, double cy, double scale)
    {
        double r = 7.5 * scale;
        return $"M{N(cx + 6.77 * scale)} {N(cy + 3.23 * scale)}A{N(r)} {N(r)} 0 0 1 {N(cx - 3.23 * scale)} {N(cy - 6.77 * scale)}"
            + $"a{N(r)} {N(r)} 0 1 0 {N(10 * scale)} {N(10 * scale)}z";
    }

    /// <summary>A six-armed snowflake of radius r.</summary>
    static string Flake(double cx, double cy, double r)
    {
        double w = r * Math.Sqrt(3) / 2;
        return $"M{N(cx)} {N(cy - r)}v{N(2 * r)}M{N(cx - w)} {N(cy - r / 2)}l{N(2 * w)} {N(r)}M{N(cx - w)} {N(cy + r / 2)}l{N(2 * w)} {N(-r)}";
    }

    static IEnumerable<IconLayer> Behind(IEnumerable<IconLayer> layers) => layers.Select(l => l with { Behind = true });

    static string N(double x) => Math.Round(x, 2).ToString(CultureInfo.InvariantCulture);
}
