using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Skills;

namespace Jarvis.Api.Endpoints;

public sealed record SkillDto(Guid Id, string Name, string Description, string Instructions, string Source,
    string Status, bool IsLocked, int Version, int UseCount, DateTimeOffset? LastUsedAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
public sealed record SkillDetailsDto(SkillDto Skill, IReadOnlyList<SkillRevisionRecord> Revisions);
public sealed record SaveSkillRequest(string? Name, string? Description, string? Instructions);
public sealed record SkillStatusRequest(string? Status);
public sealed record SkillLockRequest(bool Locked);
public sealed record ImportSkillRequest(string? Markdown);

internal static class SkillEndpoints
{
    public static RouteGroupBuilder MapSkillEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var skills = api.MapGroup("/skills");

        skills.MapGet("", async (ISkillRepository repository, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await repository.ListAsync(currentUser.OwnerId, ct)).Select(ToDto)))
            .WithName("ListSkills");

        skills.MapGet("/{id:guid}", async (Guid id, ISkillRepository repository, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var skill = await repository.GetAsync(id, currentUser.OwnerId, ct);
            return skill is null
                ? Results.NotFound()
                : Results.Ok(new SkillDetailsDto(ToDto(skill),
                    await repository.ListRevisionsAsync(id, currentUser.OwnerId, ct)));
        }).WithName("GetSkill");

        skills.MapPost("", async (SaveSkillRequest request, ISkillRepository repository, IAuditEventStore audit,
                ICurrentUser currentUser, CancellationToken ct) =>
            await SaveAsync(new SkillDraft(request.Name ?? "", request.Description ?? "", request.Instructions ?? ""),
                SkillSources.User, "Created in the app", repository, audit, logger, currentUser, ct, created: true))
            .WithName("CreateSkill");

        skills.MapPut("/{id:guid}", async (Guid id, SaveSkillRequest request, ISkillRepository repository,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var existing = await repository.GetAsync(id, currentUser.OwnerId, ct);
            if (existing is null) return Results.NotFound();
            return await SaveAsync(new SkillDraft(existing.Name, request.Description ?? existing.Description,
                    request.Instructions ?? existing.Instructions), SkillSources.User, "Edited in the app",
                repository, audit, logger, currentUser, ct, created: false);
        }).WithName("UpdateSkill");

        skills.MapPost("/import", async (ImportSkillRequest request, ISkillRepository repository,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            SkillDraft draft;
            try { draft = SkillMarkdown.Parse(request.Markdown ?? string.Empty); }
            catch (ArgumentException exception) { return EndpointHelpers.Invalid("markdown", exception.Message); }
            return await SaveAsync(draft, SkillSources.Imported, "Imported from SKILL.md", repository, audit, logger,
                currentUser, ct, created: true);
        }).WithName("ImportSkill");

        skills.MapGet("/{id:guid}/export", async (Guid id, ISkillRepository repository, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var skill = await repository.GetAsync(id, currentUser.OwnerId, ct);
            return skill is null
                ? Results.NotFound()
                : Results.Text(SkillMarkdown.Serialize(skill), "text/markdown; charset=utf-8");
        }).WithName("ExportSkill");

        skills.MapPost("/{id:guid}/status", async (Guid id, SkillStatusRequest request, ISkillRepository repository,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.Status is not (SkillStatuses.Active or SkillStatuses.Disabled))
                return EndpointHelpers.Invalid("status", "Set a skill to active or disabled.");
            var skill = await repository.SetStatusAsync(id, currentUser.OwnerId, request.Status, ct);
            if (skill is null) return Results.NotFound();
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "skills",
                "skill." + request.Status, "moderate", true, null,
                JsonSerializer.Serialize(new { resourceId = id, name = skill.Name }), ct);
            return Results.Ok(ToDto(skill));
        }).WithName("SetSkillStatus");

        skills.MapPost("/{id:guid}/lock", async (Guid id, SkillLockRequest request, ISkillRepository repository,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var skill = await repository.SetLockedAsync(id, currentUser.OwnerId, request.Locked, ct);
            return skill is null ? Results.NotFound() : Results.Ok(ToDto(skill));
        }).WithName("SetSkillLock");

        skills.MapDelete("/{id:guid}", async (Guid id, ISkillRepository repository, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await repository.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "skills", "skill.deleted",
                "moderate", true, null, JsonSerializer.Serialize(new { resourceId = id }), ct);
            return Results.NoContent();
        }).WithName("DeleteSkill");

        return api;
    }

    private static async Task<IResult> SaveAsync(SkillDraft input, string source, string note,
        ISkillRepository repository, IAuditEventStore audit, ILogger logger, ICurrentUser currentUser,
        CancellationToken ct, bool created)
    {
        SkillDraft draft;
        try { draft = SkillMarkdown.Validate(input); }
        catch (ArgumentException exception) { return EndpointHelpers.Invalid("skill", exception.Message); }
        if (created && await repository.FindByNameAsync(currentUser.OwnerId, draft.Name, ct) is not null)
            return Results.Conflict(new { message = $"A skill named {draft.Name} already exists." });
        SkillRecord? saved;
        try
        {
            saved = await repository.UpsertAsync(currentUser.OwnerId, draft, source, SkillStatuses.Active,
                respectLock: false, note, ct);
        }
        catch (ArgumentException exception)
        {
            return EndpointHelpers.Invalid("skill", exception.Message);
        }
        await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "skills",
            created ? "skill.created" : "skill.updated", "moderate", true, null,
            JsonSerializer.Serialize(new { resourceId = saved!.Id, name = saved.Name, source }), ct);
        return created ? Results.Created($"/api/v1/skills/{saved.Id}", ToDto(saved)) : Results.Ok(ToDto(saved));
    }

    private static SkillDto ToDto(SkillRecord skill) => new(skill.Id, skill.Name, skill.Description,
        skill.Instructions, skill.Source, skill.Status, skill.IsLocked, skill.Version, skill.UseCount,
        skill.LastUsedAt, skill.CreatedAt, skill.UpdatedAt);
}
