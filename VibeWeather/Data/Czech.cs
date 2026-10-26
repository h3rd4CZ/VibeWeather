using System.Globalization;

namespace VibeWeather.Data;

/// <summary>
/// The app's language is Czech: dates, numbers and plurals for user-visible text. Numbers that end up in CSS, SVG or
/// URLs keep the invariant culture.
/// </summary>
public static class Czech
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("cs-CZ");

    /// <summary>The noun form for <paramref name="n"/>: 1 záznam, 2–4 záznamy, 0 or 5+ záznamů.</summary>
    public static string Plural(long n, string one, string few, string many) =>
        Math.Abs(n) switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };

    /// <summary>"3 záznamy": the number with its noun form.</summary>
    public static string Count(long n, string one, string few, string many) => $"{n} {Plural(n, one, few, many)}";

    /// <summary>A number for display, with a decimal comma: "1,5".</summary>
    public static string Number(double x, string format = "0.0") => x.ToString(format, Culture);
}
