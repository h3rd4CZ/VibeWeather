using VibeWeather.Services;
using VibeWeather.Services.MorningSummary;
using VibeWeather.Services.PrecipAlerts;
using VibeWeather.Services.SevereWeather;

namespace VibeWeather;

public partial class App : Application
{
	readonly WeatherState weather;
	readonly GeofenceManager geofences;
	readonly PrecipAlertMonitor alerts;
	readonly MorningSummaryService morning;
	readonly SevereWeatherMonitor severe;

	// PrecipAlertMonitor is created here so it follows location changes for the whole app session.
	public App(WeatherState weather, GeofenceManager geofences, PrecipAlertMonitor alerts, MorningSummaryService morning,
		SevereWeatherMonitor severe, ThemeService theme)
	{
		InitializeComponent();
		// The stored appearance (or the system's, in Auto) before the first page shows.
		theme.Attach(this);
		this.weather = weather;
		this.geofences = geofences;
		this.alerts = alerts;
		this.morning = morning;
		this.severe = severe;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "Vibe Weather" };
		// Coming back to the app after a while should show fresh weather.
		window.Resumed += (_, _) =>
		{
			_ = weather.RefreshIfStaleAsync();
			// The user may have allowed exact alarms in system settings meanwhile.
			morning.Refresh();
			alerts.Refresh();
			severe.Refresh();
		};
		// Re-register geofences in case the platform dropped them (e.g. location was turned off).
		_ = geofences.RestoreAsync(forgetInside: false);
		// Re-arm precipitation checks and the morning alarm if the platform dropped them (e.g. after an app update).
		alerts.EnsureScheduled();
		morning.ScheduleNext();
		severe.EnsureScheduled();
		return window;
	}
}
