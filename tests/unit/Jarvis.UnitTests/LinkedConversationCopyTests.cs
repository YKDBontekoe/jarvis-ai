using Jarvis.Application.Conversations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class LinkedConversationCopyTests
{
    [Fact]
    public void Reminder_copy_keeps_titles_on_one_line()
    {
        var intro = LinkedConversationCopy.ReminderIntro("Call\nmom");
        Assert.Contains("Call mom", intro);
        Assert.Contains("linked to your reminder", intro);

        var due = LinkedConversationCopy.ReminderDue("Dentist");
        Assert.Contains("Reminder: Dentist", due);
        Assert.Contains("due now", due);
    }

    [Fact]
    public void Automation_run_copy_includes_action_results()
    {
        var json = """[{"kind":"notification","status":"completed","detail":null,"resourceId":null}]""";
        var text = LinkedConversationCopy.AutomationRun("Morning", "completed", "Manual run", json);
        Assert.Contains("Morning", text);
        Assert.Contains("completed", text);
        Assert.Contains("notification: completed", text);
        Assert.Contains("Reply here", text);
    }
}
