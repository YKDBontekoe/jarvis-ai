using Temporalio.Activities;

namespace Jarvis.Worker.Activities;

internal sealed class ActivityHeartbeat : IDisposable
{
    private readonly Timer _timer;

    public ActivityHeartbeat(Guid resourceId)
    {
        var activity = ActivityExecutionContext.Current;
        activity.Heartbeat(resourceId);
        _timer = new Timer(_ =>
        {
            try { activity.Heartbeat(resourceId); }
            catch (InvalidOperationException) { }
        }, null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
    }

    public void Dispose() => _timer.Dispose();
}
