using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>
/// The temperature unit, wind speed unit and time format chosen in Settings, remembered between launches.
/// <see cref="Changed"/> fires on a new choice.
/// </summary>
public sealed class DisplaySettings
{
    const string TemperaturePreference = "units_temperature";
    const string WindPreference = "units_wind";
    const string ClockPreference = "time_format";

    public event Action? Changed;

    public DisplayUnits Units { get; private set; } = new(
        Load(TemperaturePreference, DisplayUnits.Default.Temperature),
        Load(WindPreference, DisplayUnits.Default.Wind),
        Load(ClockPreference, DisplayUnits.Default.Clock));

    public void SetTemperature(TemperatureUnit unit) => Set(Units with { Temperature = unit });

    public void SetWind(WindSpeedUnit unit) => Set(Units with { Wind = unit });

    public void SetClock(TimeFormat format) => Set(Units with { Clock = format });

    void Set(DisplayUnits units)
    {
        if (units == Units) return;
        Units = units;
        Preferences.Default.Set(TemperaturePreference, units.Temperature.ToString());
        Preferences.Default.Set(WindPreference, units.Wind.ToString());
        Preferences.Default.Set(ClockPreference, units.Clock.ToString());
        Changed?.Invoke();
    }

    static T Load<T>(string preference, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(Preferences.Default.Get(preference, ""), out var value) ? value : fallback;
}
