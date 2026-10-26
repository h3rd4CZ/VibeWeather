namespace VibeWeather.Services;

/// <summary>Ends the app at once: no fade-out, no cleanup. Scheduled checks still run, as Android starts the app for them.</summary>
public static class AppExit
{
    public static void Now()
    {
#if ANDROID
        // Out of the recent apps, then the process ends without waiting for the activity to finish.
        Platform.CurrentActivity?.FinishAndRemoveTask();
        Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#else
        Application.Current?.Quit();
#endif
    }
}
