using Jarvis.Application.Missions;
using Jarvis.Domain.Missions;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class MissionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Status { get; set; } = MissionStatuses.Ready;
    public Guid? ProjectId { get; set; }
    public string? Summary { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Mission ToRecord() => new(Id, OwnerId, Title, Goal, Status, ProjectId, Summary, FailureReason, CreatedAt,
        UpdatedAt, StartedAt, CompletedAt);

    public void Apply(Mission mission)
    {
        Title = mission.Title;
        Goal = mission.Goal;
        Status = mission.Status;
        ProjectId = mission.ProjectId;
        Summary = mission.Summary;
        FailureReason = mission.FailureReason;
        UpdatedAt = mission.UpdatedAt;
        StartedAt = mission.StartedAt;
        CompletedAt = mission.CompletedAt;
    }
}

public sealed class MissionStepEntity
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public Guid OwnerId { get; set; }
    public string Key { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Role { get; set; } = MissionRoles.Generalist;
    public string Title { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public string[] DependsOn { get; set; } = [];
    public string Status { get; set; } = StepStatuses.Pending;
    public Guid? TaskId { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public MissionStep ToRecord() => new(Id, MissionId, OwnerId, Key, Ordinal, Role, Title, Instruction, DependsOn,
        Status, TaskId, Result, Error, StartedAt, CompletedAt);

    public void Apply(MissionStep step)
    {
        Key = step.Key;
        Ordinal = step.Ordinal;
        Role = step.Role;
        Title = step.Title;
        Instruction = step.Instruction;
        DependsOn = step.DependsOn.ToArray();
        Status = step.Status;
        TaskId = step.TaskId;
        Result = step.Result;
        Error = step.Error;
        StartedAt = step.StartedAt;
        CompletedAt = step.CompletedAt;
    }
}

public sealed class MissionNoteEntity
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public Guid OwnerId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? StepKey { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public MissionNote ToRecord() => new(Id, MissionId, OwnerId, Key, Value, StepKey, UpdatedAt);
}

public sealed class MissionRepository(JarvisDbContext db) : IMissionRepository
{
    public async Task<IReadOnlyList<Mission>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Missions.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.CreatedAt)
            .Take(100).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<Mission?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Missions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<Mission?> GetForSupervisorAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Missions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<Guid>> ListRunningIdsAsync(CancellationToken cancellationToken) =>
        await db.Missions.AsNoTracking().Where(x => x.Status == MissionStatuses.Running).Select(x => x.Id)
            .Take(200).ToListAsync(cancellationToken);

    public Task<int> CountActiveAsync(Guid ownerId, CancellationToken cancellationToken) =>
        db.Missions.CountAsync(x => x.OwnerId == ownerId && (x.Status == MissionStatuses.Ready ||
                                                             x.Status == MissionStatuses.Running ||
                                                             x.Status == MissionStatuses.Paused), cancellationToken);

    public async Task AddAsync(Mission mission, IReadOnlyList<MissionStep> steps, CancellationToken cancellationToken)
    {
        var entity = new MissionEntity { Id = mission.Id, OwnerId = mission.OwnerId, CreatedAt = mission.CreatedAt };
        entity.Apply(mission);
        db.Missions.Add(entity);
        foreach (var step in steps)
        {
            var stepEntity = new MissionStepEntity { Id = step.Id, MissionId = step.MissionId, OwnerId = step.OwnerId };
            stepEntity.Apply(step);
            db.MissionSteps.Add(stepEntity);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Mission mission, CancellationToken cancellationToken)
    {
        var entity = await db.Missions.SingleOrDefaultAsync(x => x.Id == mission.Id, cancellationToken);
        if (entity is null) return false;
        entity.Apply(mission);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await db.MissionNotes.Where(x => x.MissionId == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
        await db.MissionSteps.Where(x => x.MissionId == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
        return await db.Missions.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<MissionStep>> ListStepsAsync(Guid missionId, CancellationToken cancellationToken) =>
        (await db.MissionSteps.AsNoTracking().Where(x => x.MissionId == missionId).OrderBy(x => x.Ordinal)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<MissionStep?> GetStepAsync(Guid stepId, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.MissionSteps.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == stepId && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<MissionStep?> FindStepByTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        (await db.MissionSteps.AsNoTracking().FirstOrDefaultAsync(x => x.TaskId == taskId, cancellationToken))
        ?.ToRecord();

    public async Task<bool> UpdateStepAsync(MissionStep step, CancellationToken cancellationToken)
    {
        var entity = await db.MissionSteps.SingleOrDefaultAsync(x => x.Id == step.Id, cancellationToken);
        if (entity is null) return false;
        entity.Apply(step);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryClaimStepAsync(Guid stepId, DateTimeOffset at, CancellationToken cancellationToken) =>
        await db.MissionSteps.Where(x => x.Id == stepId && x.Status == StepStatuses.Pending)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, StepStatuses.Running)
                .SetProperty(x => x.StartedAt, at), cancellationToken) > 0;

    public async Task<IReadOnlyList<MissionNote>> ListNotesAsync(Guid missionId, CancellationToken cancellationToken) =>
        (await db.MissionNotes.AsNoTracking().Where(x => x.MissionId == missionId).OrderBy(x => x.UpdatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task UpsertNoteAsync(MissionNote note, CancellationToken cancellationToken)
    {
        var entity = await db.MissionNotes.SingleOrDefaultAsync(
            x => x.MissionId == note.MissionId && x.Key == note.Key, cancellationToken);
        if (entity is null)
        {
            db.MissionNotes.Add(new MissionNoteEntity
            {
                Id = note.Id, MissionId = note.MissionId, OwnerId = note.OwnerId, Key = note.Key, Value = note.Value,
                StepKey = note.StepKey, UpdatedAt = note.UpdatedAt
            });
        }
        else
        {
            entity.Value = note.Value;
            entity.StepKey = note.StepKey;
            entity.UpdatedAt = note.UpdatedAt;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
