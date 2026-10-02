using Jarvis.Application.Conversations;
using Jarvis.Application.Planner;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Planner;

internal sealed class PlannerToolContributor(IDayPlannerService planner, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new PlannerAgentTools(planner, currentUser);
        yield return AIFunctionFactory.Create(tools.GetTodayPlanAsync);
        yield return AIFunctionFactory.Create(tools.AddToDayPlanAsync);
        yield return AIFunctionFactory.Create(tools.PlanMyDayAsync);
        yield return AIFunctionFactory.Create(tools.CompleteDayPlanItemAsync);
        yield return AIFunctionFactory.Create(tools.RemoveDayPlanItemAsync);
    }
}

/// <summary>Tells the agent how the day planner works, without loading the plan into every turn.</summary>
internal sealed class PlannerContextContributor : IAgentContextContributor
{
    internal const string Guidance = """
        Day planner: the user has a Today plan in Jarvis. When they ask to plan their day, list what they want to get done with AddToDayPlan (ask for rough minutes only if unclear), then call PlanMyDay and summarise the timeline in a few lines. Use GetTodayPlan for "what does my day look like". Reminders are for fixed times; day plan to-dos are for things to fit in. The plan lives in Jarvis only. Add focus blocks to the user's calendar only when they ask, and only through InvokeMcpTool with the calendar server's create-event tool, so Jarvis asks for approval for every calendar write.
        """;

    public int Order => 46;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        context.IsBackgroundTask ? [] : [new PlannerGuidanceProvider(Guidance)];

    private sealed class PlannerGuidanceProvider(string text) : MessageAIContextProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IEnumerable<ChatMessage>>([new ChatMessage(ChatRole.User, text)]);
    }
}
