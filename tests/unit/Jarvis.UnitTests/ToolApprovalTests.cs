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

    [Fact]
    public void AbortResume_stops_in_flight_decided_approvals()
    {
        var approval = new ToolApproval(Guid.CreateVersion7(), Guid.CreateVersion7(), "req", "call",
            "CreateReminder", "{}");
        approval.Decide(true);
        approval.AbortResume();
        Assert.Equal("approved", approval.Status);
        Assert.Equal("cancelled", approval.ResumeStatus);

        approval.AbortResume();
        Assert.Equal("cancelled", approval.ResumeStatus);

        var pending = new ToolApproval(Guid.CreateVersion7(), Guid.CreateVersion7(), "req-3", "call-3",
            "CreateReminder", "{}");
        pending.AbortResume();
        Assert.Equal("pending", pending.Status);
        Assert.Equal("not_started", pending.ResumeStatus);
    }
}
