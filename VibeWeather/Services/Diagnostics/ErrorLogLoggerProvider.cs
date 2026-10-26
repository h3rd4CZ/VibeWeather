using Microsoft.Extensions.Logging;

namespace VibeWeather.Services.Diagnostics;

/// <summary>
/// Copies Blazor's own error logs (e.g. "Unhandled exception rendering component", for failures outside any
/// error boundary) into the <see cref="ErrorLog"/>. The app's own errors go through <see cref="ErrorReporter"/>.
/// </summary>
public sealed class ErrorLogLoggerProvider(ErrorLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        categoryName.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal)
            ? new ComponentsLogger(log, categoryName)
            : NullLoggerInstance;

    public void Dispose() { }

    static readonly ILogger NullLoggerInstance = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    sealed class ComponentsLogger(ErrorLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && exception is not null)
                // The framework's own (English) log text mostly repeats the exception message, which the entry keeps.
                log.Write(ErrorKind.General, $"Chyba uživatelského rozhraní [{category}]", exception);
        }
    }
}
