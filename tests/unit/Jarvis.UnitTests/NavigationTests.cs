using System.Reflection;
using Jarvis.Agents.Navigation;
using Jarvis.Application.Channels;
using Jarvis.Application.Navigation;
using Jarvis.Application.Search;
using Jarvis.Application.WhatsApp;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class NavigationTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid SecondAccount = Guid.NewGuid();
    private readonly List<(string Method, Guid Owner)> _reads = [];
    private readonly List<WhatsAppChatSettings> _chats = [];
    private readonly List<ChannelConnectionRecord> _accounts = [Connection(Account), Connection(SecondAccount)];
    private readonly Dictionary<Guid, IReadOnlyList<WhatsAppChatActivity>> _activity = [];
    private readonly Search _search = new();

    [Theory]
    [InlineData("open", "today")]
    [InlineData("open", "expenses")]
    public async Task General_views_are_validated_and_have_no_mutations(string kind, string destination)
    {
        var response = await Service(new(kind, destination)).ResolveAsync(Owner, "Show me my day", default);
        var action = Assert.Single(response.Actions);
        Assert.Equal("utility", action.Route.Kind);
        Assert.Equal(destination, action.Route.Parameters["destination"]);
        Assert.Empty(_reads);
    }

    [Fact]
    public async Task Multiple_people_or_accounts_require_a_choice_and_preserve_the_request()
    {
        _chats.Add(Chat("+31611111111", "Sanne de Vries"));
        _chats.Add(Chat("+31622222222", "Sanne", SecondAccount));
        const string request = "Help me tell Sanne that tomorrow doesn't work";
        var response = await Service(new("reply", Query: "Sanne")).ResolveAsync(Owner, request, default);
        Assert.Equal("Which conversation do you mean?", response.Message);
        Assert.Equal(2, response.Actions.Count);
        Assert.Equal([Account.ToString(), SecondAccount.ToString()], response.Actions.Select(action => action.Route.Parameters["connectionId"]));
        Assert.All(response.Actions, action => {
            Assert.Equal("draft", action.Route.Parameters["workflow"]);
            Assert.Equal(request, action.Route.Parameters["request"]);
        });
        Assert.All(_reads, read => Assert.Equal(Owner, read.Owner));
    }

    [Fact]
    public async Task Disabled_read_along_foreign_owners_and_foreign_connections_are_excluded()
    {
        _chats.Add(Chat("+31611111111", "Sanne") with { ReadAlong = false });
        _chats.Add(Chat("+31622222222", "Sanne") with { OwnerId = Other });
        _chats.Add(Chat("+31633333333", "Sanne", Guid.NewGuid()));
        var response = await Service(new("reply", Query: "Sanne")).ResolveAsync(Owner, "Reply to Sanne", default);
        Assert.Equal("utility", Assert.Single(response.Actions).Route.Kind);
        Assert.Equal("whatsapp", response.Actions[0].Route.Parameters["destination"]);
    }

    [Theory]
    [InlineData("jose")]
    [InlineData("+31 6 1111 1111")]
    public async Task Chat_names_support_accents_and_formatted_phone_numbers(string query)
    {
        _chats.Add(Chat("+31611111111", "José"));
        var action = Assert.Single((await Service(new("whatsapp", Query: query)).ResolveAsync(Owner, "Open José", default)).Actions);
        Assert.Equal("whatsapp_chat", action.Route.Kind);
        Assert.Equal("+31611111111", action.Route.Parameters["chatId"]);
        Assert.False(action.Route.Parameters.ContainsKey("workflow"));
    }

    [Fact]
    public async Task Chat_questions_open_the_right_workflow_with_the_original_question()
    {
        _chats.Add(Chat("+31611111111", "Piet"));
        const string request = "What did Piet say about Friday?";
        var action = Assert.Single((await Service(new("ask_whatsapp", Query: "Piet")).ResolveAsync(Owner, request, default)).Actions);
        Assert.Equal("ask", action.Route.Parameters["workflow"]);
        Assert.Equal(request, action.Route.Parameters["request"]);
    }

    [Fact]
    public async Task Action_requests_go_to_the_assistant_without_executing_them_during_resolution()
    {
        const string request = "Remind me to call Piet tomorrow";
        var action = Assert.Single((await Service(new("assistant")).ResolveAsync(Owner, request, default)).Actions);
        Assert.Equal("assistant", action.Route.Kind);
        Assert.Equal(request, action.Route.Parameters["prompt"]);
        Assert.Empty(_reads);
    }

    [Fact]
    public async Task Missing_model_and_unknown_destinations_fall_back_without_inventing_navigation()
    {
        foreach (var intent in new NavigationIntent?[] { null, new("open", "delete-all"), new("execute") })
        {
            var result = await Service(intent).ResolveAsync(Owner, "Some request", default);
            Assert.False(result.Understood);
            Assert.Equal("assistant", Assert.Single(result.Actions).Route.Kind);
        }
    }

    [Fact]
    public async Task Finding_saved_items_uses_the_extracted_query_and_owner_scope()
    {
        var route = new SearchRouteTarget("task", new Dictionary<string, string> { ["taskId"] = "real-task" });
        _search.Results = [new("task", "real-task", "Aurora deployment", null, null, route, 1, false)];
        var action = Assert.Single((await Service(new("find", Query: "Aurora")).ResolveAsync(Owner, "Find the Aurora deployment task", default)).Actions);
        Assert.Equal((Owner, "Aurora"), Assert.Single(_search.Calls));
        Assert.Same(route, action.Route);
    }

    [Fact]
    public async Task Suggestions_are_grounded_in_unread_activity_and_do_not_mark_messages_read()
    {
        _chats.Add(Chat("+31611111111", "Piet"));
        _chats.Add(Chat("+31622222222", "Sanne") with { ReadAlong = false });
        _activity[Account] = [new("+31611111111", "private message", false, 3), new("+31622222222", "off", false, 9)];
        var result = await Service(null).SuggestionsAsync(Owner, default);
        Assert.Equal(2, result.Actions.Count); // One actual unread chat, plus the planner.
        Assert.Contains("3 unread", result.Actions[0].Description);
        Assert.DoesNotContain("private message", result.Actions[0].Description);
        Assert.Equal("+31611111111", result.Actions[0].Route.Parameters["chatId"]);
        Assert.All(_reads, read => Assert.Equal(Owner, read.Owner));
        Assert.DoesNotContain(_reads, read => read.Method == "MarkReadAsync");
    }

    [Fact]
    public async Task Empty_owners_and_oversized_requests_fail_before_inference()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service(null).ResolveAsync(Guid.Empty, "a", default));
        await Assert.ThrowsAsync<ArgumentException>(() => Service(null).ResolveAsync(Owner, new string('a', 1001), default));
    }

    [Theory]
    [InlineData("{\"kind\":\"open\",\"destination\":\"today\"}", "open")]
    [InlineData("```json\n{\"kind\":\"reply\",\"query\":\"Sanne\"}\n```", "reply")]
    [InlineData("{\"kind\":\"open\",\"destination\":\"https://evil.example\"}", null)]
    [InlineData("{\"kind\":\"send\",\"query\":\"Sanne\"}", null)]
    [InlineData("{\"kind\":7}", null)]
    [InlineData("not json", null)]
    public void Interpreter_accepts_only_the_workflow_schema(string text, string? kind) =>
        Assert.Equal(kind, NavigationIntentInterpreter.Parse(text)?.Kind);

    private NavigationService Service(NavigationIntent? intent) => new(new Interpreter(intent), _search,
        Proxy<IWhatsAppAssistantRepository>((method, args) => {
            _reads.Add((method.Name, (Guid)args[0]!));
            return method.Name switch {
                "ListChatsAsync" => Task.FromResult<IReadOnlyList<WhatsAppChatSettings>>(_chats),
                "ListActivityAsync" => Task.FromResult(_activity.GetValueOrDefault((Guid)args[1]!, [])),
                _ => throw new InvalidOperationException("Unexpected write: " + method.Name)
            };
        }), Proxy<IChannelRepository>((method, args) => {
            _reads.Add((method.Name, (Guid)args[0]!));
            if (method.Name != "ListAsync") throw new InvalidOperationException("Unexpected write");
            return Task.FromResult<IReadOnlyList<ChannelConnectionRecord>>(_accounts);
        }));

    private static WhatsAppChatSettings Chat(string id, string name, Guid? account = null) =>
        new(Guid.NewGuid(), Owner, account ?? Account, id, name, false, true, true, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static ChannelConnectionRecord Connection(Guid id) =>
        new(id, Owner, ChannelKinds.WhatsAppLinked, "My WhatsApp", id == Account ? "+31600000000" : "+31699999999",
            true, [], false, null, "", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.MinValue);
    private sealed class Interpreter(NavigationIntent? intent) : INavigationIntentInterpreter
    {
        public Task<NavigationIntent?> InterpretAsync(Guid ownerId, string request, CancellationToken ct) => Task.FromResult(intent);
    }
    private sealed class Search : IFederatedSearchService
    {
        public IReadOnlyList<FederatedSearchResult> Results = [];
        public readonly List<(Guid Owner, string Query)> Calls = [];
        public Task<FederatedSearchResponse> SearchAsync(Guid ownerId, string query, IReadOnlySet<string>? kinds, CancellationToken ct)
        {
            Calls.Add((ownerId, query));
            return Task.FromResult(new FederatedSearchResponse(Results, []));
        }
    }
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)proxy).Call = call;
        return proxy;
    }
    public class RepositoryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args!);
    }
}
