namespace VibeWeather.Services;

/// <summary>
/// The platform's own time picker dialog. Used instead of an HTML time input, which some WebView versions
/// (e.g. 154 on Samsung) don't open.
/// </summary>
public interface ITimeDialog
{
    /// <summary>The chosen time, or null when the dialog was dismissed.</summary>
    Task<TimeOnly?> PickAsync(TimeOnly initial, string title);
}
