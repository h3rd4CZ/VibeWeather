using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VibeWeather.Services.Diagnostics;

namespace VibeWeather.Components.Shared;

/// <summary>
/// Catches exceptions thrown while rendering or handling events in its content, records them in the error
/// log and shows the general error popup. The content is replaced by ErrorContent until <see cref="ErrorBoundaryBase.Recover"/>.
/// </summary>
public sealed class AppErrorBoundary : ErrorBoundary
{
    [Inject] ErrorReporter Errors { get; set; } = default!;

    /// <summary>What the content was doing, for the log and popup.</summary>
    [Parameter] public string Source { get; set; } = "Zobrazení této obrazovky";

    protected override Task OnErrorAsync(Exception exception)
    {
        Errors.Report(exception, Source);
        return Task.CompletedTask;
    }
}
