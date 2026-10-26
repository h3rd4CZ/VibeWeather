namespace VibeWeather.Services;

/// <summary>Short vibrations as touch feedback.</summary>
public interface IHaptics
{
    /// <summary>One short tick; strength from 0 (none) to 1 (strongest).</summary>
    void Tick(double strength);
}
