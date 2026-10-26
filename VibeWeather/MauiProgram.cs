using Microsoft.Extensions.Logging;
using VibeWeather.Services;
using VibeWeather.Services.AzureMaps;
using VibeWeather.Services.Diagnostics;
using VibeWeather.Services.OpenMeteo;
using VibeWeather.Services.MorningSummary;
using VibeWeather.Services.PrecipAlerts;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		// The Blazor UI brings its own web fonts (wwwroot/fonts); no native fonts are registered.
		builder.UseMauiApp<App>();

		builder.Services.AddMauiBlazorWebView();

		// Error log first, so every later failure (including Blazor's own) can be recorded.
		var errorLog = new ErrorLog();
		builder.Services.AddSingleton(errorLog);
		builder.Services.AddSingleton<ErrorReporter>();
		builder.Logging.AddProvider(new ErrorLogLoggerProvider(errorLog));

		builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
		// Weather comes from the source chosen in Settings (Open-Meteo, or Azure Maps with the user's key).
		builder.Services.AddSingleton<WeatherSourceSettings>();
		builder.Services.AddSingleton<OpenMeteoWeatherProvider>();
		builder.Services.AddSingleton<AzureMapsWeatherProvider>();
		builder.Services.AddSingleton<IWeatherProvider, WeatherProviderRouter>();
		builder.Services.AddSingleton<ILocationSearch, OpenMeteoLocationSearch>();
		builder.Services.AddSingleton<DeviceLocationService>();
		builder.Services.AddSingleton<GeofenceManager>();
		builder.Services.AddSingleton<LocationSelection>();
		builder.Services.AddSingleton<DisplaySettings>();
		builder.Services.AddSingleton<WeatherState>();
		builder.Services.AddSingleton<PlacePicker>();
		builder.Services.AddSingleton<ThemeService>();
		builder.Services.AddSingleton<PrecipAlertMonitor>();
		builder.Services.AddSingleton<MorningSummaryService>();
		builder.Services.AddSingleton<AzureMapsSevereAlerts>();
		builder.Services.AddSingleton<SevereWeatherMonitor>();
#if ANDROID
		builder.Services.AddSingleton<IGeofenceMonitor, Platforms.Android.Geofencing.AndroidGeofenceMonitor>();
		builder.Services.AddSingleton<IPrecipAlertScheduler, Platforms.Android.PrecipAlerts.AndroidPrecipAlertScheduler>();
		builder.Services.AddSingleton<IMorningSummaryScheduler, Platforms.Android.MorningSummary.AndroidMorningSummaryScheduler>();
		builder.Services.AddSingleton<ISevereWeatherScheduler, Platforms.Android.SevereWeather.AndroidSevereWeatherScheduler>();
		builder.Services.AddSingleton<ISevereWarningNotifier, Platforms.Android.SevereWeather.AndroidSevereWarningNotifier>();
		builder.Services.AddSingleton<ITimeDialog, Platforms.Android.AndroidTimeDialog>();
		builder.Services.AddSingleton<IHaptics, Platforms.Android.AndroidHaptics>();
#endif

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();
		app.Services.GetRequiredService<ErrorReporter>().CaptureUnhandledExceptions();
		return app;
	}
}
