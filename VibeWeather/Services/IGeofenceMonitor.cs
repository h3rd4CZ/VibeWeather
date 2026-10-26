using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>The platform's geofencing service and the permissions it needs.</summary>
public interface IGeofenceMonitor
{
    /// <summary>Precise location while the app is in use, which background access builds on.</summary>
    Task<bool> RequestForegroundPermissionAsync();

    /// <summary>Whether the app may use location in the background ("Allow all the time").</summary>
    Task<bool> HasBackgroundPermissionAsync();

    /// <summary>Asks for background location. On Android 11+ this opens the app's location permission screen.</summary>
    Task<bool> RequestBackgroundPermissionAsync();

    /// <summary>Asks to show notifications (needed on Android 13+). Geofences work without it, just silently.</summary>
    Task RequestNotificationPermissionAsync();

    void OpenAppSettings();

    /// <summary>Starts watching the given geofences, replacing any with the same id.</summary>
    Task RegisterAsync(IReadOnlyList<GeofencePlace> geofences);

    Task UnregisterAsync(IReadOnlyList<string> ids);
}

/// <summary>A geofence couldn't be set up; Message is suitable for showing to the user.</summary>
public sealed class GeofenceException(string message, Exception? inner = null) : Exception(message, inner);
