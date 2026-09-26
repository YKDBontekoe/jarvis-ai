using Jarvis.Domain.Approvals;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ToolApprovalTests
{
    [Fact]
    public void Cancel_only_applies_to_pending_approvals()
    {
        var pending = new ToolApproval(Guid.CreateVersion7(), Guid.CreateVersion7(), "req", "call",
            "CreateReminder", "{}");
        pending.Cancel();
        Assert.Equal("cancelled", pending.Status);

        var decided = new ToolApproval(Guid.CreateVersion7(), Guid.CreateVersion7(), "req-2", "call-2",
            "CreateReminder", "{}");
        decided.Decide(true);
        decided.Cancel();
        Assert.Equal("approved", decided.Status);
    }
}
