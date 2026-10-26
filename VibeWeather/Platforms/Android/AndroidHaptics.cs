using Android.Content;
using Android.OS;
using VibeWeather.Services;

namespace VibeWeather.Platforms.Android;

/// <summary>
/// Ticks through the vibrator at an amplitude matching the strength. Vibrators without amplitude control (and
/// Android before 8) get a longer tick for a stronger one instead.
/// </summary>
public sealed class AndroidHaptics : IHaptics
{
    const int MinMs = 8;
    const int MaxMs = 40;
    // Weaker than this can't be felt on most phones.
    const int MinAmplitude = 30;

    readonly Vibrator? vibrator = FindVibrator();

    public void Tick(double strength)
    {
        if (vibrator is null || !vibrator.HasVibrator || strength <= 0) return;
        strength = Math.Min(1, strength);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var effect = vibrator.HasAmplitudeControl
                ? VibrationEffect.CreateOneShot(20, (int)Math.Round(MinAmplitude + (255 - MinAmplitude) * strength))
                : VibrationEffect.CreateOneShot(Duration(strength), VibrationEffect.DefaultAmplitude);
            vibrator.Vibrate(effect);
        }
        else
        {
#pragma warning disable CA1422 // The only way before Android 8.
            vibrator.Vibrate(Duration(strength));
#pragma warning restore CA1422
        }
    }

    static long Duration(double strength) => (long)Math.Round(MinMs + (MaxMs - MinMs) * strength);

    static Vibrator? FindVibrator()
    {
        var context = global::Android.App.Application.Context;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            return (context.GetSystemService(Context.VibratorManagerService) as VibratorManager)?.DefaultVibrator;
#pragma warning disable CA1422 // Replaced by VibratorManager on Android 12.
        return context.GetSystemService(Context.VibratorService) as Vibrator;
#pragma warning restore CA1422
    }
}
