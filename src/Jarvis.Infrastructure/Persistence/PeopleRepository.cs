using Jarvis.Application.People;
using Jarvis.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class PersonEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="PeopleRules.NameKey"/> of <see cref="Name"/>; unique per owner.</summary>
    public string NameKey { get; set; } = string.Empty;

    public string? Relationship { get; set; }
    public int? BirthdayMonth { get; set; }
    public int? BirthdayDay { get; set; }
    public int? BirthYear { get; set; }
    public string? Notes { get; set; }
    public int? ContactEveryDays { get; set; }
    public DateTimeOffset? LastContactedAt { get; set; }
    public Guid? GraphEntityId { get; set; }
    public int? BirthdayNotifiedYear { get; set; }
    public DateTimeOffset? CheckInNudgedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Person ToRecord() => new(Id, OwnerId, Name, Relationship, BirthdayMonth, BirthdayDay, BirthYear, Notes,
        ContactEveryDays, LastContactedAt, GraphEntityId, BirthdayNotifiedYear, CheckInNudgedAt, CreatedAt, UpdatedAt);

    public void Apply(Person person)
    {
        Name = person.Name;
        NameKey = PeopleRules.NameKey(person.Name);
        Relationship = person.Relationship;
        BirthdayMonth = person.BirthdayMonth;
        BirthdayDay = person.BirthdayDay;
        BirthYear = person.BirthYear;
        Notes = person.Notes;
        ContactEveryDays = person.ContactEveryDays;
        LastContactedAt = person.LastContactedAt;
        GraphEntityId = person.GraphEntityId;
        BirthdayNotifiedYear = person.BirthdayNotifiedYear;
        CheckInNudgedAt = person.CheckInNudgedAt;
        UpdatedAt = person.UpdatedAt;
    }
}

public sealed class PeopleRepository(JarvisDbContext db) : IPeopleRepository
{
    public async Task<IReadOnlyList<Person>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.People.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.NameKey)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<Person?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.People.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddAsync(Person person, CancellationToken cancellationToken)
    {
        var entity = new PersonEntity { Id = person.Id, OwnerId = person.OwnerId, CreatedAt = person.CreatedAt };
        entity.Apply(person);
        db.People.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Person person, CancellationToken cancellationToken)
    {
        var entity = await db.People
            .SingleOrDefaultAsync(x => x.Id == person.Id && x.OwnerId == person.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(person);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.People.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<Guid>> ListOwnersWithCheckInsAsync(CancellationToken cancellationToken) =>
        await db.People.AsNoTracking()
            .Where(x => x.BirthdayMonth != null || x.ContactEveryDays != null)
            .Select(x => x.OwnerId)
            // The relationship radar also rides on the daily check-in, so owners with linked chats need it too.
            .Union(db.PersonChannelLinks.AsNoTracking().Select(x => x.OwnerId))
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task MarkNotifiedAsync(Guid ownerId, IReadOnlyCollection<Guid> birthdayIds, int birthdayYear,
        IReadOnlyCollection<Guid> nudgedIds, DateTimeOffset nudgedAt, CancellationToken cancellationToken)
    {
        var birthdays = birthdayIds.ToArray();
        var nudged = nudgedIds.ToArray();
        if (birthdays.Length > 0)
            await db.People.Where(x => x.OwnerId == ownerId && birthdays.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.BirthdayNotifiedYear, birthdayYear),
                    cancellationToken);
        if (nudged.Length > 0)
            await db.People.Where(x => x.OwnerId == ownerId && nudged.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CheckInNudgedAt, nudgedAt),
                    cancellationToken);
    }
}
