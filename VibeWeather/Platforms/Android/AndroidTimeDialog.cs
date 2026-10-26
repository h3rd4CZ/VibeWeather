using Android.App;
using Android.Content;
using VibeWeather.Services;

namespace VibeWeather.Platforms.Android;

/// <summary>Android's TimePickerDialog, in the 12- or 24-hour style chosen in Settings.</summary>
public sealed class AndroidTimeDialog(DisplaySettings display) : ITimeDialog
{
    public Task<TimeOnly?> PickAsync(TimeOnly initial, string title)
    {
        var result = new TaskCompletionSource<TimeOnly?>();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Platform.CurrentActivity is not { } activity)
            {
                result.TrySetResult(null);
                return;
            }

            var dialog = new TimePickerDialog(activity,
                (_, e) => result.TrySetResult(new TimeOnly(e.HourOfDay, e.Minute)),
                initial.Hour, initial.Minute, display.Units.Is24Hour);
            dialog.SetTitle(title);
            // Czech buttons whatever the phone's language; the dialog itself still handles the clicks.
            dialog.SetButton((int)DialogButtonType.Positive, "OK", dialog);
            dialog.SetButton((int)DialogButtonType.Negative, "Zrušit", dialog);
            // Also raised after a time was set; by then the result is taken, so this only covers Cancel and back.
            dialog.DismissEvent += (_, _) => result.TrySetResult(null);
            dialog.Show();
        });
        return result.Task;
    }
}
