namespace Jarvis.Application;

public static class CancellationExceptions
{
    public static OperationCanceledException? Unwrap(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException canceled) return canceled;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                if (inner is OperationCanceledException canceled) return canceled;
            }
        }

        return null;
    }
}
