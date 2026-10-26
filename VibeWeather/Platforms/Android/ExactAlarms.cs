using Android.App;
using Android.Content;
using Android.Provider;

namespace VibeWeather.Platforms.Android;

/// <summary>
/// AlarmManager alarms that also fire during Doze. With the "Alarms &amp; reminders" permission they're exact;
/// without it (the default on Android 14+) Android may deliver them late. Alarms don't survive a reboot; the boot
/// receiver schedules them again.
/// </summary>
internal static class ExactAlarms
{
    static Context Context => Platform.AppContext;

    static AlarmManager Alarms => (AlarmManager)Context.GetSystemService(Context.AlarmService)!;

    public static bool CanBeExact => !OperatingSystem.IsAndroidVersionAtLeast(31) || Alarms.CanScheduleExactAlarms();

    public static void Set(DateTime at, PendingIntent intent)
    {
        var triggerUtcMs = new DateTimeOffset(at.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(at, DateTimeKind.Local) : at)
            .ToUnixTimeMilliseconds();
        if (CanBeExact)
            Alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerUtcMs, intent);
        else
            Alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerUtcMs, intent);
    }

    public static void Cancel(PendingIntent intent) => Alarms.Cancel(intent);

    /// <summary>Opens the system "Alarms &amp; reminders" page for this app.</summary>
    public static void RequestExactTiming()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return;
        var intent = new Intent(Settings.ActionRequestScheduleExactAlarm, global::Android.Net.Uri.Parse($"package:{Context.PackageName}"))
            .AddFlags(ActivityFlags.NewTask);
        Context.StartActivity(intent);
    }
}
