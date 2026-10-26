namespace VibeWeather.Services;

public enum ThemeMode { Auto, Light, Dark }

/// <summary>
/// Appearance: follow the system (Auto, the default), or always Light or Dark. Remembered between launches.
/// Drives MAUI's app theme, so native parts (page background, system bar icons, Android dialogs like the
/// time picker) follow it too. <see cref="Changed"/> fires on a new choice and, in Auto, when the system
/// switches between light and dark.
/// </summary>
public sealed class ThemeService
{
    const string ModePreference = "appearance_mode";
    // Earlier versions stored only light/dark as a bool; such a choice is kept.
    const string LegacyDarkPreference = "appearance_dark";

    Application? app;

    public event Action? Changed;

    public ThemeMode Mode { get; private set; } = LoadMode();

    /// <summary>Whether the app is dark right now, resolving Auto against the system.</summary>
    public bool IsDark => Mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        // With no user theme set, RequestedTheme is the system's.
        _ => (app ?? Application.Current)?.RequestedTheme == AppTheme.Dark,
    };

    /// <summary>CSS class for the app container.</summary>
    public string CssClass => IsDark ? "theme-dark" : "theme-light";

    /// <summary>Applies the stored mode to <paramref name="application"/> and follows system changes. Call once at startup.</summary>
    public void Attach(Application application)
    {
        app = application;
        Apply();
        application.RequestedThemeChanged += (_, _) =>
        {
            if (Mode == ThemeMode.Auto) Changed?.Invoke();
        };
    }

    public void SetMode(ThemeMode mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        Preferences.Default.Set(ModePreference, mode.ToString());
        MainThread.BeginInvokeOnMainThread(Apply);
        Changed?.Invoke();
    }

    void Apply()
    {
        if (app is null) return;
        app.UserAppTheme = Mode switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }

    static ThemeMode LoadMode()
    {
        if (Enum.TryParse<ThemeMode>(Preferences.Default.Get(ModePreference, ""), out var mode))
            return mode;
        if (Preferences.Default.ContainsKey(LegacyDarkPreference))
        {
            mode = Preferences.Default.Get(LegacyDarkPreference, false) ? ThemeMode.Dark : ThemeMode.Light;
            Preferences.Default.Set(ModePreference, mode.ToString());
            Preferences.Default.Remove(LegacyDarkPreference);
            return mode;
        }
        return ThemeMode.Auto;
    }
}
