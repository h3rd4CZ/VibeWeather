namespace VibeWeather.Services;

/// <summary>
/// Where the page sits relative to the screen, for the start screen in index.html: Android's splash draws the sun at the
/// screen's centre, the page's centre is <see cref="ShiftDp"/> lower (half the top system bar less the bottom one).
/// Set by the platform once it knows the window insets; remembered by SplashHandoff for the next start.
/// </summary>
public static class SplashGeometry
{
    public static double ShiftDp { get; set; }
}
