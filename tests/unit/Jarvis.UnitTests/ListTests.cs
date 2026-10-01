using Jarvis.Agents.Lists;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Lists;
using Jarvis.Domain.Lists;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ListTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000dddd");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000eeee");
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Boodschappen", "boodschappen")]
    [InlineData("de boodschappenlijst", "boodschappen")]
    [InlineData("My To-do list", "todo")]
    [InlineData("Crème brûlée lijstje", "cremebrulee")]
    [InlineData("List", "list")]
    public void Name_keys_ignore_list_suffixes_articles_and_accents(string name, string key) =>
        Assert.Equal(key, ListRules.NameKey(name));

    [Theory]
    [InlineData("Boodschappen", ListKinds.Shopping)]
    [InlineData("Groceries", ListKinds.Shopping)]
    [InlineData("To-do", ListKinds.Todo)]
    [InlineData("Klusjes", ListKinds.Todo)]
    [InlineData("Vakantie", ListKinds.General)]
    public void Kind_is_guessed_from_the_name(string name, string kind) => Assert.Equal(kind, ListKinds.Guess(name));

    [Fact]
    public async Task Adding_by_name_creates_a_shopping_list_once_and_reuses_it()
    {
        var (service, repository) = Create();

        var first = await service.AddItemsByNameAsync(Owner, "boodschappenlijst", ["melk"], default);
        var second = await service.AddItemsByNameAsync(Owner, "Boodschappen", ["brood", " Melk "], default);

        Assert.True(first.Value!.CreatedList);
        var list = Assert.Single(repository.Lists);
        Assert.Equal("boodschappenlijst", list.Name);
        Assert.Equal(ListKinds.Shopping, list.Kind);
        Assert.False(second.Value!.CreatedList);
        Assert.Equal(["melk", "brood"], list.Items.Select(x => x.Text));
        Assert.Equal(["Melk"], second.Value.AlreadyOnList);
    }

    [Fact]
    public async Task Shopping_requests_find_the_only_shopping_list_under_another_name()
    {
        var (service, _) = Create();
        await service.CreateAsync(Owner, "Albert Heijn", ListKinds.Shopping, default);

        var found = await service.FindAsync(Owner, "grocery list", default);

        Assert.Equal("Albert Heijn", found?.Name);
        Assert.Null(await service.FindAsync(Owner, "Vakantie", default));
    }

    [Fact]
    public async Task Checked_off_items_come_back_when_added_again()
    {
        var (service, repository) = Create();
        var list = (await service.AddItemsByNameAsync(Owner, "Boodschappen", ["melk"], default)).Value!.List;
        await service.SetDoneByTextAsync(Owner, list.Id, ["melk"], true, default);

        var result = await service.AddItemsAsync(Owner, list.Id, ["melk"], default);

        var item = Assert.Single(repository.Lists.Single().Items);
        Assert.False(item.IsDone);
        Assert.Null(item.DoneAt);
        Assert.Single(result.Value!.Reopened);
        Assert.Empty(result.Value.Added);
    }

    [Fact]
    public async Task Check_off_matches_whole_words_and_reports_misses_and_ambiguity()
    {
        var (service, repository) = Create();
        var list = (await service.AddItemsByNameAsync(Owner, "Boodschappen",
            ["2 liter melk", "halfvolle yoghurt", "volle yoghurt", "eieren"], default)).Value!.List;

        var result = (await service.SetDoneByTextAsync(Owner, list.Id, ["Melk", "yoghurt", "ei", "kaas"], true,
            default)).Value!;

        Assert.Equal(["2 liter melk"], result.Changed.Select(x => x.Text));
        Assert.Equal("yoghurt", Assert.Single(result.Ambiguous).Text);
        Assert.Equal(["ei", "kaas"], result.NotFound);
        Assert.True(repository.Lists.Single().Items.Single(x => x.Text == "2 liter melk").IsDone);

        var again = (await service.SetDoneByTextAsync(Owner, list.Id, ["melk"], true, default)).Value!;
        Assert.Equal(["melk"], again.Unchanged);
    }

    [Fact]
    public async Task Clearing_checked_items_keeps_open_ones()
    {
        var (service, repository) = Create();
        var list = (await service.AddItemsByNameAsync(Owner, "To-do", ["bel loodgieter", "was ophangen"], default))
            .Value!.List;
        await service.SetDoneByTextAsync(Owner, list.Id, ["was"], true, default);

        Assert.Equal(1, await service.ClearDoneAsync(Owner, list.Id, default));
        Assert.Equal(["bel loodgieter"], repository.Lists.Single().Items.Select(x => x.Text));
    }

    [Fact]
    public async Task Lists_are_owner_scoped()
    {
        var (service, _) = Create();
        var list = (await service.CreateAsync(Owner, "Boodschappen", null, default)).Value!;

        Assert.Null(await service.GetAsync(list.Id, Other, default));
        Assert.Empty(await service.ListAsync(Other, default));
        Assert.Null(await service.FindAsync(Other, "Boodschappen", default));
        Assert.Equal(ListFailure.NotFound, (await service.AddItemsAsync(Other, list.Id, ["melk"], default)).Failure);
        Assert.False(await service.DeleteAsync(list.Id, Other, default));
        Assert.Null(await service.ClearDoneAsync(Other, list.Id, default));
    }

    [Fact]
    public async Task Names_must_be_unique_and_valid()
    {
        var (service, _) = Create();
        await service.CreateAsync(Owner, "Boodschappen", null, default);

        Assert.Equal(ListFailure.Conflict,
            (await service.CreateAsync(Owner, "boodschappenlijst", null, default)).Failure);
        Assert.Equal(ListFailure.Invalid, (await service.CreateAsync(Owner, "  ", null, default)).Failure);
        Assert.Equal(ListFailure.Invalid, (await service.CreateAsync(Owner, "Test", "recipes", default)).Failure);
        Assert.True((await service.CreateAsync(Other, "Boodschappen", null, default)).Succeeded);
    }

    [Fact]
    public async Task Items_are_limited_in_length()
    {
        var (service, _) = Create();
        var list = (await service.CreateAsync(Owner, "To-do", null, default)).Value!;

        var result = await service.AddItemsAsync(Owner, list.Id, [new string('a', ListRules.MaxItemLength + 1)],
            default);

        Assert.Equal(ListFailure.Invalid, result.Failure);
    }

    [Fact]
    public async Task Tools_add_check_off_and_read_back_without_putting_item_text_in_the_audit_log()
    {
        var (service, repository) = Create();
        var audit = new RecordingAudit();
        var tools = new ListAgentTools(service, audit, new FixedUser(), NullLogger.Instance);

        var added = await tools.AddToListAsync("boodschappenlijst", ["melk", "appels"]);
        var checkedOff = await tools.CheckOffListItemsAsync("boodschappen", ["appels"]);
        var read = await tools.GetListsAsync("boodschappen");

        Assert.Contains("Created the list", added);
        Assert.Contains("\"melk\" and \"appels\"", added);
        Assert.Contains("Checked off \"appels\"", checkedOff);
        Assert.Contains("1 item is still open", checkedOff);
        Assert.Contains("- [ ] melk", read);
        Assert.Contains("- [x] appels", read);
        Assert.Equal(["list.created", "list.items_added", "list.items_checked"], audit.Actions);
        Assert.DoesNotContain(audit.Metadata, metadata => metadata.Contains("melk") || metadata.Contains("appels"));
        Assert.Single(repository.Lists);
    }

    [Fact]
    public async Task Tools_name_existing_lists_when_one_is_not_found()
    {
        var (service, _) = Create();
        var tools = new ListAgentTools(service, new RecordingAudit(), new FixedUser(), NullLogger.Instance);
        await tools.AddToListAsync("Vakantie", ["paspoort"]);

        var result = await tools.CheckOffListItemsAsync("Kerst", ["boom"]);

        Assert.Contains("no list called \"Kerst\"", result);
        Assert.Contains("\"Vakantie\"", result);
    }

    private static (ListService Service, FakeListRepository Repository) Create()
    {
        var repository = new FakeListRepository();
        return (new ListService(repository, new FixedClock(Now)), repository);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingAudit : IAuditEventStore
    {
        public List<string> Actions { get; } = [];
        public List<string> Metadata { get; } = [];

        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null)
        {
            Actions.Add(action);
            Metadata.Add(metadataJson ?? "");
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass,
                approvalId, DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeListRepository : IListRepository
    {
        public List<PersonalList> Lists { get; } = [];

        public Task<IReadOnlyList<PersonalList>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersonalList>>(Lists.Where(x => x.OwnerId == ownerId)
                .OrderByDescending(x => x.UpdatedAt).ToArray());

        public Task<PersonalList?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Lists.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddListAsync(PersonalList list, CancellationToken cancellationToken)
        {
            Lists.Add(list);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateListAsync(Guid id, Guid ownerId, string name, string kind, DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            var index = Lists.FindIndex(x => x.Id == id && x.OwnerId == ownerId);
            if (index < 0) return Task.FromResult(false);
            Lists[index] = Lists[index] with { Name = name, Kind = kind, UpdatedAt = updatedAt };
            return Task.FromResult(true);
        }

        public Task<bool> DeleteListAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Lists.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task<bool> SaveItemsAsync(Guid ownerId, Guid listId, IReadOnlyList<ListItem> added,
            IReadOnlyList<ListItem> changed, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        {
            var index = Lists.FindIndex(x => x.Id == listId && x.OwnerId == ownerId);
            if (index < 0) return Task.FromResult(false);
            var byId = changed.ToDictionary(x => x.Id);
            Lists[index] = Lists[index] with
            {
                Items = Lists[index].Items.Select(x => byId.GetValueOrDefault(x.Id, x)).Concat(added).ToArray(),
                UpdatedAt = updatedAt
            };
            return Task.FromResult(true);
        }

        public Task<int> DeleteItemsAsync(Guid ownerId, Guid listId, IReadOnlyCollection<Guid> itemIds,
            DateTimeOffset updatedAt, CancellationToken cancellationToken)
        {
            var index = Lists.FindIndex(x => x.Id == listId && x.OwnerId == ownerId);
            if (index < 0) return Task.FromResult(0);
            var before = Lists[index].Items.Count;
            Lists[index] = Lists[index] with
            {
                Items = Lists[index].Items.Where(x => !itemIds.Contains(x.Id)).ToArray(), UpdatedAt = updatedAt
            };
            return Task.FromResult(before - Lists[index].Items.Count);
        }
    }
}
