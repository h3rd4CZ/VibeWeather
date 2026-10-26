using VibeWeather.Data;

namespace VibeWeather.Services.SevereWeather;

/// <summary>Czech texts about a warning, shared by notifications and pages.</summary>
public static class WarningText
{
    /// <summary>
    /// When it applies, in the place's clock: "do 21:00" once it has started, otherwise "od 15:00 do čt 08:00".
    /// A time on another day than today gets its weekday.
    /// </summary>
    public static string Period(SevereWarning warning, DisplayUnits units, DateTimeOffset now)
    {
        string When(DateTimeOffset t)
        {
            var today = now.ToOffset(t.Offset).Date;
            var time = units.Time(t.DateTime);
            return t.Date == today ? time : $"{t.DateTime.ToString("ddd", Czech.Culture)} {time}";
        }
        return warning.Start <= now ? $"do {When(warning.End)}" : $"od {When(warning.Start)} do {When(warning.End)}";
    }

    /// <summary>The source's status keyword in Czech, for the detail page.</summary>
    public static string Status(string status) => status switch
    {
        "New" => "Nová",
        "Update" => "Aktualizovaná",
        "Upgrade" => "Zvýšená",
        "Extend" => "Prodloužená",
        "Correct" => "Opravená",
        "Continue" => "Trvá",
        _ => "Platí",
    };
}
