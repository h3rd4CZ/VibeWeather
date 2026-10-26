using System.Globalization;
using System.Text;

namespace VibeWeather.Data;

/// <summary>
/// Display-ready values for the screens, following the design prototype's layout logic.
/// Temperatures, wind speeds and times are shown in the <see cref="DisplayUnits"/> chosen in Settings.
/// Numbers that end up in CSS or SVG are formatted with the invariant culture.
/// </summary>
public sealed class ForecastView
{
    /// <param name="Icon">Path of its icon (see WeatherIcons.Tile*); Tone a CSS class colouring it ("" for the text colour).</param>
    /// <param name="Arrow">When set, an arrow after the value turned this many degrees from up: where the wind blows,
    /// or the pressure trend (45 rising, 90 steady, 135 falling).</param>
    public sealed record Tile(string Key, string Value, string Unit, string Sub, string Icon, string Tone = "", int? Arrow = null);
    public sealed record Bar(int Height, string Color);
    /// <summary>A mark under the minute chart: offset from now ("30m") and clock time, at Left percent.</summary>
    public sealed record AxisTick(string Offset, string Clock, string Left);
    public sealed record LegendItem(string Name, string Color);
    /// <summary>
    /// What a bar of the Next hour chart stands for, shown while it's touched: "15:42", "1,8 mm/h", "Mírný déšť",
    /// "28 dBZ" (measured) or "≈ 28 dBZ (odhad)"; Snow is the snow rate when snow is part of it. Center is the bar's
    /// middle as a percentage of the chart's width. Strength is how heavy it is, 0 (dry) to 1, on the bars' height scale.
    /// </summary>
    public sealed record BarInfo(string Time, string Rate, string? Snow, string Kind, string KindColor, string Reflectivity, string Center, double Strength);
    /// <summary>A dashed line across the Next hour chart, Bottom px above its foot, labelled "20 dBZ".</summary>
    public sealed record GridLine(string Label, int Bottom);

    /// <summary>A colour along a gradient, at Offset percent of its length.</summary>
    public sealed record GradientStop(string Offset, string Color);
    public sealed record Stat(string Key, string Value);
    public sealed record HourCell(string Label, WeatherCondition Condition, string Temp);
    /// <param name="Day">Set on the first hour of each day: "Dnes", "Zítra · 13. 10. · 4,2 mm", "St · 14. 10.".</param>
    /// <param name="Precip">The hour's precipitation bar, coloured by kind; null when dry.</param>
    /// <param name="Amount">What falls in the hour, "1,2 mm" or for snow "0,8 cm"; null when dry.</param>
    /// <param name="Wind">Wind speed as a number in the chosen unit (see <see cref="WindUnit"/>).</param>
    /// <param name="WindTo">Where the wind blows to, in degrees: the rotation of an arrow pointing up.</param>
    public sealed record HourColumn(string Label, WeatherCondition Condition, string Temp, int TempTop, string Pop, bool PopHigh, bool Selected,
        string? Day, Bar? Precip, string? Amount, string Wind, int WindTo);
    /// <param name="Date">"12. 10.".</param>
    /// <param name="Precip">One line per kind that falls, "Déšť 4,2 mm", "Sníh 2 cm", in its chart colour; empty when dry.</param>
    /// <param name="WetColor">For a wet day, the colour of its main kind (most of it falls as that), which tints the row;
    /// WetMix how strongly ("6%" to "24%", by the day's total), so heavier days stand out more. Null when dry.</param>
    /// <param name="LowColor">CSS colour for the low temperature, as on the Now tab; HighColor likewise.</param>
    /// <param name="Wind">The day's strongest wind, "13 km/h"; WindTo is where it blows to (arrow rotation, degrees).</param>
    public sealed record DayRow(
        string Name, string Date, WeatherCondition Condition, string Pop, string Low, string High, string LowColor, string HighColor,
        string RangeLeft, string RangeWidth, bool IsToday, string NowLeft,
        bool Open, string Summary, IReadOnlyList<Stat> Stats, IReadOnlyList<DayPrecipLine> Precip,
        string? WetColor, string? WetMix, string Wind, int WindTo, bool NewWeek);

    /// <param name="Amount">"4,2 mm" (water) or "2 cm" (new snow).</param>
    public sealed record DayPrecipLine(string Name, string Color, string Amount);

    const int HourColumnWidth = 56;
    // The Next hour bars stand on a radar reflectivity scale from 0 to MaxDbz, which is BarArea px tall.
    const double MaxDbz = 60;
    const int BarArea = 116;
    static readonly int[] GridDbz = [20, 40, 60];
    const string DryColor = "var(--dry)";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly CultureInfo Cs = Czech.Culture;

    // Hue stops for temperature colour: icy blue at -5°C, teal around 12°C, amber at 24°C, red at 32°C.
    static readonly (double Temp, double Hue)[] TempHues = [(-5, 245), (5, 215), (12, 175), (18, 95), (24, 60), (32, 28)];

    // Header
    public string AccentKey { get; }
    public string LocationName { get; }
    public string HeaderSubtitle { get; }

    // Now
    public int CurrentTemp { get; }
    public WeatherCondition CurrentCondition { get; }
    public string CurrentDescription { get; }
    public string FeelsLike { get; }
    public string TodayLow { get; }
    public string TodayHigh { get; }
    public string NowMarkerLeft { get; }
    public string TempColor { get; }
    public string TodayLowColor { get; }
    public string TodayHighColor { get; }
    public IReadOnlyList<HourCell> Next6 { get; }
    public IReadOnlyList<Tile> Tiles { get; }

    // Next hour (or two, when the source forecasts that far by the minute)
    public string MinuteHeading { get; }
    public string MinuteShortHeading { get; }
    public IReadOnlyList<AxisTick> MinuteAxis { get; }
    public bool HasPrecip { get; }
    public bool IsMixed { get; }
    public string SummaryTitle { get; }
    public string SummarySub { get; }
    public string MinuteTotal { get; }
    public IReadOnlyList<Bar> Spark { get; }
    public IReadOnlyList<Bar> MinuteBars { get; }
    /// <summary>Details for each of <see cref="MinuteBars"/>, in the same order.</summary>
    public IReadOnlyList<BarInfo> MinuteBarInfo { get; }
    /// <summary>The height of the tallest of <see cref="MinuteBars"/> (px): the bar details are shown above it.</summary>
    public int MinuteBarsTop { get; }
    /// <summary>The reflectivity scale behind <see cref="MinuteBars"/>, up to <see cref="MaxDbz"/>.</summary>
    public IReadOnlyList<GridLine> MinuteGrid { get; }
    public IReadOnlyList<LegendItem> Legend { get; }
    public string ReadingTop { get; }
    public string ReadingValue { get; }
    public string ReadingCategory { get; }
    public string ReadingColor { get; }
    public IReadOnlyList<Stat> Events { get; }

    // Hourly (three days)
    public string HourHeadline { get; }
    public IReadOnlyList<HourColumn> HourColumns { get; }
    /// <summary>The kinds of precipitation in the hourly bars; empty when all hours are dry.</summary>
    public IReadOnlyList<LegendItem> HourLegend { get; }
    public int HourTrackWidth { get; }
    public string HourLine { get; }
    /// <summary>The unit of the hourly wind speeds, "km/h".</summary>
    public string WindUnit => units.WindUnit;
    /// <summary>The curve's colours: each hour's temperature colour at its point, across the track.</summary>
    public IReadOnlyList<GradientStop> HourLineStops { get; }
    public string HourArea { get; }
    public int SelectedX { get; }
    public string SelectedY { get; }
    public string HourDetailWhen { get; }
    public string HourDetailDescription { get; }
    public string HourDetailTemp { get; }
    public IReadOnlyList<Stat> HourDetailStats { get; }

    // Daily (up to 16 days)
    public string DayRange { get; }
    public IReadOnlyList<DayRow> DayRows { get; }

    readonly DisplayUnits units;

    public ForecastView(Forecast forecast, DisplayUnits units)
    {
        this.units = units;
        var cur = forecast.Current;
        var hours = forecast.Hours;
        var mins = forecast.Minutes;
        var days = forecast.Days;
        var now = forecast.LocalNow;
        var today = days.Count > 0 ? days[0] : null;
        double todayLow = today?.Low ?? cur.Temp, todayHigh = today?.High ?? cur.Temp;

        AccentKey = WeatherCodes.Scenario(cur.Code).ToString().ToLowerInvariant();
        LocationName = forecast.Location.Name;
        HeaderSubtitle = $"{ShortDate(now)} · Aktualizováno {Time(now)}";

        // Now
        CurrentTemp = units.TempValue(cur.Temp);
        // Sunshine is known per hour; the current hour's stands for now.
        var sunNow = hours.Count > 0 ? hours[0].Sun : null;
        CurrentCondition = WeatherCodes.Condition(cur.Code, cur.IsDay, sunNow);
        CurrentDescription = WeatherCodes.Describe(cur.Code, cur.IsDay, sunNow);
        FeelsLike = $"Pocitově {Temp(cur.FeelsLike)}";
        TodayLow = Temp(todayLow);
        TodayHigh = Temp(todayHigh);
        NowMarkerLeft = Pct(Math.Clamp((cur.Temp - todayLow) / NonZero(todayHigh - todayLow), 0, 1));
        TempColor = TemperatureColor(cur.Temp);
        TodayLowColor = TemperatureColor(todayLow);
        TodayHighColor = TemperatureColor(todayHigh);
        Next6 = hours.Take(6).Select((h, i) => new HourCell(
            i == 0 ? "Teď" : HourLabel(h.Time), WeatherCodes.Condition(h.Code, h.IsDay, h.Sun), Temp(h.Temp))).ToList();
        var trend = PressureTrend(cur, hours);
        Tiles =
        [
            new("VÍTR", units.WindValue(cur.WindSpeed).ToString(Inv), units.WindUnit,
                $"{Compass(cur.WindDirection)} · nárazy {units.WindValue(cur.WindGusts)}",
                WeatherIcons.TileWind, Arrow: DownWind(cur.WindDirection)),
            new("VLHKOST", cur.Humidity.ToString(Inv), "%", $"Rosný bod {Temp(cur.DewPoint)}", WeatherIcons.TileHumidity, "tone-rain"),
            new("UV INDEX", Round(cur.Uv).ToString(Inv), "", UvText(cur.Uv), WeatherIcons.TileUv, "tone-sun"),
            new("TLAK", Round(cur.Pressure).ToString(Inv), "hPa", PressureText(trend), WeatherIcons.TilePressure,
                Arrow: trend switch { > 0 => 45, < 0 => 135, 0 => 90, _ => null }),
            new("VIDITELNOST", cur.VisibilityKm >= 10 ? Round(cur.VisibilityKm).ToString(Inv) : D1(cur.VisibilityKm), "km",
                cur.VisibilityKm >= 10 ? "Dobrá viditelnost" : "Snížená viditelnost", WeatherIcons.TileVisibility),
            today is null
                ? new("ZÁPAD SLUNCE", "–", "", "", WeatherIcons.TileSunset, "tone-sun")
                : new("ZÁPAD SLUNCE", Time(today.Sunset), "", $"Východ {Time(today.Sunrise)}", WeatherIcons.TileSunset, "tone-sun"),
        ];

        // Next hour: hue shows the precipitation type, lightness the intensity. The window is as long as the
        // minute forecast (60 or 120 minutes); "span" below is its length, windowEnd the time the axis ends at.
        int span = Math.Max(mins.Count, 1);
        bool twoHours = span > 60;
        // "Příští hodinu bez srážek", "Slabý déšť celou příští hodinu".
        string window = twoHours ? "příští 2 hodiny" : "příští hodinu";
        string wholeWindow = twoHours ? "celé příští 2 hodiny" : "celou příští hodinu";
        MinuteHeading = twoHours ? "PŘÍŠTÍ 2 HODINY" : "PŘÍŠTÍCH 60 MINUT";
        MinuteShortHeading = twoHours ? "PŘÍŠTÍ 2 HODINY" : "PŘÍŠTÍ HODINA";
        // Ticks over the nominal window; a nowcast a minute or two short still ends on "2 h" at the right edge.
        int tickEvery = twoHours ? 30 : 15;
        int nominal = twoHours ? 120 : 60;
        string windowEnd = Time(now.AddMinutes(nominal));
        MinuteAxis = Enumerable.Range(0, nominal / tickEvery + 1).Select(k => k * tickEvery).Select(m => new AxisTick(
            m == 0 ? "Teď" : m % 60 == 0 ? $"{m / 60} h" : m > 60 ? $"{m / 60} h {m % 60}" : $"{m} min",
            units.ShortTime(now.AddMinutes(m)),
            Pct(Math.Min(1, (double)m / span)))).ToList();
        var wet = Enumerable.Range(0, mins.Count).Where(i => IsWet(mins[i])).ToList();
        int peak = IndexOfMax(mins);
        // Amounts over the window: water equivalent of everything that falls (mm), and new snow (cm).
        double total = mins.Sum(m => m.Intensity) / 60;
        double snowCm = NewSnowCm(mins);
        bool hasSnow = snowCm >= MinSnowCm;
        string snowNote = hasSnow ? $" Napadne asi {SnowAmount(snowCm)} nového sněhu." : "";
        var nextRain = days.Skip(1).FirstOrDefault(d => d.Pop >= 50);
        var segments = Segments(mins, wet);
        var types = segments.Select(s => s.Type).Distinct().ToList();
        string NameAt(int i) => PrecipName(mins[i].Type);
        string LowerNameAt(int i) => Lower(NameAt(i));
        // "Slabý déšť": the intensity agrees with every kind, as all are masculine nouns.
        string KindAt(int i) => $"{Category(mins[i].Intensity)} {LowerNameAt(i)}";
        string At(int minute) => Time(now.AddMinutes(minute));

        // The first section, as alerts see it: dry breaks under MergeGapMinutes are part of it; a longer one ends it.
        // Then the start of the next section, if any.
        int first = wet.Count > 0 ? wet[0] : 0, last = first;
        foreach (int i in wet)
        {
            if (i - last > MergeGapMinutes) break;
            last = i;
        }
        bool lasts = last >= span - 1;
        int? later = wet.Where(i => i > last).Select(i => (int?)i).FirstOrDefault();
        string laterNote = later is { } next ? $" Další srážky od {At(next)}." : "";

        HasPrecip = wet.Count > 0;
        IsMixed = types.Count > 1;
        if (!HasPrecip)
        {
            SummaryTitle = $"{Cap(window)} bez srážek";
            SummarySub = $"Do {windowEnd} bez srážek." + (nextRain is null ? "" : $" Další srážky se očekávají {OnDay(nextRain.Date, now)}.");
        }
        else
        {
            // Its own sentence; "skončí" fits both a kind (singular) and "Srážky" (plural), "potrvá/potrvají" doesn't.
            string ending = IsMixed
                ? lasts ? $" Srážky potrvají i po {windowEnd}." : $" Srážky skončí do {At(last + 1)}."
                : lasts ? $" Potrvá i po {windowEnd}." : $" Skončí do {At(last + 1)}.";
            if (IsMixed)
            {
                SummaryTitle = first == 0
                    ? $"Teď {LowerNameAt(0)}, později {Lower(PrecipName(segments[1].Type))}"
                    : $"{NameAt(first)} začne za {Duration(first)}";
                SummarySub = Cap(string.Join(", ", segments.Skip(1).Select(s => $"od {At(s.Start)} {Lower(PrecipName(s.Type))}")))
                    + $". Nejsilnější kolem {At(peak)} ({Lower(KindAt(peak))}).{ending}{laterNote}{snowNote}";
            }
            else if (first == 0)
            {
                SummaryTitle = lasts
                    ? $"{KindAt(0)} {wholeWindow}"
                    : $"{KindAt(0)}, skončí za {Duration(last + 1)}";
                SummarySub = (lasts ? $"Nejsilnější kolem {At(peak)}." : $"Postupně slábne, od {At(last + 1)} bez srážek.") + laterNote + snowNote;
            }
            else
            {
                SummaryTitle = $"{NameAt(first)} začne za {Duration(first)}";
                SummarySub = $"Nejsilnější kolem {At(peak)} ({Lower(Category(mins[peak].Intensity))}).{ending}{laterNote}{snowNote}";
            }
        }
        // Snow alone is totalled as new snow depth; anything else (or a mix) as water.
        MinuteTotal = !HasPrecip ? $"{D1(total)} MM"
            : !IsMixed && types[0] == PrecipType.Snow && hasSnow ? $"SNÍH · ≈ {Upper(SnowAmount(snowCm))}"
            : $"{(IsMixed ? "SMÍŠENÉ" : Upper(NameAt(wet[0])))} · {D1(total)} MM";
        // At most 60 bars, so two hours get one bar per 2 minutes rather than hairlines.
        var barMins = BarMinutes(mins);
        // The Now tab's preview, on the same reflectivity scale as the Next hour bars: 60 dBZ fills its 30px.
        Spark = barMins.Select(m => IsWet(m)
            ? new Bar(Math.Max(3, Round(30 * DbzScale(m))), PrecipColor(m.Intensity, m.Type))
            : new Bar(2, DryColor)).ToList();
        // Heights follow the reflectivity, so heavy rain and storms still fit under the 60 dBZ line; wet bars stay visible.
        MinuteBars = barMins.Select(m => IsWet(m)
            ? new Bar(Math.Max(4, Round(BarArea * DbzScale(m))), PrecipColor(m.Intensity, m.Type))
            : new Bar(2, DryColor)).ToList();
        MinuteBarsTop = MinuteBars.Select(b => b.Height).DefaultIfEmpty(0).Max();
        MinuteGrid = [.. GridDbz.Select(dbz => new GridLine($"{dbz} dBZ", Round(BarArea * dbz / MaxDbz)))];
        int perBar = Math.Max(1, (int)Math.Ceiling(mins.Count / 60.0));
        MinuteBarInfo = barMins.Select((m, i) => new BarInfo(
            Time(now.AddMinutes(i * perBar)),
            $"{D1(m.Intensity)} mm/h",
            m.SnowCmPerHour > 0 && IsWet(m) ? $"{D1(m.SnowCmPerHour)} cm/h sněhu" : null,
            IsWet(m) ? KindOf(m) : "Sucho",
            IsWet(m) ? PrecipColor(Math.Max(m.Intensity, 3), m.Type) : "var(--mut)",
            Reflectivity(m),
            Pct((i + 0.5) / barMins.Count),
            IsWet(m) ? DbzScale(m) : 0)).ToList();
        Legend = IsMixed ? types.Select(t => new LegendItem(PrecipName(t), PrecipColor(3, t))).ToList() : [];
        // What falls now; the heaviest is in the summary and the events below.
        bool wetNow = mins.Count > 0 && IsWet(mins[0]);
        ReadingTop = $"TEĎ · {Time(now)}";
        ReadingValue = mins.Count == 0 ? $"{D1(0)} mm/h" : Rate(mins[0]);
        ReadingCategory = wetNow ? KindAt(0) : "Sucho";
        ReadingColor = wetNow ? PrecipColor(Math.Max(mins[0].Intensity, 3), mins[0].Type) : "var(--mut)";
        Events = HasPrecip
            ?
            [
                // Each kind with its time and amount: "Déšť  16:51 – 17:20 · 1,2 mm", "Sníh  17:20 – později · ≈ 2 cm".
                .. IsMixed
                    ? segments.Select(s => new Stat(PrecipName(s.Type),
                        $"{At(s.Start)} – {(s.End >= span - 1 ? "později" : At(s.End + 1))} · {Amount(mins, s.Start, s.End)}"))
                    : [new Stat("Začátek", wet[0] == 0 ? "Teď" : At(wet[0]))],
                new("Nejsilnější", $"{At(peak)} · {Rate(mins[peak])}"),
                new("Konec", lasts ? $"Po {windowEnd}" : At(last + 1)),
                .. later is { } again ? [new Stat("Další srážky", At(again))] : Array.Empty<Stat>(),
                .. hasSnow
                    ? [new Stat("Nový sníh", $"≈ {SnowAmount(snowCm)}"), new Stat(IsMixed ? "Celkem (voda)" : "Vodní hodnota", $"{D1(total)} mm")]
                    : new[] { new Stat("Očekávaný úhrn", $"{D1(total)} mm") },
            ]
            :
            [
                new("Srážky", "Neočekávají se"),
                new("Další srážky", nextRain is null
                    ? days.Count <= 1 ? "Dnes ne" : $"V příštích {days.Count} dnech ne"
                    : ShortDate(nextRain.Date)),
            ];

        // Hourly: 56px columns on a 250px track. From the top: the day (where one starts), hour, icon, then the
        // temperature curve between y=110 and y=168, and precipitation bars standing on y=216 above the chance.
        var temps = hours.Select(h => h.Temp).ToList();
        double tmin = temps.DefaultIfEmpty(cur.Temp).Min(), tmax = temps.DefaultIfEmpty(cur.Temp).Max();
        var points = hours.Select((h, i) => (X: i * HourColumnWidth + HourColumnWidth / 2, Y: 168 - (h.Temp - tmin) / NonZero(tmax - tmin) * 58)).ToList();
        const int selected = 0;
        HourTrackWidth = hours.Count * HourColumnWidth;
        // Each day's total over the hours shown (for today, the rest of it).
        var dayTotals = hours.Where(IsWet).GroupBy(h => h.Time.Date)
            .ToDictionary(g => g.Key, g => (Water: g.Sum(h => h.Precipitation), Snow: g.Sum(h => h.Snow)));
        string DayLabel(DateTime time) => dayTotals.TryGetValue(time.Date, out var total) && total.Water >= DayForecast.MinAmount
            ? $"{DayName(time, now)} · {D1(total.Water)} mm" + (total.Snow >= MinSnowCm ? $" · ≈ {SnowAmount(total.Snow)} sněhu" : "")
            : DayName(time, now);
        HourColumns = hours.Select((h, i) => new HourColumn(
            i == 0 ? "Teď" : HourLabel(h.Time), WeatherCodes.Condition(h.Code, h.IsDay, h.Sun), Temp(h.Temp),
            Round(points[i].Y - 30), $"{h.Pop}%", h.Pop >= 30, i == selected,
            Day: i == 0 || h.Time.Date != hours[i - 1].Time.Date ? DayLabel(h.Time) : null,
            Precip: IsWet(h)
                ? new Bar(Round(3 + 23 * Math.Min(1, Math.Sqrt(h.Precipitation / 6))), PrecipColor(h.Precipitation, h.Type))
                : null,
            Amount: !IsWet(h) ? null
                : h.Type == PrecipType.Snow && h.Snow >= MinSnowCm ? SnowAmount(h.Snow)
                : $"{D1(h.Precipitation)} mm",
            Wind: units.WindValue(h.Wind).ToString(Inv),
            WindTo: DownWind(h.WindDirection))).ToList();
        HourLegend = hours.Where(IsWet).Select(h => h.Type).Distinct().Order()
            .Select(t => new LegendItem(PrecipName(t), PrecipColor(3, t))).ToList();
        if (hours.Count > 0)
        {
            // The headline is about the coming 24 hours; the curve shows all three days.
            var day = hours.Take(24).ToList();
            var highHour = day.MaxBy(h => h.Temp)!;
            var lowHour = day.MinBy(h => h.Temp)!;
            // "Max. 15° ve 14:00, min. 8° v noci".
            HourHeadline = $"Max. {Temp(highHour.Temp)} {AtHour(highHour.Time)}, min. {Temp(lowHour.Temp)} "
                + (lowHour.IsDay ? AtHour(lowHour.Time) : "v noci");
            HourLine = Smooth(points);
            HourArea = $"{HourLine} L{points[^1].X},186 L{points[0].X},186 Z";
            HourLineStops = hours.Select((h, i) => new GradientStop(Pct((double)points[i].X / HourTrackWidth), TemperatureColor(h.Temp))).ToList();
            SelectedX = points[selected].X;
            SelectedY = F1(points[selected].Y);
            var sh = hours[selected];
            HourDetailWhen = "TEĎ";
            HourDetailDescription = WeatherCodes.Describe(sh.Code, sh.IsDay, sh.Sun);
            HourDetailTemp = Temp(sh.Temp);
            HourDetailStats =
            [
                new("Pocitově", Temp(sh.FeelsLike)),
                new("Srážky", $"{sh.Pop}%"),
                new("Vítr", units.WindSpeed(sh.Wind)),
                new("Vlhkost", $"{sh.Humidity}%"),
                new("UV index", Round(sh.Uv).ToString(Inv)),
                new("Rosný bod", Temp(sh.DewPoint)),
            ];
        }
        else
        {
            HourHeadline = "Hodinová předpověď není k dispozici";
            HourLine = HourArea = SelectedY = HourDetailWhen = HourDetailDescription = HourDetailTemp = "";
            HourLineStops = [];
            HourDetailStats = [];
        }

        // Daily: bars share one scale across all days.
        double gmin = days.Select(d => d.Low).DefaultIfEmpty(0).Min(), gmax = days.Select(d => d.High).DefaultIfEmpty(0).Max();
        double range = NonZero(gmax - gmin);
        DayRange = days.Count > 0
            ? Upper($"{ShortDate(days[0].Date)} – {ShortDate(days[^1].Date)}")
            : "";
        DayRows = days.Select((d, i) => new DayRow(
            i == 0 ? "Dnes" : DayOfWeek(d.Date), d.Date.ToString("d. M.", Cs), WeatherCodes.Condition(d.Code, isDay: true, d.Sun),
            d.Pop >= 20 ? $"{d.Pop}%" : "", Temp(d.Low), Temp(d.High), TemperatureColor(d.Low), TemperatureColor(d.High),
            Pct((d.Low - gmin) / range), Pct((d.High - d.Low) / range),
            IsToday: i == 0, NowLeft: Pct(Math.Clamp((cur.Temp - gmin) / range, 0, 1)),
            Open: i == 0,
            // "Úterý 13. října. Polojasno, max. 15°, min. 8°. Vítr od západu, až 13 km/h."
            Summary: $"{Cap(d.Date.ToString("dddd d. MMMM", Cs))}. {WeatherCodes.Describe(d.Code, isDay: true, d.Sun)}, max. {Temp(d.High)}, min. {Temp(d.Low)}. "
                + $"Vítr {CompassFrom(d.WindDirection)}, až {units.WindSpeed(d.Wind)}.",
            Stats:
            [
                new("Východ", Time(d.Sunrise)),
                new("Západ", Time(d.Sunset)),
                new("Srážky", $"{D1(d.PrecipMm)} mm"),
                // Chance of precipitation; "Pravděpodobnost" doesn't fit a third of a phone's width.
                new("Šance", $"{d.Pop}%"),
                new("Vítr", units.WindSpeed(d.Wind)),
                new("UV index", $"{Round(d.Uv)} · {UvText(d.Uv)}"),
            ],
            Precip: DayLines(d),
            WetColor: WetKind(d) is { } main ? PrecipColor(3, main) : null,
            WetMix: WetKind(d) is null ? null : WetMix(d.PrecipMm),
            Wind: units.WindSpeed(d.Wind),
            WindTo: DownWind(d.WindDirection),
            // A Monday after the first row starts a new week, set apart by a little space.
            NewWeek: i > 0 && d.Date.DayOfWeek == System.DayOfWeek.Monday)).ToList();
    }

    /// <summary>A line per kind, "Déšť 4,2 mm" / "Sníh 2 cm"; a total alone when the source gives no split.</summary>
    static List<DayPrecipLine> DayLines(DayForecast d)
    {
        var lines = (d.Kinds ?? []).Select(k => new DayPrecipLine(PrecipName(k.Type), PrecipColor(3, k.Type),
            k.Type == PrecipType.Snow ? SnowAmount(k.Amount) : $"{D1(k.Amount)} mm")).ToList();
        if (lines.Count == 0 && d.PrecipMm >= DayForecast.MinAmount)
            lines.Add(new("Srážky", PrecipColor(3, PrecipType.Rain), $"{D1(d.PrecipMm)} mm"));
        return lines;
    }

    /// <summary>The kind most of a wet day's precipitation falls as (a cm of snow is about a mm of water); null when dry.</summary>
    static PrecipType? WetKind(DayForecast d) =>
        d.PrecipMm < DayForecast.MinAmount ? null
        : (d.Kinds ?? []).MaxBy(k => k.Amount)?.Type ?? PrecipType.Rain;

    /// <summary>How strongly a wet day's row is tinted, by its total: under 1 mm, under 5, under 15, then more.</summary>
    static string WetMix(double mm) => mm switch { < 1 => "7%", < 5 => "12%", < 15 => "18%", _ => "26%" };

    public static string PrecipName(PrecipType type) => type switch
    {
        PrecipType.Rain => "Déšť",
        PrecipType.IceRain => "Mrznoucí déšť",
        PrecipType.Sleet => "Déšť se sněhem",
        _ => "Sníh",
    };

    string Temp(double celsius) => units.Temp(celsius);

    string Time(DateTime time) => units.Time(time);

    string HourLabel(DateTime time) => units.Hour(time);

    /// <summary>A whole hour with its preposition, as it is read out: "v 8:00", "ve 14:00", "ve 2 odp.".</summary>
    string AtHour(DateTime time)
    {
        int h = units.Is24Hour ? time.Hour : (time.Hour % 12 == 0 ? 12 : time.Hour % 12);
        return (h is 2 or 3 or 4 or 12 or 13 or 14 or 20 or 21 or 22 or 23 ? "ve " : "v ") + HourLabel(time);
    }

    /// <summary>"Út".</summary>
    static string DayOfWeek(DateOnly date) => Cap(date.ToString("ddd", Cs));

    /// <summary>"Po 12. 10.".</summary>
    static string ShortDate(DateOnly date) => Cap(date.ToString("ddd d. M.", Cs));

    static string ShortDate(DateTime time) => Cap(time.ToString("ddd d. M.", Cs));

    /// <summary>When the next precipitation is due, in running text: "zítra", "ve středu", "ve středu 21. 10.".</summary>
    static string OnDay(DateOnly date, DateTime now)
    {
        int ahead = date.DayNumber - DateOnly.FromDateTime(now).DayNumber;
        if (ahead == 1) return "zítra";
        string day = date.DayOfWeek switch
        {
            System.DayOfWeek.Monday => "v pondělí",
            System.DayOfWeek.Tuesday => "v úterý",
            System.DayOfWeek.Wednesday => "ve středu",
            System.DayOfWeek.Thursday => "ve čtvrtek",
            System.DayOfWeek.Friday => "v pátek",
            System.DayOfWeek.Saturday => "v sobotu",
            _ => "v neděli",
        };
        return ahead <= 6 ? day : $"{day} {date.ToString("d. M.", Cs)}";
    }

    /// <summary>"Dnes", "Zítra · 13. 10.", "St · 14. 10.".</summary>
    static string DayName(DateTime time, DateTime now) => (time.Date - now.Date).Days switch
    {
        <= 0 => "Dnes",
        1 => $"Zítra · {time.ToString("d. M.", Cs)}",
        _ => $"{Cap(time.ToString("ddd", Cs))} · {time.ToString("d. M.", Cs)}",
    };

    // Wind directions say where it comes from; arrows show where it goes.
    static int DownWind(int fromDegrees) => ((fromDegrees + 180) % 360 + 360) % 360;

    static int Octant(int degrees) => (int)Math.Round(((degrees % 360) + 360) % 360 / 45.0) % 8;

    static string Compass(int degrees) =>
        new[] { "S", "SV", "V", "JV", "J", "JZ", "Z", "SZ" }[Octant(degrees)];

    /// <summary>Where the wind comes from, in running text: "od západu".</summary>
    static string CompassFrom(int degrees) =>
        new[] { "od severu", "od severovýchodu", "od východu", "od jihovýchodu", "od jihu", "od jihozápadu", "od západu", "od severozápadu" }[Octant(degrees)];

    /// <summary>The source's tendency when it has one, else sea-level pressure now compared with three hours ahead.</summary>
    /// <returns>+1 rising, 0 steady, -1 falling; null when unknown.</returns>
    static int? PressureTrend(CurrentConditions cur, IReadOnlyList<HourForecast> hours)
    {
        if (cur.PressureTendency is { } tendency) return Math.Sign(tendency);
        if (hours.Count < 4) return null;
        double change = hours[3].Pressure - hours[0].Pressure;
        return change > 1 ? 1 : change < -1 ? -1 : 0;
    }

    static string PressureText(int? trend) => trend switch { > 0 => "Stoupá", < 0 => "Klesá", 0 => "Stálý", _ => "" };

    /// <summary>
    /// CSS colour for a temperature in °C, interpolating the hue between <see cref="TempHues"/> stops.
    /// The lightness comes from the theme (--temp-l), so the same value works in light and dark mode.
    /// </summary>
    static string TemperatureColor(double celsius)
    {
        double x = Math.Clamp(celsius, TempHues[0].Temp, TempHues[^1].Temp);
        int i = 0;
        while (i < TempHues.Length - 2 && x > TempHues[i + 1].Temp) i++;
        var (a, hueA) = TempHues[i];
        var (b, hueB) = TempHues[i + 1];
        double hue = hueA + (hueB - hueA) * (x - a) / (b - a);
        return $"oklch(var(--temp-l) 0.14 {Round(hue)})";
    }

    /// <summary>
    /// CSS colour for a precipitation bar: hue per type, more saturated as intensity rises. Lightness moves with
    /// intensity along the theme's --precip-l0 + --precip-l1 × f line: darker on light paper, brighter on dark.
    /// </summary>
    static string PrecipColor(double mmPerHour, PrecipType type)
    {
        var (hue, chroma) = type switch
        {
            PrecipType.Rain => (252, 0.17),
            PrecipType.IceRain => (200, 0.12),
            PrecipType.Sleet => (165, 0.11),
            _ => (295, 0.12),
        };
        double f = Math.Min(1, Math.Sqrt(mmPerHour / 6));
        return $"oklch(calc(var(--precip-l0) + var(--precip-l1) * {F3(f)}) {F3(chroma * (0.45 + 0.55 * f))} {hue})";
    }

    /// <summary>Groups consecutive wet minutes of the same precipitation type.</summary>
    static List<(PrecipType Type, int Start, int End)> Segments(IReadOnlyList<MinutePrecip> mins, List<int> wet)
    {
        var segments = new List<(PrecipType Type, int Start, int End)>();
        foreach (int i in wet)
        {
            if (segments.Count > 0 && segments[^1].Type == mins[i].Type && segments[^1].End == i - 1)
                segments[^1] = segments[^1] with { End = i };
            else
                segments.Add((mins[i].Type, i, i));
        }
        return segments;
    }

    /// <summary>Below this much new snow (cm) the view doesn't mention it.</summary>
    public const double MinSnowCm = 0.05;

    /// <summary>New snow over the minutes (cm), e.g. for the "New snow" figure.</summary>
    public static double NewSnowCm(IReadOnlyList<MinutePrecip> mins) => mins.Sum(m => m.SnowCmPerHour) / 60;

    /// <summary>"0,4 cm", "3 cm".</summary>
    public static string SnowAmount(double cm) => cm < 1 ? $"{D1(cm)} cm" : $"{Round(cm)} cm";

    /// <summary>Snow as the rate it builds up (cm/h); everything else as water (mm/h).</summary>
    static string Rate(MinutePrecip m) =>
        m.Type == PrecipType.Snow && m.SnowCmPerHour > 0 ? $"{D1(m.SnowCmPerHour)} cm/h sněhu" : $"{D1(m.Intensity)} mm/h";

    /// <summary>What falls from minute <paramref name="start"/> to <paramref name="end"/>: new snow in cm for snow, else water in mm.</summary>
    static string Amount(IReadOnlyList<MinutePrecip> mins, int start, int end)
    {
        var part = mins.Skip(start).Take(end - start + 1).ToList();
        double snow = NewSnowCm(part);
        return part[0].Type == PrecipType.Snow && snow >= MinSnowCm ? $"≈ {SnowAmount(snow)}" : $"{D1(part.Sum(m => m.Intensity) / 60)} mm";
    }

    /// <summary>"7 min", "1 h 5 min".</summary>
    static string Duration(int minutes) =>
        minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";

    /// <summary>
    /// The minutes as chart bars, merging neighbours when there are more than 60: each bar is the average
    /// intensity, with the type of its wettest minute.
    /// </summary>
    static List<MinutePrecip> BarMinutes(IReadOnlyList<MinutePrecip> mins)
    {
        int per = Math.Max(1, (int)Math.Ceiling(mins.Count / 60.0));
        if (per == 1) return [.. mins];
        return mins.Chunk(per)
            .Select(c => new MinutePrecip(c.Average(m => m.Intensity), c.MaxBy(m => m.Intensity)!.Type, c.Average(m => m.SnowCmPerHour),
                c.MaxBy(m => m.Intensity)!.Dbz))
            .ToList();
    }

    /// <summary>Dry breaks shorter than this (minutes) don't split precipitation into separate sections, here and in alerts.</summary>
    public const int MergeGapMinutes = 10;

    /// <summary>Above this (mm/h, or mm in an hour) a minute or hour counts as wet: in the charts and for alerts.</summary>
    public const double WetMmPerHour = 0.05;

    static bool IsWet(MinutePrecip minute) => minute.Intensity > WetMmPerHour;

    // "Slabý déšť": the intensity agrees with every kind, as all are masculine nouns.
    static string KindOf(MinutePrecip m) => $"{Category(m.Intensity)} {Lower(PrecipName(m.Type))}";

    /// <summary>
    /// Radar reflectivity: as measured when the source has radar, else estimated from the rate with the same relations
    /// Azure Maps' radar is converted by (Marshall–Palmer Z = 200 R^1.6 for rain and ice, Z = 2000 R^2 for snow).
    /// </summary>
    static string Reflectivity(MinutePrecip m)
    {
        if (m.Dbz is { } measured) return measured > 0 ? $"{Round(measured)} dBZ" : "–";
        if (!IsWet(m)) return "–";
        return $"≈ {Round(EstimatedDbz(m))} dBZ (odhad)";
    }

    static double EstimatedDbz(MinutePrecip m) =>
        10 * Math.Log10(m.Type == PrecipType.Snow ? 2000 * m.Intensity * m.Intensity : 200 * Math.Pow(m.Intensity, 1.6));

    /// <summary>The reflectivity (measured, else estimated) as a fraction of <see cref="MaxDbz"/>, 0 to 1.</summary>
    static double DbzScale(MinutePrecip m) =>
        IsWet(m) ? Math.Clamp((m.Dbz ?? EstimatedDbz(m)) / MaxDbz, 0, 1) : 0;

    static bool IsWet(HourForecast hour) => hour.Precipitation > WetMmPerHour;

    /// <summary>
    /// The intensity as an adjective agreeing with the (masculine) <see cref="PrecipName"/> nouns: "Slabý" + "déšť";
    /// "Sucho" when dry.
    /// </summary>
    public static string Category(double mmPerHour) =>
        mmPerHour <= 0.05 ? "Sucho" : mmPerHour < 1 ? "Slabý" : mmPerHour < 4 ? "Mírný" : "Silný";

    static string UvText(double uv) => uv < 3 ? "Nízký" : uv < 6 ? "Střední" : uv < 8 ? "Vysoký" : "Velmi vysoký";

    // Halves round up, as in the design prototype.
    static int Round(double x) => (int)Math.Floor(x + 0.5);

    /// <summary>One decimal for SVG path data and coordinates (invariant); see <see cref="D1"/> for display.</summary>
    static string F1(double x) => x.ToString("0.0", Inv);

    /// <summary>One decimal for the user, with a decimal comma: "1,5".</summary>
    static string D1(double x) => Czech.Number(x, "0.0");

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Cs) + s[1..];

    static string Lower(string s) => s.ToLower(Cs);

    static string Upper(string s) => s.ToUpper(Cs);

    static string F3(double x) => x.ToString("0.000", Inv);

    static string Pct(double fraction) => (fraction * 100).ToString("0.0", Inv);

    static double NonZero(double x) => x == 0 ? 1 : x;

    static int IndexOfMax(IReadOnlyList<MinutePrecip> minutes)
    {
        int best = 0;
        for (int i = 1; i < minutes.Count; i++)
            if (minutes[i].Intensity > minutes[best].Intensity) best = i;
        return best;
    }

    // Catmull-Rom spline through the points, as cubic Bézier segments.
    static string Smooth(IReadOnlyList<(int X, double Y)> p)
    {
        var sb = new StringBuilder($"M{p[0].X},{F1(p[0].Y)}");
        for (int i = 0; i < p.Count - 1; i++)
        {
            var p0 = i > 0 ? p[i - 1] : p[i];
            var p1 = p[i];
            var p2 = p[i + 1];
            var p3 = i + 2 < p.Count ? p[i + 2] : p2;
            sb.Append($" C{F1(p1.X + (p2.X - p0.X) / 6.0)},{F1(p1.Y + (p2.Y - p0.Y) / 6)}")
              .Append($" {F1(p2.X - (p3.X - p1.X) / 6.0)},{F1(p2.Y - (p3.Y - p1.Y) / 6)}")
              .Append($" {p2.X},{F1(p2.Y)}");
        }
        return sb.ToString();
    }
}
