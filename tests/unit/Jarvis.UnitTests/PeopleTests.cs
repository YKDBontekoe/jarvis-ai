using Jarvis.Agents;
using Jarvis.Agents.People;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.People;
using Jarvis.Application.Workflows;
using Jarvis.Domain.People;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PeopleTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("1990-03-14", 3, 14, 1990)]
    [InlineData("--03-14", 3, 14, null)]
    [InlineData("14 maart", 3, 14, null)]
    [InlineData("14 maart 1990", 3, 14, 1990)]
    [InlineData("March 14, 1990", 3, 14, 1990)]
    [InlineData("the 14th of March", 3, 14, null)]
    [InlineData("14-03", 3, 14, null)]
    [InlineData("3/14", 3, 14, null)]
    [InlineData("05-06-1985", 6, 5, 1985)]
    [InlineData("29 februari", 2, 29, null)]
    [InlineData("1e mei", 5, 1, null)]
    public void Birthdays_are_read_in_common_formats(string text, int month, int day, int? year) =>
        Assert.Equal(new Birthday(month, day, year), Birthday.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("31 februari")]
    [InlineData("29 februari 2023")]
    [InlineData("14 maart 2999")]
    [InlineData("13/13")]
    public void Nonsense_birthdays_are_rejected(string text) => Assert.Null(Birthday.Parse(text));

    [Fact]
    public void Leap_day_birthdays_fall_on_the_28th_in_other_years()
    {
        var person = PersonWith(birthdayMonth: 2, birthdayDay: 29, birthYear: 2000);

        Assert.Equal(new DateOnly(2027, 2, 28), PeopleCalendar.NextBirthday(person, new DateOnly(2026, 10, 1)));
        Assert.Equal(new DateOnly(2028, 2, 29), PeopleCalendar.NextBirthday(person, new DateOnly(2027, 3, 1)));
        Assert.Equal(27, PeopleCalendar.AgeOn(person, new DateOnly(2027, 2, 28)));
    }

    [Fact]
    public void A_birthday_today_is_next_and_yesterday_is_a_year_away()
    {
        var person = PersonWith(birthdayMonth: 10, birthdayDay: 1);

        Assert.Equal(0, PeopleCalendar.DaysUntilBirthday(person, new DateOnly(2026, 10, 1)));
        Assert.Equal(364, PeopleCalendar.DaysUntilBirthday(person, new DateOnly(2026, 10, 2)));
    }

    [Theory]
    [InlineData(1, "every day")]
    [InlineData(7, "every week")]
    [InlineData(14, "every 2 weeks")]
    [InlineData(30, "every month")]
    [InlineData(90, "every 3 months")]
    [InlineData(10, "every 10 days")]
    public void Cadences_read_naturally(int days, string text) =>
        Assert.Equal(text, PeopleCalendar.DescribeCadence(days));

    [Theory]
    [InlineData("sister_of", true, "Sister")]
    [InlineData("is_best_friend_of", true, "Best friend")]
    [InlineData("has_mother", false, "Mother")]
    [InlineData("works_at", true, null)]
    [InlineData("likes", false, null)]
    public void Graph_predicates_become_relationships(string predicate, bool personIsSubject, string? expected) =>
        Assert.Equal(expected, PeopleGraph.Relationship(predicate, personIsSubject));

    [Fact]
    public async Task Names_are_unique_per_owner_ignoring_case_and_accents()
    {
        var (service, repository, _) = Create();

        Assert.True((await service.CreateAsync(Owner, Input("Zoë"), default)).Succeeded);
        var duplicate = await service.CreateAsync(Owner, Input(" zoe "), default);
        var otherOwner = await service.CreateAsync(Other, Input("Zoe"), default);

        Assert.Equal(PeopleFailure.Conflict, duplicate.Failure);
        Assert.True(otherOwner.Succeeded);
        Assert.Equal(2, repository.People.Count);
    }

    [Fact]
    public async Task Invalid_input_names_the_field()
    {
        var (service, _, _) = Create();

        Assert.Equal("name", (await service.CreateAsync(Owner, Input("  "), default)).Field);
        Assert.Equal("birthday", (await service.CreateAsync(Owner, Input("Anna") with { BirthdayMonth = 3 }, default)).Field);
        Assert.Equal("birthday", (await service.CreateAsync(Owner,
            Input("Anna") with { BirthdayMonth = 2, BirthdayDay = 30 }, default)).Field);
        Assert.Equal("contactEveryDays",
            (await service.CreateAsync(Owner, Input("Anna") with { ContactEveryDays = 0 }, default)).Field);
    }

    [Fact]
    public async Task People_are_found_by_name_relationship_or_unique_first_name()
    {
        var (service, _, _) = Create();
        await service.CreateAsync(Owner, Input("Ingrid") with { Relationship = "Mama" }, default);
        await service.CreateAsync(Owner, Input("Anna de Vries"), default);
        await service.CreateAsync(Owner, Input("Tom Bakker"), default);
        await service.CreateAsync(Owner, Input("Tom Jansen"), default);

        Assert.Equal("Ingrid", (await service.FindAsync(Owner, "mijn mama", default))?.Name);
        Assert.Equal("Anna de Vries", (await service.FindAsync(Owner, "anna", default))?.Name);
        Assert.Equal("Tom Jansen", (await service.FindAsync(Owner, "Tom Jansen", default))?.Name);
        Assert.Null(await service.FindAsync(Owner, "Tom", default));
        Assert.Null(await service.FindAsync(Other, "anna", default));
    }

    [Fact]
    public async Task New_people_link_to_the_graph_person_with_the_same_name_and_schedule_check_ins()
    {
        var graph = new FakeGraph();
        var anna = graph.AddPerson("Anna");
        var scheduler = new RecordingScheduler();
        var (service, _, _) = Create(graph, scheduler);

        var created = await service.CreateAsync(Owner, Input("anna") with { BirthdayMonth = 3, BirthdayDay = 14 },
            default);
        await service.CreateAsync(Owner, Input("Bob"), default);

        Assert.Equal(anna, created.Value!.GraphEntityId);
        Assert.Equal([Owner], scheduler.Owners);
    }

    [Fact]
    public async Task Suggestions_come_from_graph_people_not_yet_on_the_list()
    {
        var graph = new FakeGraph();
        var user = graph.Add("user", "person");
        var mama = graph.AddPerson("Ingrid");
        graph.AddPerson("Anna");
        graph.Add("Albert Heijn", "organization");
        graph.Edges.Add(new GraphEdge(mama, user, "mother_of", 0.9f, Now));
        graph.Literals.Add(new GraphLiteral(mama, "birthday", "14 maart 1961", 0.9f, Now));
        var (service, _, _) = Create(graph);
        await service.CreateAsync(Owner, Input("Anna"), default);

        var suggestion = Assert.Single(await service.SuggestAsync(Owner, default));

        Assert.Equal("Ingrid", suggestion.Name);
        Assert.Equal("Mother", suggestion.Relationship);
        Assert.Equal(new Birthday(3, 14, 1961), suggestion.Birthday);
    }

    [Fact]
    public async Task Logging_contact_keeps_the_most_recent_moment_and_rejects_the_future()
    {
        var (service, _, _) = Create();
        var person = (await service.CreateAsync(Owner, Input("Anna"), default)).Value!;

        await service.LogContactAsync(person.Id, Owner, Now.AddHours(-1), default);
        var older = await service.LogContactAsync(person.Id, Owner, Now.AddDays(-3), default);
        var future = await service.LogContactAsync(person.Id, Owner, Now.AddDays(1), default);

        Assert.Equal(Now.AddHours(-1), older.Value!.LastContactedAt);
        Assert.Equal(PeopleFailure.Invalid, future.Failure);
        Assert.Equal(PeopleFailure.NotFound, (await service.LogContactAsync(person.Id, Other, null, default)).Failure);
    }

    [Fact]
    public async Task Check_in_waits_until_nine_in_the_owner_zone()
    {
        var repository = new FakePeopleRepository();
        repository.People.Add(PersonWith(birthdayMonth: 10, birthdayDay: 1));
        // 06:00 UTC is 08:00 in Amsterdam during summer time.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero));
        var notifications = new RecordingNotifications();
        var service = new PeopleCheckInService(repository, notifications, new FixedBriefings("Europe/Amsterdam"), clock);

        var result = await service.RunAsync(Owner, default);

        Assert.True(result.Continue);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero), result.NextRunAt);
        Assert.Empty(notifications.Created);
    }

    [Fact]
    public async Task Birthdays_notify_once_on_the_day_with_the_age()
    {
        var repository = new FakePeopleRepository();
        var anna = PersonWith("Anna", birthdayMonth: 10, birthdayDay: 1, birthYear: 1996);
        repository.People.Add(anna);
        repository.People.Add(PersonWith("Bob", birthdayMonth: 10, birthdayDay: 2));
        var clock = new FakeTimeProvider(Now);
        var notifications = new RecordingNotifications();
        var service = new PeopleCheckInService(repository, notifications, new FixedBriefings("UTC"), clock);

        var first = await service.RunAsync(Owner, default);
        var second = await service.RunAsync(Owner, default);

        var notification = Assert.Single(notifications.Created);
        Assert.Equal(PeopleCheckInService.BirthdayType, notification.Type);
        Assert.Equal("It's Anna's birthday today", notification.Title);
        Assert.Contains("turns 30", notification.Body);
        Assert.Equal(anna.Id, notification.SourceId);
        Assert.Equal(1, first.BirthdaysNotified);
        Assert.Equal(0, second.BirthdaysNotified);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), first.NextRunAt);
    }

    [Fact]
    public async Task Check_in_nudges_repeat_weekly_until_contact_is_logged()
    {
        var repository = new FakePeopleRepository();
        repository.People.Add(PersonWith("Mama", contactEveryDays: 14, lastContactedAt: Now.AddDays(-15)));
        repository.People.Add(PersonWith("Anna", contactEveryDays: 14, lastContactedAt: Now.AddDays(-3)));
        var clock = new FakeTimeProvider(Now);
        var notifications = new RecordingNotifications();
        var service = new PeopleCheckInService(repository, notifications, new FixedBriefings(null), clock);

        await service.RunAsync(Owner, default);
        clock.Advance(TimeSpan.FromDays(1));
        await service.RunAsync(Owner, default);
        clock.Advance(TimeSpan.FromDays(6));
        await service.RunAsync(Owner, default);

        Assert.Equal(2, notifications.Created.Count);
        Assert.All(notifications.Created, x => Assert.Equal("Time to check in with Mama", x.Title));
        Assert.Contains("every 2 weeks", notifications.Created[0].Body);
        Assert.Contains("2 weeks ago", notifications.Created[0].Body);
    }

    [Fact]
    public async Task Several_due_people_share_one_nudge()
    {
        var repository = new FakePeopleRepository();
        repository.People.Add(PersonWith("Mama", contactEveryDays: 14, lastContactedAt: Now.AddDays(-20)));
        repository.People.Add(PersonWith("Papa", contactEveryDays: 7, lastContactedAt: Now.AddDays(-8)));
        repository.People.Add(PersonWith("Opa", contactEveryDays: 30));
        var notifications = new RecordingNotifications();
        var service = new PeopleCheckInService(repository, notifications, new FixedBriefings("UTC"),
            new FakeTimeProvider(Now.AddDays(31)));

        var result = await service.RunAsync(Owner, default);

        var notification = Assert.Single(notifications.Created);
        Assert.Equal(3, result.CheckInsNotified);
        Assert.Equal("Time to check in with Mama and 2 others", notification.Title);
        Assert.Null(notification.SourceId);
    }

    [Fact]
    public async Task Check_in_stops_when_nobody_needs_one()
    {
        var repository = new FakePeopleRepository();
        repository.People.Add(PersonWith("Anna"));
        var service = new PeopleCheckInService(repository, new RecordingNotifications(), new FixedBriefings("UTC"),
            new FakeTimeProvider(Now));

        Assert.False((await service.RunAsync(Owner, default)).Continue);
    }

    [Fact]
    public async Task Agent_saves_birthdays_and_cadences_and_keeps_earlier_fields()
    {
        var (service, repository, _) = Create();
        var tools = Tools(service);

        var added = await tools.SavePersonAsync("Mama", relationship: "Mother", birthday: "14 maart 1961");
        var updated = await tools.SavePersonAsync("mama", contactEveryDays: 14, note: "Likes tulips");
        await tools.SavePersonAsync("Mama", note: "New phone number in contacts");

        var person = Assert.Single(repository.People);
        Assert.StartsWith("Added Mama.", added);
        Assert.Contains("every 2 weeks", updated);
        Assert.Equal("Mother", person.Relationship);
        Assert.Equal((3, 14, 1961), (person.BirthdayMonth, person.BirthdayDay, person.BirthYear));
        Assert.Equal(14, person.ContactEveryDays);
        Assert.Equal("Likes tulips\nNew phone number in contacts", person.Notes);
    }

    [Fact]
    public async Task Agent_logs_contact_by_relationship_and_reports_unknown_people()
    {
        var (service, repository, _) = Create();
        var tools = Tools(service);
        await tools.SavePersonAsync("Ingrid", relationship: "Mama", contactEveryDays: 14);

        var logged = await tools.LogContactAsync("mama", "yesterday");
        var unknown = await tools.LogContactAsync("Bob");

        Assert.Contains("in touch with Ingrid", logged);
        Assert.Equal(Now.AddDays(-1), repository.People[0].LastContactedAt);
        Assert.Contains("Ingrid (Mama)", unknown);
    }

    [Fact]
    public async Task Agent_overview_lists_upcoming_birthdays_and_due_check_ins()
    {
        var (service, _, _) = Create();
        var tools = Tools(service);
        await tools.SavePersonAsync("Anna", birthday: "3 October 1996");
        await service.CreateAsync(Owner, Input("Mama") with
        {
            ContactEveryDays = 7, LastContactedAt = Now.AddDays(-10)
        }, default);

        var overview = await tools.GetPeopleAsync();

        Assert.Contains("Anna's birthday is 3 October (in 2 days, turning 30)", overview);
        Assert.Contains("Due for a check-in:", overview);
        Assert.Contains("Mama: last in touch 10 days ago", overview);
    }

    [Fact]
    public void Removing_a_person_needs_approval()
    {
        var (service, _, _) = Create();
        var contributor = new PeopleToolContributor(service, new NullAudit(), new FixedUser(Owner),
            NullLoggerFactory.Instance, new FakeTimeProvider(Now));

        var tools = contributor.GetTools(new AgentBuildContext(Owner, null)).ToArray();

        Assert.IsType<Microsoft.Extensions.AI.ApprovalRequiredAIFunction>(
            tools.Single(x => x.Name == "RemovePerson"));
        Assert.All(tools.Where(x => x.Name != "RemovePerson"),
            x => Assert.IsNotType<Microsoft.Extensions.AI.ApprovalRequiredAIFunction>(x));
    }

    private static PeopleAgentTools Tools(IPeopleService service) =>
        new(service, new NullAudit(), new FixedUser(Owner), NullLogger.Instance, new FakeTimeProvider(Now));

    private static (PeopleService Service, FakePeopleRepository Repository, FakeGraph Graph) Create(
        FakeGraph? graph = null, IPeopleCheckInScheduler? scheduler = null)
    {
        var repository = new FakePeopleRepository();
        graph ??= new FakeGraph();
        return (new PeopleService(repository, graph, new FixedBriefings("UTC"), scheduler, new FakeTimeProvider(Now)),
            repository, graph);
    }

    private static PersonInput Input(string name) => new(name, null, null, null, null, null, null);

    private static Person PersonWith(string name = "Anna", int? birthdayMonth = null, int? birthdayDay = null,
        int? birthYear = null, int? contactEveryDays = null, DateTimeOffset? lastContactedAt = null) =>
        new(Guid.CreateVersion7(), Owner, name, null, birthdayMonth, birthdayDay, birthYear, null, contactEveryDays,
            lastContactedAt, null, null, null, Now.AddDays(-30), Now.AddDays(-30));

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class FixedUser(Guid ownerId) : ICurrentUser
    {
        public Guid OwnerId => ownerId;
    }

    private sealed class RecordingScheduler : IPeopleCheckInScheduler
    {
        public List<Guid> Owners { get; } = [];

        public Task SchedulePeopleCheckInAsync(Guid ownerId, CancellationToken cancellationToken)
        {
            Owners.Add(ownerId);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedBriefings(string? timeZoneId) : IDailyBriefingRepository
    {
        public Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(timeZoneId is null
                ? null
                : new DailyBriefingPreferenceRecord(ownerId, true, new TimeOnly(7, 0), timeZoneId, "w", null, null));

        public Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(Guid ownerId,
            SaveDailyBriefingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    internal sealed class FakePeopleRepository : IPeopleRepository
    {
        public List<Person> People { get; } = [];

        public Task<IReadOnlyList<Person>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Person>>(People.Where(x => x.OwnerId == ownerId)
                .OrderBy(x => PeopleRules.NameKey(x.Name)).ToArray());

        public Task<Person?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(People.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddAsync(Person person, CancellationToken cancellationToken)
        {
            People.Add(person);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Person person, CancellationToken cancellationToken)
        {
            var index = People.FindIndex(x => x.Id == person.Id && x.OwnerId == person.OwnerId);
            if (index < 0) return Task.FromResult(false);
            People[index] = person;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(People.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task<IReadOnlyList<Guid>> ListOwnersWithCheckInsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(People.Where(x => x.NeedsCheckIns).Select(x => x.OwnerId)
                .Distinct().ToArray());

        public Task MarkNotifiedAsync(Guid ownerId, IReadOnlyCollection<Guid> birthdayIds, int birthdayYear,
            IReadOnlyCollection<Guid> nudgedIds, DateTimeOffset nudgedAt, CancellationToken cancellationToken)
        {
            for (var i = 0; i < People.Count; i++)
            {
                if (People[i].OwnerId != ownerId) continue;
                if (birthdayIds.Contains(People[i].Id)) People[i] = People[i] with { BirthdayNotifiedYear = birthdayYear };
                if (nudgedIds.Contains(People[i].Id)) People[i] = People[i] with { CheckInNudgedAt = nudgedAt };
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGraph : IKnowledgeGraphRepository
    {
        public List<GraphEntityRecord> Entities { get; } = [];
        public List<GraphEdge> Edges { get; } = [];
        public List<GraphLiteral> Literals { get; } = [];

        public Guid AddPerson(string name) => Add(name, "person");

        public Guid Add(string name, string type)
        {
            var id = Guid.NewGuid();
            Entities.Add(new GraphEntityRecord(id, name, type, null, [], 0, Now));
            return id;
        }

        public Task<IReadOnlyList<GraphEntityRecord>> ListEntitiesAsync(Guid ownerId, string? search, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GraphEntityRecord>>(Entities);

        public Task<GraphEntityDetails?> GetEntityAsync(Guid ownerId, Guid entityId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Entities.FirstOrDefault(x => x.Id == entityId) is { } entity
                ? new GraphEntityDetails(entity, [], [])
                : null);

        public Task<GraphEntityDetails?> FindEntityAsync(Guid ownerId, string name, DateTimeOffset? asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(Entities.FirstOrDefault(x => GraphNames.Key(x.Name) == GraphNames.Key(name)) is { } entity
                ? new GraphEntityDetails(entity, [], [])
                : null);

        public Task<IReadOnlyList<GraphEntityRecord>> FindMentionedAsync(Guid ownerId, string text, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GraphEntityRecord>>([]);

        public Task<GraphOverview> GetOverviewAsync(Guid ownerId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new GraphOverview(Entities, Edges, Literals));

        public Task<int> MergeAsync(Guid ownerId, IReadOnlyList<GraphFact> facts, Guid? sourceMemoryId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GraphEntityRecord?> UpdateEntityAsync(Guid ownerId, Guid entityId, string? name, string? type,
            string? summary, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CloseRelationAsync(Guid ownerId, Guid relationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> MergeEntitiesAsync(Guid ownerId, Guid keepId, Guid absorbId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteEntityAsync(Guid ownerId, Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
