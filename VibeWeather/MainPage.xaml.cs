using Microsoft.AspNetCore.Components.WebView;
using VibeWeather.Services;

namespace VibeWeather;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
		// The page sits between the system bars; move the sun to the screen's centre, where Android's splash drew it.
		SizeChanged += (_, _) => splashSun.TranslationY = -SplashGeometry.ShiftDp;
	}

	// The WebView is white until index.html paints; transparent, it shows the page (and its splash sun) instead.
	void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
	{
#if ANDROID
		e.WebView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#endif
	}

	// Back with no earlier page in the WebView (e.g. a page opened from a notification) would close the app; from any
	// page but Teď it goes to Teď instead.
	protected override bool OnBackButtonPressed()
	{
		var weather = Handler?.MauiContext?.Services.GetService<WeatherState>();
		if (weather is null || weather.CurrentRoute.Length == 0) return base.OnBackButtonPressed();
		weather.ShowHome();
		return true;
	}
}
