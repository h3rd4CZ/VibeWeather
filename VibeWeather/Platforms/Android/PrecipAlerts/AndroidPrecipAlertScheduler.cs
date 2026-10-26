using Android.App;
using Android.App.Job;
using Android.Content;
using VibeWeather.Services.PrecipAlerts;

namespace VibeWeather.Platforms.Android.PrecipAlerts;

/// <summary>
/// One pending alarm at a time (see <see cref="ExactAlarms"/>), received by <see cref="PrecipAlertReceiver"/>; each
/// check sets the next. Exact with "Alarms &amp; reminders"; without it Android may run checks late.
/// </summary>
public sealed class AndroidPrecipAlertScheduler : IPrecipAlertScheduler
{
    const int RequestCode = 7101;
    // Checks used to run as a JobScheduler job with this id; drop any left over from an older version.
    const int LegacyJobId = 7101;

    static Context Context => Platform.AppContext;

    public bool IsScheduled =>
        PendingIntent.GetBroadcast(Context, RequestCode, NewIntent(), PendingIntentFlags.Immutable | PendingIntentFlags.NoCreate) is not null;

    public bool CanBeExact => ExactAlarms.CanBeExact;

    public void Schedule(DateTime atUtc)
    {
        ((JobScheduler)Context.GetSystemService(Context.JobSchedulerService)!).Cancel(LegacyJobId);
        ExactAlarms.Set(atUtc, PendingAlarm());
    }

    public void Cancel()
    {
        var alarm = PendingAlarm();
        ExactAlarms.Cancel(alarm);
        // Also drop the PendingIntent itself, so IsScheduled turns false.
        alarm.Cancel();
    }

    public void RequestExactTiming() => ExactAlarms.RequestExactTiming();

    static Intent NewIntent() => new(Context, typeof(PrecipAlertReceiver));

    static PendingIntent PendingAlarm() =>
        PendingIntent.GetBroadcast(Context, RequestCode, NewIntent(), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}
