using Android.App;
using Android.Content;
using Android.Gms.Location;
using Microsoft.Extensions.Logging;
using VibeWeather.Data;
using VibeWeather.Services;
using VibeWeather.Services.Diagnostics;

namespace VibeWeather.Platforms.Android.Geofencing;

/// <summary>Receives geofence transitions from Play services, also when the app isn't open.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class GeofenceBroadcastReceiver : BroadcastReceiver
{
    /// <summary>The intent Play services fires for transitions. Must be mutable so it can attach the event.</summary>
    public static PendingIntent CreatePendingIntent(Context context) =>
        PendingIntent.GetBroadcast(
            context, 0, new Intent(context, typeof(GeofenceBroadcastReceiver)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        var geofencingEvent = GeofencingEvent.FromIntent(intent);
        // MauiApplication builds the service container on process start, including starts for a broadcast.
        var services = IPlatformApplication.Current?.Services;
        var geofences = services?.GetService<GeofenceManager>();
        if (geofencingEvent is null || geofences is null) return;

        if (geofencingEvent.HasError)
        {
            services!.GetService<ILogger<GeofenceBroadcastReceiver>>()?.LogWarning(
                "Geofencing error {Code}", GeofenceStatusCodes.GetStatusCodeString(geofencingEvent.ErrorCode));
            // Location was turned off; fences must be registered again, which happens on the next app start.
            if (geofencingEvent.ErrorCode == GeofenceStatusCodes.GeofenceNotAvailable)
                geofences.ForgetInside();
            return;
        }

        bool entered = geofencingEvent.GeofenceTransition == Geofence.GeofenceTransitionEnter;
        if (!entered && geofencingEvent.GeofenceTransition != Geofence.GeofenceTransitionExit) return;

        var ids = geofencingEvent.TriggeringGeofences?.Select(g => g.RequestId).OfType<string>() ?? [];
        var transition = geofences.ApplyTransition(ids, entered);
        // The smallest geofence entered becomes the location for the app and all alarms, also while the app is closed.
        // Leaving it is handled by LocationSelection itself, through GeofenceManager.Changed.
        services!.GetService<LocationSelection>()?.OnFencesEntered(transition.Entered);
        var arrivals = transition.Announce;
        if (arrivals.Count == 0) return;

        // Fetch each arrival's weather for the notification; keep the process alive until it's posted.
        var appContext = context.ApplicationContext ?? context;
        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                foreach (var place in arrivals)
                    GeofenceNotifications.ShowArrival(appContext, place, await LoadWeatherAsync(services!, place));
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    // Broadcast receivers get about 10 seconds; leave room to post the notification without weather.
    static readonly TimeSpan WeatherTimeout = TimeSpan.FromSeconds(8);

    static async Task<ForecastView?> LoadWeatherAsync(IServiceProvider services, GeofencePlace place)
    {
        var provider = services.GetService<IWeatherProvider>();
        if (provider is null) return null;
        try
        {
            using var timeout = new CancellationTokenSource(WeatherTimeout);
            var units = services.GetService<DisplaySettings>()?.Units ?? DisplayUnits.Default;
            return new ForecastView(await provider.GetForecastAsync(place.Place, timeout.Token), units);
        }
        catch (Exception ex) when (ex is WeatherProviderException or OperationCanceledException)
        {
            // No popup from the background; the notification still goes out, just without weather.
            services.GetService<ErrorReporter>()?.Report(ex, $"Načítání počasí po příjezdu na místo {place.Place.Name}", popup: false);
            return null;
        }
    }
}
