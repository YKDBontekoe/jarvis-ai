using Jarvis.Infrastructure.Persistence;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class TaskNotificationTests
{
    [Fact]
    public void Finished_body_carries_the_first_line_of_the_result()
    {
        var body = WorkflowRepository.TaskFinishedBody("Summarise inbox",
            "## Result\n\n**14 emails** triaged.\n\nDetails follow.");
        Assert.Equal("Summarise inbox: 14 emails triaged.", body);
    }

    [Fact]
    public void Finished_body_falls_back_to_the_title_and_stays_short()
    {
        Assert.Equal("Just a title", WorkflowRepository.TaskFinishedBody("Just a title", "  \n "));
        var long_ = WorkflowRepository.TaskFinishedBody("T", new string('x', 500));
        Assert.True(long_.Length < 200);
        Assert.EndsWith("…", long_);
    }
}
