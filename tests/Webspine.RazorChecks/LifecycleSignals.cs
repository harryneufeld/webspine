namespace Webspine.RazorChecks;

public static class LifecycleSignals
{
    public static TaskCompletionSource Started { get; set; } = NewSignal();
    public static TaskCompletionSource Continue { get; set; } = NewSignal();
    public static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static void Reset() { Started = NewSignal(); Continue = NewSignal(); }
}
