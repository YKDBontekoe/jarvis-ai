using System.ComponentModel;
using Jarvis.Application.Browser;
using Jarvis.Application.Computer;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Realtime;
using Jarvis.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Computer;

/// <summary>
/// Computer use: Jarvis drives a small Linux desktop in the sandbox container (infra/computer) through the
/// Playwright browser_* tools and the desktop computer_* tools. Starting a session is the approval; inside an active
/// session most actions run without a new card (computer_shell and browser_fill_form still ask).
/// </summary>
internal sealed class ComputerAgentTools(
    IBrowserSessionStore sessions,
    IComputerSandbox sandbox,
    Guid? conversationId,
    IRealtimePublisher realtime,
    McpToolHost mcp,
    ICurrentUser currentUser)
{
    [Description("Start using the sandbox computer: a small Linux desktop with a visible Chromium browser and a shell, isolated from Jarvis and the user's devices. Use it for web tasks that need a real browser and for desktop or command-line work. The user can watch live and take over. Call this before any browser_* or computer_* tool. Never use it for private or local URLs.")]
    public async Task<string> UseComputerAsync(
        [Description("What you need to accomplish on the computer, in one or two sentences.")] string goal,
        [Description("Optional https URL to open first.")] string? startUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId is not { } id)
            return "UseComputer is only available during an interactive conversation.";
        var trimmedGoal = goal?.Trim() ?? string.Empty;
        if (trimmedGoal.Length is 0 or > 1_000)
            return "Describe the goal in 1 to 1,000 characters.";
        string? url;
        try { url = BrowserUrls.Normalize(startUrl); }
        catch (ArgumentException exception) { return exception.Message; }

        await mcp.InitializeAsync(cancellationToken);
        var tools = mcp.Tools.Select(tool => tool.Name).Where(ComputerToolWrapping.IsComputerTool).ToArray();
        if (!sandbox.IsConfigured || !tools.Any(name => name.StartsWith("computer_", StringComparison.Ordinal)))
            return "The sandbox computer is not connected. Enable the computer feature (JARVIS_FEATURES=computer), then try again.";

        var session = await sessions.TryStartComputerAsync(currentUser.OwnerId, id, trimmedGoal, url,
            sandbox.IdleTimeout, cancellationToken);
        if (session is null)
            return "The sandbox computer is busy with another conversation. Try again later, or finish that session first.";
        try
        {
            await sandbox.ResetAsync(cancellationToken);
        }
        catch (Exception exception) when (IsSandboxFailure(exception, cancellationToken))
        {
            await sessions.CompleteAsync(session.Id, "failed", CancellationToken.None);
            return "The sandbox computer did not respond. Check that the computer-sandbox container is healthy.";
        }

        await sessions.RecordStepAsync(session.Id, "UseComputer",
            url is null ? $"Started: {trimmedGoal}" : $"Started: {trimmedGoal} at {url}", true, cancellationToken);
        await realtime.PublishToConversationAsync(id, "browser.session",
            new
            {
                id = session.Id, conversationId = id, goal = trimmedGoal, startUrl = url, status = session.Status,
                kind = session.Kind, controlMode = session.ControlMode
            }, cancellationToken);
        var start = url is null
            ? "Take a computer_screenshot first to see the desktop."
            : $"Open {url} with browser_navigate first.";
        return $"Computer session {session.Id:N} is open on a fresh desktop. {start} Use browser_* tools for web pages " +
               "and computer_* tools (screenshot, click, type, key, scroll, shell) for anything else. Call StopComputer " +
               $"when the goal is done. Available tools: {string.Join(", ", tools.Take(24))}.";
    }

    [Description("Finish the current sandbox computer session: closes every program and wipes its files. Call this when the computer task is done.")]
    public async Task<string> StopComputerAsync(CancellationToken cancellationToken = default)
    {
        if (conversationId is not { } id)
            return "StopComputer is only available during an interactive conversation.";
        var session = await sessions.GetActiveForConversationAsync(currentUser.OwnerId, id, cancellationToken);
        if (session is null || session.Kind != BrowserSessionKinds.Computer)
            return "There is no open computer session in this conversation.";
        await sessions.CompleteAsync(session.Id, "completed", cancellationToken);
        try { await sandbox.ResetAsync(cancellationToken); }
        catch (Exception exception) when (IsSandboxFailure(exception, cancellationToken))
        {
            // The next UseComputer resets again before anyone sees the desktop.
        }

        await realtime.PublishToConversationAsync(id, "browser.session",
            new
            {
                id = session.Id, conversationId = id, goal = session.Goal, startUrl = session.StartUrl,
                status = "completed", kind = session.Kind, controlMode = session.ControlMode
            }, cancellationToken);
        return "The computer session is closed and the sandbox was wiped.";
    }

    /// <summary>The sandbox is down or timed out; a cancelled turn is not a sandbox failure.</summary>
    private static bool IsSandboxFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or InvalidOperationException ||
        (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}

internal sealed class ComputerToolContributor(
    IBrowserSessionStore sessions,
    IComputerSandbox sandbox,
    IRealtimePublisher realtime,
    McpToolHost mcp,
    ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        if (!sandbox.IsConfigured || context.ConversationId is null) return [];
        var tools = new ComputerAgentTools(sessions, sandbox, context.ConversationId, realtime, mcp, currentUser);
        return
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tools.UseComputerAsync)),
            AIFunctionFactory.Create(tools.StopComputerAsync)
        ];
    }
}

internal sealed class ComputerContextContributor(IComputerSandbox sandbox) : IAgentContextContributor
{
    public int Order => 56;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        sandbox.IsConfigured && context.ConversationId is not null ? [new ComputerContextProvider()] : [];
}

internal sealed class ComputerContextProvider : MessageAIContextProvider
{
    internal const string Prefix = "Sandbox computer";

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        const string text = Prefix +
            ": for live websites or desktop and command-line work, call UseComputer with a goal, then work step by step. " +
            "Prefer browser_snapshot and browser_* tools for web pages; use computer_screenshot and computer_* tools for " +
            "anything outside the page. Look at the latest screenshot before choosing coordinates, and check the result of " +
            "each action. Never type passwords, card numbers or other secrets; ask the user to take over from the live view " +
            "for logins and payments. When a tool says the user has control, stop and wait for them. Call StopComputer when done.";
        return new ValueTask<IEnumerable<ChatMessage>>([new ChatMessage(ChatRole.User, text)]);
    }
}

/// <summary>
/// Wraps every sandbox MCP tool: refuses unless this conversation holds an active, agent-driven computer session,
/// records the step, keeps the screenshot in object storage for the app, and hands it to the model as an image.
/// </summary>
internal sealed class ComputerStepFunction(
    AIFunction inner,
    IBrowserSessionStore sessions,
    IObjectStorage objects,
    Guid? conversationId,
    IRealtimePublisher realtime,
    ICurrentUser currentUser) : DelegatingAIFunction(inner)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        if (conversationId is not { } id)
            return "The sandbox computer is only available during an interactive conversation.";
        var session = await sessions.GetActiveForConversationAsync(currentUser.OwnerId, id, cancellationToken);
        if (session is null || session.Kind != BrowserSessionKinds.Computer)
            return "No computer session is open. Call UseComputer first.";
        if (session.ControlMode == ComputerControlModes.User)
            return "The user has taken control of the sandbox computer. Stop and wait until they hand it back.";

        object? result;
        try
        {
            result = await base.InvokeCoreAsync(arguments, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordAsync(session, id, $"{Name} failed.", false, null, cancellationToken);
            throw;
        }

        var observation = ComputerToolResults.Read(result);
        string? screenshotKey = null;
        if (observation.Image is { Length: > 0 and <= ComputerScreenshots.MaxBytes } image)
        {
            screenshotKey = ComputerScreenshots.NewKey(currentUser.OwnerId, session.Id, observation.ImageMediaType!);
            using var stream = new MemoryStream(image, writable: false);
            await objects.PutAsync(screenshotKey, stream, observation.ImageMediaType!, cancellationToken);
        }

        await RecordAsync(session, id, Summarize(Name, observation), !observation.IsError, screenshotKey,
            cancellationToken);
        if (observation.Image is null) return result;
        return new AIContent[]
        {
            new TextContent(observation.Text.Length == 0 ? "Screenshot of the sandbox desktop." : observation.Text),
            new DataContent(observation.Image, observation.ImageMediaType!)
        };
    }

    private async Task RecordAsync(BrowserSessionRecord session, Guid conversation, string summary, bool success,
        string? screenshotKey, CancellationToken cancellationToken)
    {
        var step = await sessions.RecordStepAsync(session.Id, Name, summary, success, cancellationToken, screenshotKey);
        if (screenshotKey is not null)
        {
            foreach (var old in await sessions.TrimScreenshotsAsync(session.Id, ComputerScreenshots.KeptPerSession,
                         cancellationToken))
                await objects.DeleteAsync(old, cancellationToken);
        }

        await realtime.PublishToConversationAsync(conversation, "browser.step",
            new
            {
                sessionId = session.Id, tool = Name, summary, success, ordinal = step?.Ordinal,
                hasScreenshot = screenshotKey is not null
            }, cancellationToken);
    }

    /// <summary>
    /// A short, server-written line for the timeline. Typed text and shell output never go into it: the desktop
    /// server's first line ("Typed 12 characters.", "Command finished (exit code 0).") or a generic note.
    /// </summary>
    internal static string Summarize(string tool, ComputerObservation observation)
    {
        if (tool.StartsWith("computer_", StringComparison.Ordinal) && observation.Text.Length > 0)
        {
            var first = observation.Text.Split('\n', 2)[0].Trim();
            if (first.Length is > 0 and <= 200) return first;
        }

        return observation.IsError ? $"{tool} failed." : $"{tool} completed.";
    }
}

internal static class ComputerToolWrapping
{
    public static bool IsComputerTool(string name) =>
        name.StartsWith("computer_", StringComparison.Ordinal) ||
        name.StartsWith("browser_", StringComparison.OrdinalIgnoreCase);

    /// <summary>With the computer feature on, browser_* tools come from the sandbox's Playwright server too.</summary>
    public static IEnumerable<AITool> Wrap(IEnumerable<AITool> tools, IBrowserSessionStore sessions,
        IObjectStorage objects, Guid? conversationId, IRealtimePublisher realtime, ICurrentUser currentUser)
    {
        foreach (var tool in tools)
        {
            if (tool is AIFunction function && IsComputerTool(function.Name))
                yield return new ComputerStepFunction(function, sessions, objects, conversationId, realtime, currentUser);
            else
                yield return tool;
        }
    }
}
