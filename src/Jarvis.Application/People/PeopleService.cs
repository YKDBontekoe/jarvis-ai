using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Domain.People;

namespace Jarvis.Application.People;

public sealed class PeopleService(
    IPeopleRepository people,
    IKnowledgeGraphRepository graph,
    IDailyBriefingRepository briefings,
    IPeopleCheckInScheduler? scheduler = null,
    TimeProvider? timeProvider = null) : IPeopleService
{
    private const int MaxFacts = 12;
    private const int MaxSuggestions = 20;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<Person>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        people.ListAsync(ownerId, cancellationToken);

    public Task<Person?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        people.GetAsync(id, ownerId, cancellationToken);

    public async Task<Person?> FindAsync(Guid ownerId, string name, CancellationToken cancellationToken) =>
        Find(await people.ListAsync(ownerId, cancellationToken), name);

    public async Task<PeopleOperation<Person>> CreateAsync(Guid ownerId, PersonInput input,
        CancellationToken cancellationToken)
    {
        if (Validate(input) is { } invalid) return invalid;
        var name = PeopleRules.Clean(input.Name)!;
        var existing = await people.ListAsync(ownerId, cancellationToken);
        if (existing.Count >= PeopleRules.MaxPeople)
            return PeopleOperation<Person>.Invalid("name", $"You can keep at most {PeopleRules.MaxPeople} people.");
        if (SameName(existing, name, null) is not null)
            return PeopleOperation<Person>.Conflict("name", "Someone with that name is already on your list.");

        var now = clock.GetUtcNow();
        var graphEntityId = await ResolveGraphEntityAsync(ownerId, name, input.GraphEntityId, cancellationToken);
        var person = new Person(Guid.CreateVersion7(), ownerId, name, PeopleRules.Clean(input.Relationship),
            input.BirthdayMonth, input.BirthdayDay, input.BirthYear, PeopleRules.CleanNotes(input.Notes),
            input.ContactEveryDays, input.LastContactedAt?.ToUniversalTime(), graphEntityId,
            null, null, now, now);
        await people.AddAsync(person, cancellationToken);
        await EnsureCheckInsAsync(person, cancellationToken);
        return PeopleOperation<Person>.Ok(person);
    }

    public async Task<PeopleOperation<Person>> UpdateAsync(Guid id, Guid ownerId, PersonInput input,
        CancellationToken cancellationToken)
    {
        if (Validate(input) is { } invalid) return invalid;
        var all = await people.ListAsync(ownerId, cancellationToken);
        var person = all.FirstOrDefault(x => x.Id == id);
        if (person is null) return PeopleOperation<Person>.NotFound();
        var name = PeopleRules.Clean(input.Name)!;
        if (SameName(all, name, id) is not null)
            return PeopleOperation<Person>.Conflict("name", "Someone with that name is already on your list.");

        var birthdayChanged = person.BirthdayMonth != input.BirthdayMonth || person.BirthdayDay != input.BirthdayDay;
        var updated = person with
        {
            Name = name,
            Relationship = PeopleRules.Clean(input.Relationship),
            BirthdayMonth = input.BirthdayMonth,
            BirthdayDay = input.BirthdayDay,
            BirthYear = input.BirthYear,
            Notes = PeopleRules.CleanNotes(input.Notes),
            ContactEveryDays = input.ContactEveryDays,
            LastContactedAt = input.LastContactedAt?.ToUniversalTime() ?? person.LastContactedAt,
            GraphEntityId = input.GraphEntityId ?? person.GraphEntityId ??
                await ResolveGraphEntityAsync(ownerId, name, null, cancellationToken),
            // A moved birthday may fall later this year and should notify again.
            BirthdayNotifiedYear = birthdayChanged ? null : person.BirthdayNotifiedYear,
            UpdatedAt = clock.GetUtcNow()
        };
        if (!await people.UpdateAsync(updated, cancellationToken)) return PeopleOperation<Person>.NotFound();
        await EnsureCheckInsAsync(updated, cancellationToken);
        return PeopleOperation<Person>.Ok(updated);
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        people.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<PeopleOperation<Person>> LogContactAsync(Guid id, Guid ownerId, DateTimeOffset? at,
        CancellationToken cancellationToken)
    {
        var person = await people.GetAsync(id, ownerId, cancellationToken);
        if (person is null) return PeopleOperation<Person>.NotFound();
        var now = clock.GetUtcNow();
        var when = (at ?? now).ToUniversalTime();
        if (when > now.AddMinutes(5))
            return PeopleOperation<Person>.Invalid("at", "You can only log contact that already happened.");
        // Keep the most recent contact when an older one is logged afterwards.
        var updated = person with
        {
            LastContactedAt = person.LastContactedAt is { } last && last > when ? last : when,
            UpdatedAt = now
        };
        return await people.UpdateAsync(updated, cancellationToken)
            ? PeopleOperation<Person>.Ok(updated)
            : PeopleOperation<Person>.NotFound();
    }

    public async Task<IReadOnlyList<PersonSuggestion>> SuggestAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var known = await people.ListAsync(ownerId, cancellationToken);
        var linked = known.Where(x => x.GraphEntityId is not null).Select(x => x.GraphEntityId!.Value).ToHashSet();
        var names = known.Select(x => PeopleRules.NameKey(x.Name)).ToHashSet(StringComparer.Ordinal);

        var overview = await graph.GetOverviewAsync(ownerId, 150, cancellationToken);
        var listed = await graph.ListEntitiesAsync(ownerId, null, 200, cancellationToken);
        var candidates = overview.Entities.Concat(listed)
            .Where(PeopleGraph.IsPerson)
            .DistinctBy(x => x.Id)
            .Where(x => !linked.Contains(x.Id) && !names.Contains(PeopleRules.NameKey(x.Name)))
            .ToArray();
        if (candidates.Length == 0) return [];

        var userId = overview.Entities.FirstOrDefault(x => GraphNames.Key(x.Name) == GraphNames.UserKey)?.Id;
        return candidates
            .Select(entity => new PersonSuggestion(entity.Id, entity.Name,
                RelationshipFromEdges(overview.Edges, entity.Id, userId),
                overview.Literals
                    .Where(x => x.EntityId == entity.Id && PeopleGraph.BirthdayPredicates.Contains(x.Predicate))
                    .OrderByDescending(x => x.Confidence)
                    .Select(x => Birthday.Parse(x.Value))
                    .FirstOrDefault(x => x is not null),
                entity.Summary))
            // People Jarvis knows more about come first.
            .OrderByDescending(x => (x.Relationship is null ? 0 : 2) + (x.Birthday is null ? 0 : 1))
            .ThenByDescending(x => candidates.First(c => c.Id == x.GraphEntityId).RelationCount)
            .Take(MaxSuggestions)
            .ToArray();
    }

    public async Task<IReadOnlyList<PersonFact>> GetFactsAsync(Person person, CancellationToken cancellationToken)
    {
        if (person.GraphEntityId is not { } entityId) return [];
        var details = await graph.GetEntityAsync(person.OwnerId, entityId, cancellationToken);
        if (details is null) return [];
        return details.Current
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.ValidFrom)
            .Select(x => x.SubjectId == entityId
                ? new PersonFact(x.Predicate, x.ObjectName ?? x.ObjectValue ?? "")
                : new PersonFact(x.Predicate, x.SubjectName))
            .Where(x => x.Value.Length > 0)
            .Take(MaxFacts)
            .ToArray();
    }

    public async Task<TimeZoneInfo> GetTimeZoneAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        return LocalClock.TryFind(zoneId, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    /// <summary>
    /// The person called <paramref name="name"/>: the same name (ignoring case and accents), else the only person
    /// with that relationship ("mama", "my sister"), else the only person whose first name or whole words match.
    /// </summary>
    public static Person? Find(IReadOnlyList<Person> all, string name)
    {
        var key = PeopleRules.NameKey(name);
        if (key.Length == 0) return null;
        var exact = all.FirstOrDefault(x => PeopleRules.NameKey(x.Name) == key);
        if (exact is not null) return exact;

        var words = PeopleRules.Words(name).Where(x => x is not ("my" or "mijn" or "de" or "het" or "the")).ToArray();
        var relationKey = string.Concat(words);
        var byRelationship = all.Where(x => x.Relationship is not null &&
                                            PeopleRules.NameKey(x.Relationship) == relationKey).ToArray();
        if (byRelationship.Length == 1) return byRelationship[0];

        if (words.Length == 0) return null;
        var byWords = all.Where(x =>
        {
            var nameWords = PeopleRules.Words(x.Name);
            return words.All(word => nameWords.Contains(word));
        }).ToArray();
        return byWords.Length == 1 ? byWords[0] : null;
    }

    private static PeopleOperation<Person>? Validate(PersonInput input)
    {
        var name = PeopleRules.Clean(input.Name);
        if (name is null) return PeopleOperation<Person>.Invalid("name", "Give the person a name.");
        if (name.Length > PeopleRules.MaxNameLength)
            return PeopleOperation<Person>.Invalid("name", $"Names can contain at most {PeopleRules.MaxNameLength} characters.");
        if (PeopleRules.NameKey(name).Length == 0)
            return PeopleOperation<Person>.Invalid("name", "Give the person a name with letters or digits.");
        if (PeopleRules.Clean(input.Relationship) is { Length: > PeopleRules.MaxRelationshipLength })
            return PeopleOperation<Person>.Invalid("relationship",
                $"Relationships can contain at most {PeopleRules.MaxRelationshipLength} characters.");
        if (PeopleRules.CleanNotes(input.Notes) is { Length: > PeopleRules.MaxNotesLength })
            return PeopleOperation<Person>.Invalid("notes", $"Notes can contain at most {PeopleRules.MaxNotesLength} characters.");
        if ((input.BirthdayMonth is null) != (input.BirthdayDay is null))
            return PeopleOperation<Person>.Invalid("birthday", "Give both the day and the month of the birthday.");
        if (input.BirthYear is not null && input.BirthdayMonth is null)
            return PeopleOperation<Person>.Invalid("birthday", "Give the day and month with the birth year.");
        if (input.BirthdayMonth is { } month && input.BirthdayDay is { } day &&
            !Birthday.IsValid(month, day, input.BirthYear))
            return PeopleOperation<Person>.Invalid("birthday", "That is not a valid birthday.");
        if (input.ContactEveryDays is { } every &&
            every is < PeopleRules.MinContactEveryDays or > PeopleRules.MaxContactEveryDays)
            return PeopleOperation<Person>.Invalid("contactEveryDays",
                $"Choose a check-in interval between {PeopleRules.MinContactEveryDays} and {PeopleRules.MaxContactEveryDays} days.");
        return null;
    }

    private static Person? SameName(IEnumerable<Person> all, string name, Guid? except)
    {
        var key = PeopleRules.NameKey(name);
        return all.FirstOrDefault(x => x.Id != except && PeopleRules.NameKey(x.Name) == key);
    }

    private async Task<Guid?> ResolveGraphEntityAsync(Guid ownerId, string name, Guid? requested,
        CancellationToken cancellationToken)
    {
        if (requested is { } id)
        {
            var entity = await graph.GetEntityAsync(ownerId, id, cancellationToken);
            return entity is not null && PeopleGraph.IsPerson(entity.Entity) ? id : null;
        }
        var found = await graph.FindEntityAsync(ownerId, name, null, cancellationToken);
        return found is not null && PeopleGraph.IsPerson(found.Entity) ? found.Entity.Id : null;
    }

    private static string? RelationshipFromEdges(IReadOnlyList<GraphEdge> edges, Guid personId, Guid? userId)
    {
        if (userId is not { } user) return null;
        foreach (var edge in edges.OrderByDescending(x => x.Confidence))
        {
            if (edge.From == personId && edge.To == user &&
                PeopleGraph.Relationship(edge.Predicate, personIsSubject: true) is { } fromPerson)
                return fromPerson;
            if (edge.From == user && edge.To == personId &&
                PeopleGraph.Relationship(edge.Predicate, personIsSubject: false) is { } fromUser)
                return fromUser;
        }
        return null;
    }

    // The worker's reconciler starts check-ins too, so a failed start here only delays the first one.
    private async Task EnsureCheckInsAsync(Person person, CancellationToken cancellationToken)
    {
        if (scheduler is null || !person.NeedsCheckIns) return;
        try
        {
            await scheduler.SchedulePeopleCheckInAsync(person.OwnerId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }
}
