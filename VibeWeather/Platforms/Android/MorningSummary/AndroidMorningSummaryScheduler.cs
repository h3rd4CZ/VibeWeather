using Android.App;
using Android.Content;
using VibeWeather.Services.MorningSummary;

namespace VibeWeather.Platforms.Android.MorningSummary;

/// <summary>The morning alarm (see <see cref="ExactAlarms"/>): exact with "Alarms &amp; reminders", else up to an hour late.</summary>
public sealed class AndroidMorningSummaryScheduler : IMorningSummaryScheduler
{
    const int RequestCode = 7103;

    static Context Context => Platform.AppContext;

    public bool CanBeExact => ExactAlarms.CanBeExact;

    public void Schedule(DateTime at) => ExactAlarms.Set(at, Intent());

    public void Cancel() => ExactAlarms.Cancel(Intent());

    public void RequestExactTiming() => ExactAlarms.RequestExactTiming();

    static PendingIntent Intent() =>
        PendingIntent.GetBroadcast(Context, RequestCode, new Intent(Context, typeof(MorningSummaryReceiver)),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}
