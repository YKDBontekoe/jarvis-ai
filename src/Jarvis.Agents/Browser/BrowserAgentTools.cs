using System.ComponentModel;
using Jarvis.Application.Browser;
using Jarvis.Application.Conversations;
using Jarvis.Application.Realtime;
using Jarvis.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Browser;

internal sealed class BrowserAgentTools(
    IBrowserSessionStore sessions,
    AgentTurnContext turn,
    IRealtimePublisher realtime,
    McpToolHost mcp,
    ICurrentUser currentUser)
{
    [Description("Start an isolated browser session for a web or computer-use task. Navigation and interaction stay approval-gated through Playwright. Call this before using browser_* tools so the app can show a step timeline. Do not use this for private or local URLs.")]
    public async Task<string> BrowseTheWebAsync(
        [Description("What you need to accomplish in the browser, in one or two sentences.")] string goal,
        [Description("Optional https URL to start from.")] string? startUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (turn.ConversationId is not { } conversationId)
            return "BrowseTheWeb is only available during an interactive conversation.";
        var trimmedGoal = goal?.Trim() ?? string.Empty;
        if (trimmedGoal.Length is 0 or > 1_000)
            return "Describe the browsing goal in 1 to 1,000 characters.";
        string? url;
        try { url = BrowserUrls.Normalize(startUrl); }
        catch (ArgumentException exception) { return exception.Message; }

        await mcp.InitializeAsync(cancellationToken);
        var browserTools = mcp.Tools.Select(tool => tool.Name)
            .Where(name => name.StartsWith("browser_", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (browserTools.Length == 0)
            return "The isolated Playwright browser is not connected. Enable the browser Compose profile, then try again.";

        var session = await sessions.StartAsync(currentUser.OwnerId, conversationId, trimmedGoal, url,
            cancellationToken);
        await sessions.RecordStepAsync(session.Id, "BrowseTheWeb",
            url is null ? $"Started: {trimmedGoal}" : $"Started: {trimmedGoal} at {url}", true, cancellationToken);
        await realtime.PublishToConversationAsync(conversationId, "browser.session",
            new { id = session.Id, conversationId, goal = trimmedGoal, startUrl = url, status = session.Status },
            cancellationToken);
        var hint = url is null
            ? "Use the connected browser_* tools to complete the goal. Keep going until you can answer, then stop."
            : $"Navigate to {url} first if needed, then complete the goal with browser_* tools.";
        return $"Browser session {session.Id:N} is open. {hint} Available tools: {string.Join(", ", browserTools.Take(12))}.";
    }
}

internal sealed class BrowserToolContributor(
    IBrowserSessionStore sessions,
    AgentTurnContext turn,
    IRealtimePublisher realtime,
    McpToolHost mcp,
    ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context) =>
        [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            new BrowserAgentTools(sessions, turn, realtime, mcp, currentUser).BrowseTheWebAsync))];
}

internal sealed class BrowserContextContributor : IAgentContextContributor
{
    public int Order => 55;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new BrowserContextProvider()];
}

internal sealed class BrowserContextProvider : MessageAIContextProvider
{
    internal const string Prefix = "Browser agent";

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        const string text = Prefix +
            ": for live websites, call BrowseTheWeb with a goal, then use the isolated Playwright browser_* tools. Those tools cannot open local or private hosts. Prefer screenshots and snapshots over guessing page content.";
        return new ValueTask<IEnumerable<ChatMessage>>([new ChatMessage(ChatRole.User, text)]);
    }
}

/// <summary>Records Playwright MCP tool calls against the active browser session so the app can show a timeline.</summary>
internal sealed class BrowserStepRecordingFunction : DelegatingAIFunction
{
    private readonly IBrowserSessionStore _sessions;
    private readonly AgentTurnContext _turn;
    private readonly IRealtimePublisher _realtime;
    private readonly ICurrentUser _currentUser;

    public BrowserStepRecordingFunction(AIFunction inner, IBrowserSessionStore sessions, AgentTurnContext turn,
        IRealtimePublisher realtime, ICurrentUser currentUser) : base(inner)
    {
        _sessions = sessions;
        _turn = turn;
        _realtime = realtime;
        _currentUser = currentUser;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var success = true;
        object? result;
        try
        {
            result = await base.InvokeCoreAsync(arguments, cancellationToken);
        }
        catch
        {
            success = false;
            throw;
        }
        finally
        {
            if (_turn.ConversationId is { } conversationId)
            {
                var session = await _sessions.GetActiveForConversationAsync(_currentUser.OwnerId, conversationId,
                    cancellationToken);
                if (session is not null)
                {
                    var summary = success ? $"{Name} completed." : $"{Name} failed.";
                    await _sessions.RecordStepAsync(session.Id, Name, summary, success, cancellationToken);
                    await _realtime.PublishToConversationAsync(conversationId, "browser.step",
                        new { sessionId = session.Id, tool = Name, summary, success }, cancellationToken);
                }
            }
        }
        return result;
    }
}

internal static class BrowserToolWrapping
{
    public static IEnumerable<AITool> Wrap(IEnumerable<AITool> tools, IBrowserSessionStore sessions,
        AgentTurnContext turn, IRealtimePublisher realtime, ICurrentUser currentUser)
    {
        foreach (var tool in tools)
        {
            if (tool is AIFunction function &&
                function.Name.StartsWith("browser_", StringComparison.OrdinalIgnoreCase))
                yield return new BrowserStepRecordingFunction(function, sessions, turn, realtime, currentUser);
            else
                yield return tool;
        }
    }
}
