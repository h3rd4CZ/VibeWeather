using VibeWeather.Data;

namespace VibeWeather.Services;

/// <summary>A device position and when it was fixed.</summary>
public sealed record DevicePosition(WeatherLocation Location, DateTimeOffset At);

/// <summary>A location to load. <paramref name="StaleSince"/> is the fix time of a device position too old to count as current.</summary>
public sealed record ResolvedLocation(WeatherLocation Location, DateTimeOffset? StaleSince);

/// <summary>
/// The rules of <see cref="LocationSelection"/> without storage or platform calls.
/// Spec: docs/superpowers/specs/2026-10-16-location-selection-design.md.
/// </summary>
public static class SelectionRules
{
    /// <summary>How long a device position is used without asking for a new one, until changed in Settings.</summary>
    public static readonly TimeSpan DefaultMaxPositionAge = TimeSpan.FromHours(1);

    /// <summary>The durations Settings offers for <see cref="DefaultMaxPositionAge"/>.</summary>
    public static readonly IReadOnlyList<TimeSpan> PositionAgeChoices =
        [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2)];

    /// <summary>"Aktuální poloha" as a selection, independent of any position.</summary>
    public static readonly WeatherLocation Device = new("Aktuální poloha", "", 0, 0, IsDevice: true);

    /// <summary>Rule 1: of the geofences just entered, the smallest takes over; null when none was entered.</summary>
    public static string? OverrideAfterEnter(IEnumerable<GeofencePlace> entered) =>
        entered.MinBy(g => g.RadiusMeters)?.Id;

    /// <summary>
    /// Rule 2: the override after the inside states changed. It stays while the device is inside it; once it's left
    /// (or removed), the smallest geofence still inside takes over, or none. No override stays none.
    /// </summary>
    public static string? OverrideAfterChange(string? overrideId, IReadOnlyCollection<GeofencePlace> inside)
    {
        if (overrideId is null || inside.Any(g => g.Id == overrideId)) return overrideId;
        return inside.MinBy(g => g.RadiusMeters)?.Id;
    }

    /// <summary>The location in effect: the override geofence's place, else the base (the position for "Aktuální poloha", null while unknown).</summary>
    public static WeatherLocation? Effective(WeatherLocation baseSelection, GeofencePlace? fence, DevicePosition? position) =>
        fence?.Place ?? (baseSelection.IsDevice ? position?.Location : baseSelection);

    public static bool IsFresh(DevicePosition position, DateTimeOffset now, TimeSpan maxAge) => now - position.At < maxAge;

    /// <summary>The newer of the stored and a just found position.</summary>
    public static DevicePosition? Newer(DevicePosition? stored, DevicePosition? found) =>
        found is not null && (stored is null || found.At > stored.At) ? found : stored;

    /// <summary>
    /// Rule 7: whether to ask for a new position: none stored, or older than <paramref name="maxAge"/>, or
    /// <paramref name="outdated"/> because the device entered or left a geofence since, so it has moved whatever the age.
    /// </summary>
    public static bool NeedsLookup(DevicePosition? stored, DateTimeOffset now, TimeSpan maxAge, bool outdated) =>
        stored is null || outdated || !IsFresh(stored, now, maxAge);

    /// <summary>
    /// Rule 7: the position to use, stale when older than <paramref name="maxAge"/> or <paramref name="outdated"/> (no
    /// new one came after a geofence move); null when no position was ever known.
    /// </summary>
    public static ResolvedLocation? Resolve(DevicePosition? position, DateTimeOffset now, TimeSpan maxAge, bool outdated = false) =>
        position is null ? null : new(position.Location, IsFresh(position, now, maxAge) && !outdated ? null : position.At);

    /// <summary>
    /// Rule 6: base selection and override from the single key stored before. A saved city's key keeps that city,
    /// anything else becomes "Aktuální poloha"; a geofence at that key the device is inside becomes the override.
    /// </summary>
    public static (WeatherLocation Base, string? OverrideId) Migrate(string? legacyKey, IReadOnlyList<WeatherLocation> cities,
        IReadOnlyCollection<GeofencePlace> inside)
    {
        var city = cities.FirstOrDefault(c => !c.IsDevice && c.Key == legacyKey);
        var fence = inside.Where(g => g.Place.Key == legacyKey).MinBy(g => g.RadiusMeters);
        return (city ?? Device, fence?.Id);
    }
}
