using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.HomeRoutines;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.HomeRoutines;

internal sealed class HomeRoutineToolContributor(IHomeRoutineService routines, ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new HomeRoutineAgentTools(routines, currentUser);
        yield return AIFunctionFactory.Create(tools.PreviewHomeRoutineAsync);
    }
}

/// <summary>Read-only: shows what a home routine would do. It never runs one and never touches a device.</summary>
internal sealed class HomeRoutineAgentTools(IHomeRoutineService routines, ICurrentUser currentUser)
{
    [Description("Show what a home routine (morning, focus, leave, evening) would do: each action, the device, and which are unavailable or need the user's approval. This only previews. Devices are examples unless the app says a real home is connected; never say a routine ran.")]
    public async Task<string> PreviewHomeRoutineAsync(
        [Description("morning, focus, leave or evening.")] string routine,
        CancellationToken cancellationToken = default)
    {
        var plan = await routines.PlanAsync(currentUser.OwnerId, routine.Trim().ToLowerInvariant(), cancellationToken);
        if (!plan.Succeeded) return "There is no routine with that name. Use morning, focus, leave or evening.";
        var text = new StringBuilder($"{plan.Value!.Template.Title}: {plan.Value.Summary}\n");
        foreach (var step in plan.Value.Steps)
            text.AppendLine($"- {step.Step.Label} [{step.Status}]{(step.Device is null ? "" : " via " + step.Device.Name)}: {step.Reason}");
        text.AppendLine(HomeRoutineRules.Notice);
        return text.ToString();
    }
}
