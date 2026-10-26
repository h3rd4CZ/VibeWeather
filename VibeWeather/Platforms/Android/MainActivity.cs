using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using VibeWeather.Platforms.Android;
using VibeWeather.Platforms.Android.Geofencing;
using VibeWeather.Services;

namespace VibeWeather;

// SingleTop: a tapped arrival notification reaches the running activity through OnNewIntent instead of a second copy.
[Activity(Theme = "@style/VibeWeather.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		// MAUI reads the system light/dark setting when the process starts and on changes while an activity runs.
		// The process may have started in the background (an alarm or geofence) before the system switched, so
		// read it again before the page is built; Auto appearance follows it.
		IPlatformApplication.Current?.Application?.ThemeChanged();
		base.OnCreate(savedInstanceState);

		ApplySystemBarIcons();
		KeepAboveKeyboard();
		if (Microsoft.Maui.Controls.Application.Current is { } app)
			app.RequestedThemeChanged += (_, _) => ApplySystemBarIcons();

		// Not when the system recreates the activity; the selection was already made then.
		if (savedInstanceState is null) ShowGeofenceFrom(Intent);
	}

	// The app draws edge to edge, so the keyboard covers the page instead of shrinking it. Padding the content by
	// the keyboard's height (less the navigation bar, which the page already avoids) keeps a focused field visible:
	// the WebView gets shorter and scrolls the field into view. The insets still reach the page unchanged.
	void KeepAboveKeyboard()
	{
		if (FindViewById(global::Android.Resource.Id.Content) is not { } content) return;
		ViewCompat.SetOnApplyWindowInsetsListener(content, new KeyboardInsetsListener());
	}

	sealed class KeyboardInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
	{
		public WindowInsetsCompat OnApplyWindowInsets(global::Android.Views.View v, WindowInsetsCompat insets)
		{
			int keyboard = insets.GetInsets(WindowInsetsCompat.Type.Ime()).Bottom;
			var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
			v.SetPadding(0, 0, 0, Math.Max(0, keyboard - bars.Bottom));
			// The page sits between the system bars, so its centre is off the screen's, where Android's splash drew the sun.
			SplashGeometry.ShiftDp = (bars.Top - bars.Bottom) / 2.0 / (v.Resources?.DisplayMetrics?.Density ?? 1);
			return insets;
		}
	}

	// Dark icons on the light paper background, light icons in dark mode (chosen, or the system's in Auto).
	void ApplySystemBarIcons()
	{
		bool dark = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;
		if (Window is { } window && WindowCompat.GetInsetsController(window, window.DecorView) is { } insets)
		{
			insets.AppearanceLightStatusBars = !dark;
			insets.AppearanceLightNavigationBars = !dark;
		}
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		ShowGeofenceFrom(intent);
	}

	// Opened from a notification: an arrival shows that geofence's place, a precipitation alert its Next hour tab.
	static void ShowGeofenceFrom(Intent? intent)
	{
		if (intent is null) return;
		var services = IPlatformApplication.Current?.Services;
		var weather = services?.GetService<WeatherState>();
		if (weather is null) return;

		if (intent.GetStringExtra(GeofenceNotifications.GeofenceIdExtra) is { } id)
		{
			intent.RemoveExtra(GeofenceNotifications.GeofenceIdExtra);
			var geofence = services!.GetService<GeofenceManager>()?.Places.FirstOrDefault(g => g.Id == id);
			if (geofence is not null) _ = weather.ShowAsync(geofence);
		}
		else
		{
			// Precipitation alert or morning summary.
			NotificationIntents.TryHandle(intent, weather);
		}
	}
}
