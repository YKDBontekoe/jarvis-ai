using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.People;
using Jarvis.Domain.People;

namespace Jarvis.Api.Endpoints;

public sealed record PersonRequest(string? Name, string? Relationship, int? BirthdayMonth, int? BirthdayDay,
    int? BirthYear, string? Notes, int? ContactEveryDays, DateTimeOffset? LastContactedAt, Guid? GraphEntityId);

public sealed record PersonContactRequest(DateTimeOffset? At);

public sealed record PersonDto(Guid Id, string Name, string? Relationship, int? BirthdayMonth, int? BirthdayDay,
    int? BirthYear, string? Notes, int? ContactEveryDays, DateTimeOffset? LastContactedAt, Guid? GraphEntityId,
    DateOnly? NextBirthday, int? DaysUntilBirthday, int? TurningAge, int? DaysSinceContact, DateOnly? ContactDueOn,
    bool ContactDue, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record PersonFactDto(string Predicate, string Value);

public sealed record PersonDetailDto(PersonDto Person, IReadOnlyList<PersonFactDto> Facts);

public sealed record PersonSuggestionDto(Guid GraphEntityId, string Name, string? Relationship, int? BirthdayMonth,
    int? BirthdayDay, int? BirthYear, string? Summary);

/// <summary>
/// The people in the owner's life: birthdays, notes, and keep-in-touch cadences. Dates such as "days until the
/// birthday" use the owner's daily-briefing time zone. Audit entries carry ids only, never names or notes.
/// </summary>
internal static class PeopleEndpoints
{
    public static RouteGroupBuilder MapPeopleEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var people = api.MapGroup("/people");

        people.MapGet("", async (IPeopleService service, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var zone = await service.GetTimeZoneAsync(currentUser.OwnerId, ct);
            var today = PeopleCalendar.LocalDate(TimeProvider.System.GetUtcNow(), zone);
            return Results.Ok((await service.ListAsync(currentUser.OwnerId, ct))
                .Select(person => ToDto(person, today, zone)));
        }).WithName("ListPeople");

        people.MapGet("/suggestions", async (IPeopleService service, ICurrentUser currentUser,
                CancellationToken ct) =>
            Results.Ok((await service.SuggestAsync(currentUser.OwnerId, ct)).Select(suggestion =>
                new PersonSuggestionDto(suggestion.GraphEntityId, suggestion.Name, suggestion.Relationship,
                    suggestion.Birthday?.Month, suggestion.Birthday?.Day, suggestion.Birthday?.Year,
                    suggestion.Summary))))
            .WithName("ListPeopleSuggestions");

        people.MapGet("/{id:guid}", async (Guid id, IPeopleService service, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var person = await service.GetAsync(id, currentUser.OwnerId, ct);
            if (person is null) return Results.NotFound();
            var facts = await service.GetFactsAsync(person, ct);
            return Results.Ok(new PersonDetailDto(await ToDtoAsync(service, person, ct),
                facts.Select(fact => new PersonFactDto(fact.Predicate, fact.Value)).ToArray()));
        }).WithName("GetPerson");

        people.MapPost("", async (PersonRequest request, IPeopleService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(currentUser.OwnerId, ToInput(request), ct);
            if (Failed(result) is { } failure) return failure;
            var person = result.Value!;
            await AuditAsync(audit, logger, currentUser, "person.created", person.Id, ct);
            return Results.Created($"/api/v1/people/{person.Id}", await ToDtoAsync(service, person, ct));
        }).WithName("CreatePerson");

        people.MapPut("/{id:guid}", async (Guid id, PersonRequest request, IPeopleService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, currentUser.OwnerId, ToInput(request), ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, "person.updated", id, ct);
            return Results.Ok(await ToDtoAsync(service, result.Value!, ct));
        }).WithName("UpdatePerson");

        people.MapDelete("/{id:guid}", async (Guid id, IPeopleService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await service.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "person.deleted", id, ct, risk: "moderate");
            return Results.NoContent();
        }).WithName("DeletePerson");

        people.MapPost("/{id:guid}/contact", async (Guid id, PersonContactRequest? request, IPeopleService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.LogContactAsync(id, currentUser.OwnerId, request?.At, ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, "person.contacted", id, ct);
            return Results.Ok(await ToDtoAsync(service, result.Value!, ct));
        }).WithName("LogPersonContact");

        return api;
    }

    internal static PersonDto ToDto(Person person, DateOnly today, TimeZoneInfo zone)
    {
        var next = PeopleCalendar.NextBirthday(person, today);
        return new PersonDto(person.Id, person.Name, person.Relationship, person.BirthdayMonth, person.BirthdayDay,
            person.BirthYear, person.Notes, person.ContactEveryDays, person.LastContactedAt, person.GraphEntityId,
            next, next is { } date ? date.DayNumber - today.DayNumber : null,
            next is { } birthday ? PeopleCalendar.AgeOn(person, birthday) : null,
            PeopleCalendar.DaysSinceContact(person, today, zone), PeopleCalendar.ContactDueOn(person, zone),
            PeopleCalendar.IsContactDue(person, today, zone), person.CreatedAt, person.UpdatedAt);
    }

    private static async Task<PersonDto> ToDtoAsync(IPeopleService service, Person person, CancellationToken ct)
    {
        var zone = await service.GetTimeZoneAsync(person.OwnerId, ct);
        return ToDto(person, PeopleCalendar.LocalDate(TimeProvider.System.GetUtcNow(), zone), zone);
    }

    private static PersonInput ToInput(PersonRequest request) => new(request.Name, request.Relationship,
        request.BirthdayMonth, request.BirthdayDay, request.BirthYear, request.Notes, request.ContactEveryDays,
        request.LastContactedAt, request.GraphEntityId);

    private static IResult? Failed<T>(PeopleOperation<T> result) => result.Failure switch
    {
        PeopleFailure.None => null,
        PeopleFailure.NotFound => Results.NotFound(),
        PeopleFailure.Conflict => ApiProblemResults.Conflict(result.Message ?? "That person is already on your list."),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid personId, CancellationToken ct, string risk = "low") =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "people", action, risk, true, null,
            JsonSerializer.Serialize(new { resourceId = personId, source = "app" }), ct);
}
