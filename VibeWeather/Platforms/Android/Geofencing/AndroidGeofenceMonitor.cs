using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Common.Apis;
using Android.Gms.Location;
using Android.OS;
using AndroidX.Core.Content;
using VibeWeather.Data;
using VibeWeather.Services;

namespace VibeWeather.Platforms.Android.Geofencing;

/// <summary>Geofences through Google Play services' GeofencingClient.</summary>
public sealed class AndroidGeofenceMonitor : IGeofenceMonitor
{
    static Context Context => Platform.AppContext;

    public async Task<bool> RequestForegroundPermissionAsync()
    {
        var status = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var current = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            return current == PermissionStatus.Granted ? current : await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        });
        return status == PermissionStatus.Granted;
    }

    public async Task<bool> HasBackgroundPermissionAsync() =>
        await Permissions.CheckStatusAsync<BackgroundLocationPermission>() == PermissionStatus.Granted;

    public async Task<bool> RequestBackgroundPermissionAsync()
    {
        var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<BackgroundLocationPermission>);
        return status == PermissionStatus.Granted;
    }

    public async Task RequestNotificationPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        });
    }

    public void OpenAppSettings() => AppInfo.Current.ShowSettingsUI();

    public async Task RegisterAsync(IReadOnlyList<GeofencePlace> geofences)
    {
        if (geofences.Count == 0) return;
        if (ContextCompat.CheckSelfPermission(Context, Manifest.Permission.AccessFineLocation) != Permission.Granted)
            throw new GeofenceException("Zóny potřebují přesnou polohu. Zapněte pro tuto aplikaci „Používat přesnou polohu“.");

        var fences = geofences.Select(g => new GeofenceBuilder()
            .SetRequestId(g.Id)
            .SetCircularRegion(g.Place.Latitude, g.Place.Longitude, g.RadiusMeters)
            .SetExpirationDuration(Geofence.NeverExpire)
            .SetTransitionTypes(Geofence.GeofenceTransitionEnter | Geofence.GeofenceTransitionExit)
            .Build()).ToList();

        // Initial "enter" so a fence created while already inside it shows up straight away.
        var request = new GeofencingRequest.Builder()
            .SetInitialTrigger(GeofencingRequest.InitialTriggerEnter)
            .AddGeofences(fences)
            .Build();

        try
        {
            await LocationServices.GetGeofencingClient(Context).AddGeofencesAsync(request, GeofenceBroadcastReceiver.CreatePendingIntent(Context));
        }
        catch (ApiException ex)
        {
            throw new GeofenceException(Describe(ex.StatusCode), ex);
        }
    }

    public async Task UnregisterAsync(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return;
        try
        {
            await LocationServices.GetGeofencingClient(Context).RemoveGeofencesAsync([.. ids]);
        }
        catch (ApiException ex)
        {
            throw new GeofenceException(Describe(ex.StatusCode), ex);
        }
    }

    static string Describe(int statusCode) => statusCode switch
    {
        GeofenceStatusCodes.GeofenceNotAvailable =>
            "Zóny teď nejsou k dispozici. Zkontrolujte, že je zapnutá poloha a Přesnost určování polohy Google.",
        GeofenceStatusCodes.GeofenceTooManyGeofences => "Android povoluje nejvýše 100 zón na aplikaci.",
        GeofenceStatusCodes.GeofenceInsufficientLocationPermission =>
            "Zóny potřebují přístup k přesné poloze s volbou „Povolit vždy“.",
        GeofenceStatusCodes.GeofenceRequestTooFrequent => "Příliš mnoho změn zón najednou. Zkuste to prosím za chvíli.",
        _ => $"Zónu se nepodařilo nastavit ({GeofenceStatusCodes.GetStatusCodeString(statusCode)}).",
    };
}

/// <summary>"Allow all the time" location. Before Android 10 it comes with the normal location permission.</summary>
public sealed class BackgroundLocationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(29)
            ? [(Manifest.Permission.AccessBackgroundLocation, true)]
            : [(Manifest.Permission.AccessFineLocation, true)];
}
