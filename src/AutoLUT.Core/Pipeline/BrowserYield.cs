namespace AutoLUT.Core.Pipeline;

/// <summary>
/// The browser build has no worker threads, so Task.Run executes on the UI thread and long work
/// never lets the page paint (status text stays stale, the tab looks frozen). Awaiting this
/// between steps hands the event loop back for about one 60 Hz frame; elsewhere it is free.
/// </summary>
public static class BrowserYield
{
    private const int FrameMilliseconds = 20;

    public static Task ForFrameAsync(CancellationToken cancellationToken = default) =>
        OperatingSystem.IsBrowser() ? Task.Delay(FrameMilliseconds, cancellationToken) : Task.CompletedTask;
}
