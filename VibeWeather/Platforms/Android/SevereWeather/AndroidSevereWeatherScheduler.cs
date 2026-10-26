using Android.App;
using Android.Content;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather.Platforms.Android.SevereWeather;

/// <summary>One pending alarm at a time (see <see cref="ExactAlarms"/>), received by <see cref="SevereWeatherReceiver"/>.</summary>
public sealed class AndroidSevereWeatherScheduler : ISevereWeatherScheduler
{
    const int RequestCode = 7201;

    static Context Context => Platform.AppContext;

    public bool IsScheduled =>
        PendingIntent.GetBroadcast(Context, RequestCode, NewIntent(), PendingIntentFlags.Immutable | PendingIntentFlags.NoCreate) is not null;

    public void Schedule(DateTime atUtc) => ExactAlarms.Set(atUtc, PendingAlarm());

    public void Cancel()
    {
        var alarm = PendingAlarm();
        ExactAlarms.Cancel(alarm);
        // Also drop the PendingIntent itself, so IsScheduled turns false.
        alarm.Cancel();
    }

    static Intent NewIntent() => new(Context, typeof(SevereWeatherReceiver));

    static PendingIntent PendingAlarm() =>
        PendingIntent.GetBroadcast(Context, RequestCode, NewIntent(), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}
