using Jarvis.Application;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CancellationExceptionsTests
{
    [Fact]
    public void Unwraps_direct_and_nested_cancellation()
    {
        var canceled = new OperationCanceledException();
        Assert.Same(canceled, CancellationExceptions.Unwrap(canceled));
        Assert.Same(canceled, CancellationExceptions.Unwrap(new InvalidOperationException("wrap", canceled)));
        Assert.Same(canceled, CancellationExceptions.Unwrap(new AggregateException(new InvalidOperationException(), canceled)));
        Assert.Null(CancellationExceptions.Unwrap(new InvalidOperationException("nope")));
    }
}
