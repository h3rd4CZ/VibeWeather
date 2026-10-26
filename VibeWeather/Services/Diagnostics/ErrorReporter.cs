using Microsoft.Extensions.Logging;

namespace VibeWeather.Services.Diagnostics;

/// <summary>An error the user is told about, matching an <see cref="ErrorLog"/> entry by Reference.</summary>
/// <param name="Source">What the app was doing, e.g. "Načítání předpovědi".</param>
/// <param name="Message">User-facing explanation.</param>
/// <param name="Retry">Repeats the failed action, when that makes sense.</param>
public sealed record ErrorReport(
    string Reference, ErrorKind Kind, string Source, string Message, TimeSpan? RetryAfter, Func<Task>? Retry);

/// <summary>
/// Classifies failures as throttling (the weather service's request limits or load) or general errors,
/// records them in the <see cref="ErrorLog"/>, and raises the popup. <see cref="Changed"/> may be raised
/// off the UI thread.
/// </summary>
public sealed class ErrorReporter(ErrorLog log, ILogger<ErrorReporter> logger)
{
    public event Action? Changed;

    /// <summary>The report the popup is showing, or null.</summary>
    public ErrorReport? Current { get; private set; }

    /// <param name="popup">False to only log, for failures the screen already explains in place.</param>
    public ErrorReport Report(Exception exception, string source, Func<Task>? retry = null, bool popup = true)
    {
        var throttled = exception as WeatherProviderException is { IsThrottled: true };
        var kind = throttled ? ErrorKind.Throttled : ErrorKind.General;
        var entry = log.Write(kind, source, exception);
        logger.Log(throttled ? LogLevel.Warning : LogLevel.Error, exception, "{Source} failed ({Reference})", source, entry.Reference);

        var report = new ErrorReport(
            entry.Reference,
            kind,
            source,
            exception is WeatherProviderException ? exception.Message : "Došlo k neočekávané chybě.",
            (exception as WeatherProviderException)?.RetryAfter,
            retry);

        if (popup)
        {
            Current = report;
            Changed?.Invoke();
        }
        return report;
    }

    public void Dismiss()
    {
        Current = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Records crashes and other exceptions nothing caught, so they show up in the error log next time.
    /// Call once at startup.
    /// </summary>
    public void CaptureUnhandledExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) log.Write(ErrorKind.General, "Neošetřená chyba (aplikace spadla)", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Write(ErrorKind.General, "Neošetřená chyba úlohy na pozadí", e.Exception);
            e.SetObserved();
        };
#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            log.Write(ErrorKind.General, "Neošetřená chyba (aplikace spadla)", e.Exception);
#endif
    }
}
