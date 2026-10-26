using System.Globalization;

namespace VibeWeather.Data;

public enum TemperatureUnit { Celsius, Fahrenheit }

public enum WindSpeedUnit { KilometersPerHour, MilesPerHour, MetersPerSecond }

public enum TimeFormat { TwelveHour, TwentyFourHour }

/// <summary>
/// How values are shown. Forecasts always hold °C and km/h (see <see cref="Forecast"/>); these convert them for display.
/// Times are Czech: 24-hour by default, or 12-hour with "dop."/"odp.".
/// </summary>
public sealed record DisplayUnits(TemperatureUnit Temperature, WindSpeedUnit Wind, TimeFormat Clock)
{
    public static readonly DisplayUnits Default = new(TemperatureUnit.Celsius, WindSpeedUnit.KilometersPerHour, TimeFormat.TwentyFourHour);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly CultureInfo Cs = Czech.Culture;

    public bool Is24Hour => Clock == TimeFormat.TwentyFourHour;

    /// <summary>"12°": rounded, in the chosen unit; the app never spells out C or F.</summary>
    public string Temp(double celsius) => double.IsNaN(celsius) ? "–" : $"{TempValue(celsius)}°";

    /// <summary>A temperature as a whole number in the chosen unit.</summary>
    public int TempValue(double celsius) =>
        Round(Temperature == TemperatureUnit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius);

    /// <summary>A wind speed as a whole number in <see cref="WindUnit"/>.</summary>
    public int WindValue(double kmh) => Round(Wind switch
    {
        WindSpeedUnit.MilesPerHour => kmh / 1.609344,
        WindSpeedUnit.MetersPerSecond => kmh / 3.6,
        _ => kmh,
    });

    public string WindUnit => Wind switch
    {
        WindSpeedUnit.MilesPerHour => "mph",
        WindSpeedUnit.MetersPerSecond => "m/s",
        _ => "km/h",
    };

    /// <summary>"12 km/h".</summary>
    public string WindSpeed(double kmh) => $"{WindValue(kmh)} {WindUnit}";

    /// <summary>"5:28 odp." or "17:28".</summary>
    public string Time(DateTime time) => Is24Hour ? time.ToString("HH:mm", Inv) : time.ToString("h:mm tt", Cs);

    public string Time(TimeOnly time) => Is24Hour ? time.ToString("HH:mm", Inv) : time.ToString("h:mm tt", Cs);

    /// <summary>"5:28:09 odp." or "17:28:09".</summary>
    public string TimeWithSeconds(DateTime time) => Is24Hour ? time.ToString("HH:mm:ss", Inv) : time.ToString("h:mm:ss tt", Cs);

    /// <summary>A whole hour: "5 odp." or "17:00".</summary>
    public string Hour(DateTime time) => Is24Hour ? time.ToString("HH:mm", Inv) : time.ToString("h tt", Cs);

    /// <summary>Without AM/PM, for tight chart labels: "5:28" or "17:28".</summary>
    public string ShortTime(DateTime time) => time.ToString(Is24Hour ? "HH:mm" : "h:mm", Inv);

    // Halves round up, as in the design prototype.
    static int Round(double x) => (int)Math.Floor(x + 0.5);
}
