using System.Reflection;
using System.Text.Json;
using Jarvis.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ToolGroupTests
{
    [Fact]
    public void Every_grouped_tool_is_a_real_agent_tool_in_exactly_one_group()
    {
        var methods = typeof(JarvisAgentFactory).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                                BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name[..^5] : method.Name)
            .ToHashSet(StringComparer.Ordinal);
        var names = ToolGroups.All.SelectMany(group => group.Tools).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.Contains(name, methods));
        Assert.Equal(ToolGroups.All.Count, ToolGroups.All.Select(group => group.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("I spent €12 on lunch", "money")]
    [InlineData("Hoeveel heb ik deze maand uitgegeven?", "money")]
    [InlineData("What's in my inbox?", "inbox")]
    [InlineData("Plan my day please", "planner")]
    [InlineData("Whose birthday is coming up?", "people")]
    [InlineData("Stuur een appje naar Sam", "whatsapp")]
    [InlineData("Connect Notion", "integrations")]
    [InlineData("Write this in my journal", "journal")]
    [InlineData("Save this article for later reading", "library")]
    [InlineData("Did I ever decide on the job offer?", "decisions")]
    [InlineData("Turn on focus mode", "modes")]
    [InlineData("How much battery does my phone have?", "devices")]
    [InlineData("What happened on this day last year?", "timeline")]
    [InlineData("Every morning at 8 send me the weather", "automations")]
    [InlineData("What's my net worth?", "money")]
    [InlineData("Did I keep my streak going?", "habits")]
    public void Keywords_pick_the_group_a_message_needs(string message, string group) =>
        Assert.Contains(group, ToolGroups.MatchKeywords(message).Select(match => match.Id));

    [Theory]
    [InlineData("Remind me to call the dentist at 5")]
    [InlineData("What do you remember about me?")]
    public void Everyday_requests_load_no_feature_groups(string message) =>
        Assert.Empty(ToolGroups.MatchKeywords(message));

    [Fact]
    public void A_plain_turn_offers_core_tools_and_hides_feature_groups()
    {
        var options = Options("CreateReminder", "GetBudgets", "LogExpense", "CheckInbox");

        var narrowed = ToolGroupChatClient.Narrow([new ChatMessage(ChatRole.User, "Remind me at five")], options)!;

        Assert.Equal(["CreateReminder", ToolGroups.LoadToolsName, "SomeMcpServerTool"], narrowed.Tools!.Select(tool => tool.Name));
        var callable = Assert.IsType<string[]>(narrowed.AdditionalProperties![ToolGroups.CallableToolsProperty]);
        Assert.Contains("GetBudgets", callable);
        Assert.Equal(6, options.Tools!.Count);
    }

    [Fact]
    public void Load_tools_calls_and_recent_tool_use_keep_a_group_offered()
    {
        var options = Options("CreateReminder", "GetBudgets", "CheckInbox", "GetHabits");
        List<ChatMessage> history =
        [
            new(ChatRole.User, "How are my habits?"),
            new(ChatRole.Assistant, [new FunctionCallContent("1", "GetHabits")]),
            new(ChatRole.User, "And then?"),
            new(ChatRole.Assistant, [new FunctionCallContent("2", ToolGroups.LoadToolsName,
                new Dictionary<string, object?> { ["groups"] = JsonSerializer.SerializeToElement(new[] { "inbox" }) })]),
        ];

        var offered = ToolGroupChatClient.Narrow(history, options)!.Tools!.Select(tool => tool.Name).ToArray();

        Assert.Contains("GetHabits", offered);
        Assert.Contains("CheckInbox", offered);
        Assert.DoesNotContain("GetBudgets", offered);
    }

    [Fact]
    public void A_group_drops_out_after_a_few_turns_without_use()
    {
        var options = Options("GetHabits");
        List<ChatMessage> history =
        [
            new(ChatRole.User, "Check in my habits"),
            new(ChatRole.Assistant, [new FunctionCallContent("1", "GetHabits")]),
            new(ChatRole.User, "Thanks"),
            new(ChatRole.User, "Something else"),
            new(ChatRole.User, "And one more"),
        ];

        Assert.DoesNotContain("GetHabits",
            ToolGroupChatClient.Narrow(history, options)!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task A_deferred_tool_the_model_calls_runs_and_its_group_is_offered_next()
    {
        var budgets = 0;
        var model = new ScriptedModel(["GetBudgets", null]);
        var agent = new ChatClientAgent(new ToolGroupChatClient(model), new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(() => "ok", "CreateReminder"),
                    AIFunctionFactory.Create(() => { budgets++; return "Groceries 300"; }, "GetBudgets"),
                    AIFunctionFactory.Create(() => "Lunch 12", "LogExpense"),
                    ToolGroups.CreateLoadTools(new HashSet<string> { "GetBudgets", "LogExpense" })
                ],
                AllowMultipleToolCalls = false
            }
        });

        var response = await agent.RunAsync("Remind me later");

        Assert.Equal(1, budgets);
        Assert.Equal("Done.", response.Text);
        Assert.DoesNotContain("GetBudgets", model.ToolsSeen[0]);
        Assert.Contains("GetBudgets", model.CallableSeen[0]);
        Assert.Contains("LogExpense", model.ToolsSeen[1]);
    }

    [Fact]
    public void Load_tools_describes_the_catalog_and_reports_what_it_loaded()
    {
        var tool = ToolGroups.CreateLoadTools(new HashSet<string> { "GetBudgets", "CheckInbox" });

        Assert.Contains("money (", tool.Description);
        Assert.Contains("inbox (", tool.Description);
        Assert.DoesNotContain("library (", tool.Description);
    }

    [Fact]
    public void Codex_accepts_calls_to_callable_tools_that_were_not_offered()
    {
        var options = new ChatOptions
        {
            AdditionalProperties = new() { [ToolGroups.CallableToolsProperty] = new[] { "GetBudgets" } }
        };
        var names = CodexCliChatClient.CallableToolNames([AIFunctionFactory.Create(() => "", "CreateReminder")], options);
        using var document = JsonDocument.Parse("""{"type":"tool_call","text":"","name":"GetBudgets","argumentsJson":"{}"}""");

        var message = CodexCliChatClient.ParseAssistantMessage(document.RootElement, names);

        Assert.Equal("GetBudgets", Assert.Single(message.Contents.OfType<FunctionCallContent>()).Name);
    }

    private static ChatOptions Options(params string[] names) => new()
    {
        Tools =
        [
            .. names.Select(name => (AITool)AIFunctionFactory.Create(() => "ok", name)),
            ToolGroups.CreateLoadTools(names.ToHashSet()),
            AIFunctionFactory.Create(() => "ok", "SomeMcpServerTool")
        ]
    };

    /// <summary>Calls each scripted tool in turn (null answers "Done."), recording what each call was shown.</summary>
    private sealed class ScriptedModel(IReadOnlyList<string?> calls) : IChatClient
    {
        public List<string[]> ToolsSeen { get; } = [];
        public List<string[]> CallableSeen { get; } = [];
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ToolsSeen.Add(options?.Tools?.Select(tool => tool.Name).ToArray() ?? []);
            CallableSeen.Add(options?.AdditionalProperties?.TryGetValue(ToolGroups.CallableToolsProperty, out var value) == true
                ? (string[])value! : []);
            var step = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Count();
            return Task.FromResult(step < calls.Count && calls[step] is { } name
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent(Guid.NewGuid().ToString("N"), name)]))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates()) yield return update;
        }
    }
}
