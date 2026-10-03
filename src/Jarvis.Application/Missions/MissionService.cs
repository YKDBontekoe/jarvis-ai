using Jarvis.Application.Workflows;
using Jarvis.Domain.Missions;

namespace Jarvis.Application.Missions;

public sealed class MissionService(IMissionRepository repository, IMissionPlanner planner,
    IJarvisTaskRepository taskRepository, IJarvisTaskService tasks, INotificationRepository notifications,
    TimeProvider? timeProvider = null) : IMissionService
{
    private const string BlockedError = "An earlier step did not finish.";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<Mission>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        repository.ListAsync(ownerId, cancellationToken);

    public async Task<MissionDetail?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var mission = await repository.GetAsync(id, ownerId, cancellationToken);
        return mission is null ? null : await DetailAsync(mission, cancellationToken);
    }

    public async Task<MissionOperation<MissionDetail>> CreateAsync(Guid ownerId, string? goal, string? title,
        Guid? projectId, CancellationToken cancellationToken)
    {
        var cleanGoal = MissionRules.Clean(goal);
        if (cleanGoal is null || cleanGoal.Length < 10)
            return MissionOperation<MissionDetail>.Invalid("goal", "Describe the mission in a full sentence or two.");
        if (cleanGoal.Length > MissionRules.MaxGoalLength)
            return MissionOperation<MissionDetail>.Invalid("goal",
                $"Keep the goal under {MissionRules.MaxGoalLength} characters.");
        if (await repository.CountActiveAsync(ownerId, cancellationToken) >= MissionRules.MaxActiveMissions)
            return MissionOperation<MissionDetail>.Conflict(
                $"You can have at most {MissionRules.MaxActiveMissions} missions going at once. Finish or cancel one first.");

        var plan = MissionPlanning.Normalize(await planner.PlanAsync(ownerId, cleanGoal, cancellationToken), cleanGoal);
        var now = clock.GetUtcNow();
        var mission = new Mission(Guid.CreateVersion7(), ownerId,
            MissionRules.Limit(title, MissionRules.MaxTitleLength) ?? plan.Title, cleanGoal, MissionStatuses.Ready,
            projectId, null, null, now, now, null, null);
        var steps = plan.Steps.Select((step, index) => new MissionStep(Guid.CreateVersion7(), mission.Id, ownerId,
            step.Key, index, step.Role, step.Title, step.Instruction, step.DependsOn, StepStatuses.Pending, null, null,
            null, null, null)).ToArray();
        await repository.AddAsync(mission, steps, cancellationToken);
        return MissionOperation<MissionDetail>.Ok(await DetailAsync(mission, cancellationToken));
    }

    public async Task<MissionOperation<MissionDetail>> StartAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var mission = await repository.GetAsync(id, ownerId, cancellationToken);
        if (mission is null) return MissionOperation<MissionDetail>.NotFound();
        if (mission.Status != MissionStatuses.Ready)
            return MissionOperation<MissionDetail>.Conflict("Only a planned mission can be started.");
        var now = clock.GetUtcNow();
        mission = mission with { Status = MissionStatuses.Running, StartedAt = now, UpdatedAt = now };
        await repository.UpdateAsync(mission, cancellationToken);
        await AdvanceAsync(mission.Id, cancellationToken);
        return MissionOperation<MissionDetail>.Ok(
            await DetailAsync((await repository.GetAsync(id, ownerId, cancellationToken))!, cancellationToken));
    }

    public async Task<MissionOperation<MissionDetail>> PauseAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken) =>
        await ChangeAsync(id, ownerId, MissionStatuses.Running, MissionStatuses.Paused,
            "Only a running mission can be paused.", cancellationToken);

    public async Task<MissionOperation<MissionDetail>> ResumeAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var result = await ChangeAsync(id, ownerId, MissionStatuses.Paused, MissionStatuses.Running,
            "Only a paused mission can be resumed.", cancellationToken);
        if (result.Succeeded) await AdvanceAsync(id, cancellationToken);
        return result.Succeeded
            ? MissionOperation<MissionDetail>.Ok((await GetAsync(id, ownerId, cancellationToken))!)
            : result;
    }

    public async Task<MissionOperation<MissionDetail>> CancelAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var mission = await repository.GetAsync(id, ownerId, cancellationToken);
        if (mission is null) return MissionOperation<MissionDetail>.NotFound();
        if (MissionStatuses.IsFinished(mission.Status))
            return MissionOperation<MissionDetail>.Conflict("This mission is already finished.");

        var now = clock.GetUtcNow();
        foreach (var step in await repository.ListStepsAsync(id, cancellationToken))
        {
            if (StepStatuses.IsFinal(step.Status)) continue;
            if (step is { Status: StepStatuses.Running, TaskId: { } taskId })
                await tasks.CancelAsync(taskId, ownerId, cancellationToken);
            await repository.UpdateStepAsync(step with
            {
                Status = StepStatuses.Cancelled, Error = "The mission was cancelled.", CompletedAt = now
            }, cancellationToken);
        }
        await repository.UpdateAsync(mission with
        {
            Status = MissionStatuses.Cancelled, CompletedAt = now, UpdatedAt = now
        }, cancellationToken);
        return MissionOperation<MissionDetail>.Ok((await GetAsync(id, ownerId, cancellationToken))!);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var mission = await repository.GetAsync(id, ownerId, cancellationToken);
        if (mission is null || !MissionStatuses.IsFinished(mission.Status) && mission.Status != MissionStatuses.Ready)
            return false;
        return await repository.DeleteAsync(id, ownerId, cancellationToken);
    }

    public async Task<MissionOperation<MissionDetail>> UpdateStepAsync(Guid stepId, Guid ownerId, string? instruction,
        CancellationToken cancellationToken)
    {
        var step = await repository.GetStepAsync(stepId, ownerId, cancellationToken);
        if (step is null) return MissionOperation<MissionDetail>.NotFound();
        if (step.Status != StepStatuses.Pending)
            return MissionOperation<MissionDetail>.Conflict("Only a step that has not started can be changed.");
        var clean = MissionRules.Limit(instruction, MissionRules.MaxInstructionLength) ?? "";
        if (clean.Length == 0) return MissionOperation<MissionDetail>.Invalid("instruction", "Write what this step should do.");
        await repository.UpdateStepAsync(step with { Instruction = clean }, cancellationToken);
        return MissionOperation<MissionDetail>.Ok((await GetAsync(step.MissionId, ownerId, cancellationToken))!);
    }

    public async Task<MissionOperation<MissionDetail>> SkipStepAsync(Guid stepId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var step = await repository.GetStepAsync(stepId, ownerId, cancellationToken);
        if (step is null) return MissionOperation<MissionDetail>.NotFound();
        if (step.Status is not (StepStatuses.Pending or StepStatuses.Failed or StepStatuses.Cancelled))
            return MissionOperation<MissionDetail>.Conflict("Only a step that is waiting or failed can be skipped.");
        await repository.UpdateStepAsync(step with
        {
            Status = StepStatuses.Skipped, Error = null, CompletedAt = clock.GetUtcNow()
        }, cancellationToken);
        await ReviveBlockedAsync(step, cancellationToken);
        await ReopenIfNeededAsync(step.MissionId, ownerId, cancellationToken);
        return MissionOperation<MissionDetail>.Ok((await GetAsync(step.MissionId, ownerId, cancellationToken))!);
    }

    public async Task<MissionOperation<MissionDetail>> RetryStepAsync(Guid stepId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var step = await repository.GetStepAsync(stepId, ownerId, cancellationToken);
        if (step is null) return MissionOperation<MissionDetail>.NotFound();
        if (step.Status is not (StepStatuses.Failed or StepStatuses.Cancelled))
            return MissionOperation<MissionDetail>.Conflict("Only a failed step can be retried.");
        await repository.UpdateStepAsync(step with
        {
            Status = StepStatuses.Pending, TaskId = null, Result = null, Error = null, StartedAt = null,
            CompletedAt = null
        }, cancellationToken);

        await ReviveBlockedAsync(step, cancellationToken);
        await ReopenIfNeededAsync(step.MissionId, ownerId, cancellationToken);
        return MissionOperation<MissionDetail>.Ok((await GetAsync(step.MissionId, ownerId, cancellationToken))!);
    }

    public async Task<int> AdvanceAllAsync(CancellationToken cancellationToken)
    {
        var advanced = 0;
        foreach (var id in await repository.ListRunningIdsAsync(cancellationToken))
        {
            await AdvanceAsync(id, cancellationToken);
            advanced++;
        }
        return advanced;
    }

    public async Task AdvanceAsync(Guid missionId, CancellationToken cancellationToken)
    {
        var mission = await repository.GetForSupervisorAsync(missionId, cancellationToken);
        if (mission is not { Status: MissionStatuses.Running }) return;
        var now = clock.GetUtcNow();
        var steps = (await repository.ListStepsAsync(missionId, cancellationToken)).OrderBy(x => x.Ordinal).ToList();

        // 1. Collect what the running tasks produced.
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i] is not { Status: StepStatuses.Running, TaskId: { } taskId }) continue;
            var task = await taskRepository.GetTaskByIdAsync(taskId, cancellationToken);
            MissionStep? updated = task?.Status switch
            {
                "completed" => steps[i] with
                {
                    Status = StepStatuses.Completed, Result = MissionRules.ClipText(task.Summary, MissionRules.MaxResultLength),
                    CompletedAt = now
                },
                "failed" or "cancelled" => steps[i] with
                {
                    Status = StepStatuses.Failed,
                    Error = MissionRules.ClipText(string.IsNullOrWhiteSpace(task.Summary)
                        ? $"The task {task.Status}."
                        : task.Summary, 500),
                    CompletedAt = now
                },
                null => steps[i] with { Status = StepStatuses.Failed, Error = "The task no longer exists.", CompletedAt = now },
                _ => null
            };
            if (updated is null) continue;
            await repository.UpdateStepAsync(updated, cancellationToken);
            steps[i] = updated;
        }

        // 2. Stop what can no longer happen because an earlier step failed.
        async Task StopBlockedAsync()
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Status != StepStatuses.Pending) continue;
                    var blocked = steps[i].DependsOn.Any(dep => steps.Any(s => s.Key == dep &&
                        s.Status is StepStatuses.Failed or StepStatuses.Cancelled));
                    if (!blocked) continue;
                    var stopped = steps[i] with
                    {
                        Status = StepStatuses.Cancelled, Error = BlockedError, CompletedAt = now
                    };
                    await repository.UpdateStepAsync(stopped, cancellationToken);
                    steps[i] = stopped;
                    changed = true;
                }
            }
        }

        await StopBlockedAsync();

        // 3. Start every step whose dependencies are done, a few at a time.
        var running = steps.Count(x => x.Status == StepStatuses.Running);
        var notes = await repository.ListNotesAsync(missionId, cancellationToken);
        for (var i = 0; i < steps.Count && running < MissionRules.MaxParallel; i++)
        {
            var step = steps[i];
            if (step.Status != StepStatuses.Pending ||
                !step.DependsOn.All(dep => steps.Any(s => s.Key == dep && StepStatuses.IsDone(s.Status))))
                continue;
            if (!await repository.TryClaimStepAsync(step.Id, now, cancellationToken)) continue;
            try
            {
                var task = await tasks.CreateAsync(mission.OwnerId,
                    MissionRules.ClipText($"{mission.Title}: {step.Title}", 200),
                    MissionPrompts.ForStep(mission, step, steps, notes), cancellationToken,
                    projectId: mission.ProjectId);
                steps[i] = step with { Status = StepStatuses.Running, TaskId = task.Id, StartedAt = now };
                await repository.UpdateStepAsync(steps[i], cancellationToken);
                running++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                steps[i] = step with
                {
                    Status = StepStatuses.Failed, Error = "Jarvis could not start this step.", CompletedAt = now
                };
                await repository.UpdateStepAsync(steps[i], cancellationToken);
            }
        }

        // A step that could not start may be what other steps were waiting for.
        await StopBlockedAsync();

        // 4. Close the mission when nothing is left to do.
        if (steps.Any(x => x.Status is StepStatuses.Pending or StepStatuses.Running)) 
        {
            await repository.UpdateAsync(mission with { UpdatedAt = now }, cancellationToken);
            return;
        }
        await FinishAsync(mission, steps, now, cancellationToken);
    }

    public async Task<IReadOnlyList<MissionNote>?> ReadNotesAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var step = await repository.FindStepByTaskAsync(taskId, cancellationToken);
        return step is null ? null : await repository.ListNotesAsync(step.MissionId, cancellationToken);
    }

    public async Task<bool> PostNoteAsync(Guid taskId, string key, string value, CancellationToken cancellationToken)
    {
        var step = await repository.FindStepByTaskAsync(taskId, cancellationToken);
        var cleanKey = MissionRules.Limit(key, MissionRules.MaxNoteKeyLength);
        var cleanValue = MissionRules.ClipText(value, MissionRules.MaxNoteValueLength);
        if (step is null || cleanKey is null || cleanValue.Length == 0) return false;
        var existing = await repository.ListNotesAsync(step.MissionId, cancellationToken);
        if (existing.Count >= MissionRules.MaxNotes && !existing.Any(x => x.Key == cleanKey)) return false;
        await repository.UpsertNoteAsync(new MissionNote(Guid.CreateVersion7(), step.MissionId, step.OwnerId, cleanKey,
            cleanValue, step.Key, clock.GetUtcNow()), cancellationToken);
        return true;
    }

    private async Task FinishAsync(Mission mission, IReadOnlyList<MissionStep> steps, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var failed = steps.Where(x => x.Status is StepStatuses.Failed or StepStatuses.Cancelled).ToArray();
        var last = steps.Where(x => x.Status == StepStatuses.Completed && !string.IsNullOrWhiteSpace(x.Result))
            .OrderByDescending(x => x.Ordinal).FirstOrDefault();
        var finished = mission with
        {
            Status = failed.Length == 0 ? MissionStatuses.Completed : MissionStatuses.Failed,
            Summary = last?.Result,
            FailureReason = failed.Length == 0
                ? null
                : "Did not finish: " + string.Join(", ", failed.Select(x => x.Title)),
            CompletedAt = now,
            UpdatedAt = now
        };
        await repository.UpdateAsync(finished, cancellationToken);
        await notifications.CreateAsync(mission.OwnerId,
            failed.Length == 0 ? "mission.completed" : "mission.failed",
            failed.Length == 0 ? "Mission finished" : "Mission needs attention",
            MissionRules.ClipText(failed.Length == 0 ? mission.Title : $"{mission.Title}: {finished.FailureReason}", 300),
            mission.Id, cancellationToken);
    }

    private async Task<MissionOperation<MissionDetail>> ChangeAsync(Guid id, Guid ownerId, string from, string to,
        string conflict, CancellationToken cancellationToken)
    {
        var mission = await repository.GetAsync(id, ownerId, cancellationToken);
        if (mission is null) return MissionOperation<MissionDetail>.NotFound();
        if (mission.Status != from) return MissionOperation<MissionDetail>.Conflict(conflict);
        mission = mission with { Status = to, UpdatedAt = clock.GetUtcNow() };
        await repository.UpdateAsync(mission, cancellationToken);
        return MissionOperation<MissionDetail>.Ok(await DetailAsync(mission, cancellationToken));
    }

    /// <summary>Steps that were stopped only because of this one get another chance.</summary>
    private async Task ReviveBlockedAsync(MissionStep step, CancellationToken cancellationToken)
    {
        foreach (var other in await repository.ListStepsAsync(step.MissionId, cancellationToken))
            if (other.Id != step.Id && other is { Status: StepStatuses.Cancelled, Error: BlockedError })
                await repository.UpdateStepAsync(other with
                {
                    Status = StepStatuses.Pending, Error = null, CompletedAt = null
                }, cancellationToken);
    }

    /// <summary>A finished mission whose step is retried or skipped runs again.</summary>
    private async Task ReopenIfNeededAsync(Guid missionId, Guid ownerId, CancellationToken cancellationToken)
    {
        var mission = await repository.GetAsync(missionId, ownerId, cancellationToken);
        if (mission is null || mission.Status is not (MissionStatuses.Failed or MissionStatuses.Completed)) return;
        await repository.UpdateAsync(mission with
        {
            Status = MissionStatuses.Running, CompletedAt = null, FailureReason = null,
            UpdatedAt = clock.GetUtcNow()
        }, cancellationToken);
        await AdvanceAsync(missionId, cancellationToken);
    }

    private async Task<MissionDetail> DetailAsync(Mission mission, CancellationToken cancellationToken)
    {
        var steps = (await repository.ListStepsAsync(mission.Id, cancellationToken)).OrderBy(x => x.Ordinal).ToArray();
        var statuses = new Dictionary<Guid, string>();
        foreach (var step in steps.Where(x => x is { Status: StepStatuses.Running, TaskId: not null }))
            if (await taskRepository.GetTaskByIdAsync(step.TaskId!.Value, cancellationToken) is { } task)
                statuses[step.TaskId!.Value] = task.Status;
        return new MissionDetail(mission, steps, await repository.ListNotesAsync(mission.Id, cancellationToken), statuses);
    }
}
