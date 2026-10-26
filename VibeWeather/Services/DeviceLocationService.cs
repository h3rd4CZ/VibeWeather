using Microsoft.Extensions.Logging;
using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>The device's position, named with the platform geocoder (Open-Meteo has no reverse geocoding).</summary>
public sealed class DeviceLocationService(IGeofenceMonitor permissions, ILogger<DeviceLocationService> logger)
{
    /// <summary>The name when the geocoder doesn't know the place.</summary>
    public const string UnnamedPlace = "Aktuální poloha";

    static readonly TimeSpan ForegroundFixTimeout = TimeSpan.FromSeconds(10);
    // Alarm receivers get about 10 seconds in all; the forecast download needs the rest.
    static readonly TimeSpan BackgroundFixTimeout = TimeSpan.FromSeconds(3);
    // A new fix this close to the last named position keeps that name, saving a geocoder call.
    const double SameNameKm = 1;

    /// <summary>
    /// The position with the time of its fix: the platform's last known fix when younger than
    /// <paramref name="maxAge"/>, else a new one (or that older fix when none comes). Null when location
    /// is unavailable or not allowed. In the <paramref name="background"/> it needs "Povolit vždy" and never prompts;
    /// in the foreground <paramref name="requestPermission"/> lets it show the permission prompt.
    /// <paramref name="named"/> is the last position, whose name is kept when the new one is near it. A last known fix
    /// not after <paramref name="newerThan"/> doesn't count (the device has moved since), so a new one is asked for.
    /// </summary>
    public async Task<DevicePosition?> GetCurrentAsync(bool background, bool requestPermission, WeatherLocation? named,
        TimeSpan maxAge, DateTimeOffset? newerThan = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await HasPermissionAsync(background, requestPermission)) return null;

            var position = await Geolocation.Default.GetLastKnownLocationAsync();
            if (position is null || DateTimeOffset.UtcNow - position.Timestamp >= maxAge
                || position.Timestamp <= newerThan)
            {
                var timeout = background ? BackgroundFixTimeout : ForegroundFixTimeout;
                position = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Medium, timeout), cancellationToken)
                    ?? position;
            }
            if (position is null) return null;

            bool near = named is not null && named.Name != UnnamedPlace
                && Location.CalculateDistance(named.Latitude, named.Longitude, position.Latitude, position.Longitude,
                    DistanceUnits.Kilometers) < SameNameKm;
            var (name, region) = near ? (named!.Name, named.Region) : await NameAsync(position.Latitude, position.Longitude);
            return new DevicePosition(
                new WeatherLocation(name, region, position.Latitude, position.Longitude, IsDevice: true), position.Timestamp);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException)
        {
            logger.LogInformation(ex, "Device location unavailable");
            return null;
        }
    }

    /// <summary>Prague comes as its region, "Hlavní město Praha"; the city is "Praha".</summary>
    public static string? CityName(string? name)
    {
        const string Capital = "Hlavní město ";
        return name?.StartsWith(Capital, StringComparison.Ordinal) == true ? name[Capital.Length..] : name;
    }

    async Task<bool> HasPermissionAsync(bool background, bool request)
    {
        if (background) return await permissions.HasBackgroundPermissionAsync();
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted && request)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            return status == PermissionStatus.Granted;
        });
    }

    async Task<(string Name, string Region)> NameAsync(double latitude, double longitude)
    {
        try
        {
#if ANDROID
            var place = await CzechPlacemarkAsync(latitude, longitude);
#else
            var place = (await Geocoding.Default.GetPlacemarksAsync(latitude, longitude))?.FirstOrDefault();
#endif
            var name = CityName(place?.Locality ?? place?.SubAdminArea ?? place?.AdminArea);
            if (!string.IsNullOrWhiteSpace(name))
                return (name, place?.CountryName ?? "");
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Reverse geocoding failed");
        }
        return (UnnamedPlace, "");
    }

#if ANDROID
    /// <summary>
    /// Android's geocoder asked for Czech names ("Praha", "Česko") whatever the phone's language. MAUI's Geocoding
    /// always uses the device language.
    /// </summary>
    static Task<global::Android.Locations.Address?> CzechPlacemarkAsync(double latitude, double longitude) =>
        Task.Run(() =>
        {
            if (!global::Android.Locations.Geocoder.IsPresent) return null;
            var geocoder = new global::Android.Locations.Geocoder(Platform.AppContext, Java.Util.Locale.ForLanguageTag("cs-CZ"));
            // The listener variant (API 33+) does the same off the main thread; this one blocks, hence Task.Run.
#pragma warning disable CS0618, CA1422
            return geocoder.GetFromLocation(latitude, longitude, 1)?.FirstOrDefault();
#pragma warning restore CS0618, CA1422
        });
#endif
}
