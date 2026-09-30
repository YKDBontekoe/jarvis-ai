using System.Text.Json;
using Jarvis.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AgentAutonomyTests
{
    [Fact]
    public void Default_persona_pushes_the_agent_to_finish_instead_of_asking()
    {
        var instructions = JarvisAgentFactory.BuildInstructions(null, executingTask: false);

        Assert.Contains("Finish the job in this turn", instructions);
        Assert.Contains("pick it, act, and name the default", instructions);
        Assert.Contains("Do not ask for permission in text", instructions);
        Assert.Contains("show the user an approval card on their own", instructions);
        Assert.Contains("If the user rejects a call, do not retry it", instructions);
        Assert.Contains("When a tool fails, read the error", instructions);
        Assert.Contains("create the condition watch, reminder, or background task right away", instructions);
    }

    [Fact]
    public void Background_tasks_assume_instead_of_asking_but_keep_approvals()
    {
        var instructions = JarvisAgentFactory.BuildInstructions(null, executingTask: true);

        Assert.Contains("do not stop to ask questions", instructions);
        Assert.Contains("list the assumptions you made", instructions);
        Assert.Contains("Actions that need approval still wait for the user's decision", instructions);
        Assert.DoesNotContain("do not stop to ask questions",
            JarvisAgentFactory.BuildInstructions(null, executingTask: false));
    }

    [Fact]
    public async Task Tool_validation_errors_reach_the_model_so_it_can_retry()
    {
        var attempts = new List<string>();
        var tool = AIFunctionFactory.Create((string when) =>
        {
            attempts.Add(when);
            if (!DateTimeOffset.TryParse(when, out _))
                throw new ArgumentException("dueAt must be an ISO 8601 date-time with an offset.", nameof(when));
            return "Reminder scheduled.";
        }, "CreateReminder");
        var model = new RetryingModel("CreateReminder", ["tomorrow morning", "2030-01-02T09:00:00+01:00"]);
        var agent = CreateAgent(model, tool);

        var response = await agent.RunAsync("Remind me tomorrow morning");

        Assert.Equal(["tomorrow morning", "2030-01-02T09:00:00+01:00"], attempts);
        var firstFailure = Assert.Single(model.ResultsSeen, result => result.Contains("rejected its input"));
        Assert.Contains("dueAt must be an ISO 8601 date-time with an offset.", firstFailure);
        Assert.DoesNotContain("Parameter 'when'", firstFailure);
        Assert.Contains("Reminder scheduled.", model.ResultsSeen[^1]);
        Assert.Equal("Done.", response.Text);
    }

    [Fact]
    public async Task Unexpected_tool_failures_stay_generic_so_details_never_reach_the_transcript()
    {
        var tool = AIFunctionFactory.Create(new Func<string>(() =>
            throw new InvalidOperationException("connect failed: Password=hunter2 at /srv/secret.db")), "LookUp");
        var model = new RetryingModel("LookUp", [null]);
        var agent = CreateAgent(model, tool);

        await agent.RunAsync("Look it up");

        var failure = Assert.Single(model.ResultsSeen);
        Assert.Contains(ToolFailureFeedback.GenericFailure, failure);
        Assert.DoesNotContain("hunter2", failure);
        Assert.DoesNotContain("/srv/secret.db", failure);
    }

    [Fact]
    public async Task Approval_required_tools_still_stop_for_approval_before_running()
    {
        var invoked = false;
        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() =>
        {
            invoked = true;
            return "Deleted.";
        }, "DeleteEverything"));
        var model = new RetryingModel("DeleteEverything", [null]);
        var agent = CreateAgent(model, tool);

        var response = await agent.RunAsync("Delete it");

        Assert.False(invoked);
        Assert.Contains(response.Messages.SelectMany(message => message.Contents),
            content => content is ToolApprovalRequestContent);
        Assert.Empty(model.ResultsSeen);
    }

    [Fact]
    public void Chat_client_agent_exposes_the_function_invoker_the_factory_configures()
    {
        var agent = new ChatClientAgent(new RetryingModel("x", []), new ChatClientAgentOptions());
        Assert.NotNull(agent.ChatClient.GetService<FunctionInvokingChatClient>());
    }

    [Fact]
    public void Unknown_codex_tool_names_are_retried_instead_of_failing_the_turn()
    {
        using var document = JsonDocument.Parse(
            """{"type":"tool_call","text":"","name":"CreateRemindr","argumentsJson":"{}"}""");

        var exception = Assert.Throws<CodexCliChatClient.UnknownToolCallException>(() =>
            CodexCliChatClient.ParseAssistantMessage(document.RootElement, new HashSet<string> { "CreateReminder" }));

        var retry = CodexCliChatClient.RetryInstruction(exception);
        Assert.NotNull(retry);
        Assert.Contains("'CreateRemindr', which does not exist", retry);
        Assert.Contains("Available Jarvis functions", retry);
        Assert.Null(CodexCliChatClient.RetryInstruction(new InvalidOperationException("other")));
    }

    [Fact]
    public void Codex_prompt_labels_tool_results_with_their_tool_name()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "What is scheduled?"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "ListReminders")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "Nothing scheduled.")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("orphan", "Other.")])
        };

        var prompt = CodexCliChatClient.BuildPrompt(messages, null, [], enableWebSearch: false).Text;

        Assert.Contains("Jarvis tool result (ListReminders): \"Nothing scheduled.\"", prompt);
        Assert.Contains("Jarvis tool result: \"Other.\"", prompt);
    }

    [Fact]
    public async Task Native_web_search_progress_reaches_the_stream_without_entering_the_transcript()
    {
        var model = new NativeSearchModel();
        var agent = new ChatClientAgent(model, new ChatClientAgentOptions());
        var session = await agent.CreateSessionAsync();

        var progress = new List<Jarvis.Application.Conversations.AgentToolProgress>();
        await foreach (var update in agent.RunStreamingAsync("What is the news?", session))
            if (NativeToolProgress.Read(update) is { } native) progress.Add(native);

        Assert.Equal(
            [("websearch-1", "WebSearch", "started"), ("websearch-1", "WebSearch", "completed")],
            progress.Select(item => (item.ToolCallId, item.ToolName, item.Phase)));
        var stored = (await agent.SerializeSessionAsync(session)).GetRawText();
        Assert.Contains("Here is the news.", stored);
        Assert.DoesNotContain("jarvis.native_tool", stored);
        Assert.DoesNotContain("WebSearch", stored);
    }

    [Fact]
    public void Native_tool_progress_ignores_unknown_phases()
    {
        var update = new AgentResponseUpdate(NativeToolProgress.Create("websearch-1", "WebSearch", "leaked query"));
        Assert.Null(NativeToolProgress.Read(update));
        Assert.Null(NativeToolProgress.Read(new AgentResponseUpdate(ChatRole.Assistant, "text")));
    }

    private sealed class NativeSearchModel : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Here is the news.")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return NativeToolProgress.Create("websearch-1", NativeToolProgress.WebSearch, "started");
            yield return NativeToolProgress.Create("websearch-1", NativeToolProgress.WebSearch, "completed");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Here is the news.");
        }
    }

    private static ChatClientAgent CreateAgent(IChatClient model, AITool tool)
    {
        var agent = new ChatClientAgent(model, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Tools = [tool], AllowMultipleToolCalls = false }
        });
        ToolFailureFeedback.Configure(agent.ChatClient.GetService<FunctionInvokingChatClient>());
        return agent;
    }

    /// <summary>Calls one tool with each scripted argument in turn, then answers "Done.".</summary>
    private sealed class RetryingModel(string toolName, IReadOnlyList<string?> arguments) : IChatClient
    {
        public List<string> ResultsSeen { get; } = [];
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
            ResultsSeen.Clear();
            ResultsSeen.AddRange(results.Select(result => result.Result?.ToString() ?? string.Empty));
            if (results.Length >= arguments.Count || (results.Length > 0 && !ResultsSeen[^1].Contains("rejected")))
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));

            var callArguments = arguments[results.Length] is { } value
                ? new Dictionary<string, object?> { ["when"] = value }
                : new Dictionary<string, object?>();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent(Guid.NewGuid().ToString("N"), toolName, callArguments)])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
                yield return new ChatResponseUpdate { Role = message.Role, Contents = message.Contents };
        }
    }
}
