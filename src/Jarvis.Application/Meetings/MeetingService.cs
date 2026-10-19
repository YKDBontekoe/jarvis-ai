using System.Globalization;
using System.Text;
using Jarvis.Application.Decisions;
using Jarvis.Application.Events;
using Jarvis.Application.Inbox;
using Jarvis.Application.Integrations;
using Jarvis.Application.People;
using Jarvis.Application.Projects;
using Jarvis.Application.Search;

namespace Jarvis.Application.Meetings;

public sealed class MeetingService(
    IMeetingRepository repository,
    IProjectStore projects,
    IProjectFocusService focus,
    ICommitmentService commitments,
    IDecisionService decisions,
    IPeopleService people,
    ICalendarFeed? calendar = null,
    TimeProvider? timeProvider = null) : IMeetingService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<MeetingSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await repository.ListAsync(ownerId, cancellationToken))
        .OrderByDescending(item => item.StartsAt ?? item.UpdatedAt).ThenBy(item => item.Id)
        .Select(item => new MeetingSummary(item.Id, item.Title, item.StartsAt, item.ProjectId,
            item.Document.Candidates.Count(c => c.State == CandidateStates.Pending),
            item.Document.Candidates.Count(c => c.State == CandidateStates.Accepted), item.UpdatedAt)).ToArray();

    public Task<MeetingRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.GetAsync(id, ownerId, cancellationToken);

    public async Task<MeetingOperation<MeetingRecord>> CreateAsync(Guid ownerId, MeetingInput input,
        CancellationToken cancellationToken)
    {
        var title = Clean(input.Title, MeetingRules.MaxTitleLength);
        if (title is null) return MeetingOperation<MeetingRecord>.Invalid("title", "Give the meeting a title.");
        var template = input.Template ?? MeetingTemplates.General;
        if (!MeetingTemplates.All.Contains(template))
            return MeetingOperation<MeetingRecord>.Invalid("template", "Choose a supported meeting template.");
        if (input.ProjectId is { } projectId && await projects.GetAsync(projectId, ownerId, cancellationToken) is null)
            return MeetingOperation<MeetingRecord>.Invalid("projectId", "Project was not found.");
        var (participants, error) = await ParticipantsAsync(ownerId, input.Participants, cancellationToken);
        if (error is not null) return MeetingOperation<MeetingRecord>.Invalid("participants", error);
        if (await repository.CountAsync(ownerId, cancellationToken) >= MeetingRules.MaxPerOwner)
            return MeetingOperation<MeetingRecord>.Conflict("You can keep up to 300 meetings. Delete an old one first.");

        var now = clock.GetUtcNow();
        var record = new MeetingRecord(Guid.CreateVersion7(), ownerId, title, input.StartsAt, input.ProjectId,
            new MeetingDocument(Clean(input.Purpose, MeetingRules.MaxPurposeLength), template, participants, [], null,
                null, null, []), now, now);
        await repository.AddAsync(record, cancellationToken);
        return MeetingOperation<MeetingRecord>.Ok(record);
    }

    public async Task<MeetingOperation<MeetingRecord>> UpdateAsync(Guid id, Guid ownerId, MeetingEdit edit,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return MeetingOperation<MeetingRecord>.NotFound();
        var title = edit.Title is null ? current.Title : Clean(edit.Title, MeetingRules.MaxTitleLength);
        if (title is null) return MeetingOperation<MeetingRecord>.Invalid("title", "Give the meeting a title.");
        if (edit.PrivateNotes is { Length: > MeetingRules.MaxNotesLength })
            return MeetingOperation<MeetingRecord>.Invalid("privateNotes", $"Use at most {MeetingRules.MaxNotesLength} characters.");
        IReadOnlyList<AgendaItem> agenda = current.Document.Agenda;
        if (edit.Agenda is not null)
        {
            if (edit.Agenda.Count > MeetingRules.MaxAgendaItems)
                return MeetingOperation<MeetingRecord>.Invalid("agenda", $"Use at most {MeetingRules.MaxAgendaItems} agenda lines.");
            var lines = new List<AgendaItem>();
            foreach (var item in edit.Agenda)
            {
                var text = Clean(item.Text, MeetingRules.MaxAgendaTextLength);
                if (text is null) continue;
                lines.Add(new AgendaItem($"a{lines.Count + 1}", text, item.Shared));
            }
            agenda = lines;
        }
        var document = current.Document with
        {
            Purpose = edit.Purpose is null ? current.Document.Purpose : Clean(edit.Purpose, MeetingRules.MaxPurposeLength),
            PrivateNotes = edit.PrivateNotes is null ? current.Document.PrivateNotes
                : string.IsNullOrWhiteSpace(edit.PrivateNotes) ? null : edit.PrivateNotes.Trim(),
            Agenda = agenda
        };
        var updated = current with
        {
            Title = title, StartsAt = edit.StartsAt ?? current.StartsAt, Document = document, UpdatedAt = clock.GetUtcNow()
        };
        await repository.UpdateAsync(updated, cancellationToken);
        return MeetingOperation<MeetingRecord>.Ok(updated);
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<MeetingOperation<MeetingRecord>> PrepareAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return MeetingOperation<MeetingRecord>.NotFound();
        var today = await commitments.TodayAsync(ownerId, cancellationToken);

        string? projectName = null, outcome = null;
        ProjectNextActionView? next = null;
        var files = new List<PrepFile>();
        if (current.ProjectId is { } projectId && await projects.GetAsync(projectId, ownerId, cancellationToken) is { } project)
        {
            projectName = project.Name;
            outcome = project.Outcome;
            next = await focus.DescribeAsync(ownerId, project, cancellationToken);
            if (await projects.GetContentsAsync(projectId, ownerId, cancellationToken) is { } contents)
                files.AddRange(contents.Files.Where(file => file.ProcessingStatus != "deleting")
                    .Select(file => new PrepFile(file.Id, file.FileName)));
        }

        var inputs = new PrepInputs(current.Title, current.Document.Purpose, current.Document.Template, today,
            current.Document.Participants.Select(p => p.Name).ToArray(), current.ProjectId, projectName, outcome, next,
            await commitments.ListAsync(ownerId, null, CommitmentStatuses.Open, false, cancellationToken),
            await decisions.ListAsync(ownerId, null, DecisionRules.MaxLimit, cancellationToken), files,
            await repository.ListAsync(ownerId, cancellationToken), current.Id);
        var (prep, agenda) = MeetingPrepBuilder.Build(inputs, clock.GetUtcNow());

        // The owner's own agenda is never overwritten: it is only filled in when it is still empty.
        var updated = current with
        {
            Document = current.Document with
            {
                Prep = prep, Agenda = current.Document.Agenda.Count == 0 ? agenda : current.Document.Agenda
            },
            UpdatedAt = clock.GetUtcNow()
        };
        await repository.UpdateAsync(updated, cancellationToken);
        return MeetingOperation<MeetingRecord>.Ok(updated);
    }

    public async Task<MeetingOperation<MeetingRecord>> DebriefAsync(Guid id, Guid ownerId, string text,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return MeetingOperation<MeetingRecord>.NotFound();
        if (string.IsNullOrWhiteSpace(text)) return MeetingOperation<MeetingRecord>.Invalid("text", "Write or paste what happened.");
        if (text.Length > MeetingRules.MaxDebriefLength)
            return MeetingOperation<MeetingRecord>.Invalid("text", $"Use at most {MeetingRules.MaxDebriefLength} characters.");

        var today = await commitments.TodayAsync(ownerId, cancellationToken);
        var found = MeetingDebriefExtractor.Extract(text, current.Document.Participants.Select(p => p.Name).ToArray(), today);
        // What the owner already decided about a candidate survives a second read.
        var known = current.Document.Candidates.ToDictionary(candidate => candidate.Id);
        var merged = found.Select(candidate => known.TryGetValue(candidate.Id, out var previous) ? previous : candidate).ToList();
        merged.AddRange(current.Document.Candidates.Where(candidate => found.All(item => item.Id != candidate.Id) &&
                                                                         candidate.State != CandidateStates.Pending));
        var updated = current with
        {
            Document = current.Document with { DebriefText = text.Trim(), Candidates = merged.Take(MeetingRules.MaxCandidates).ToArray() },
            UpdatedAt = clock.GetUtcNow()
        };
        await repository.UpdateAsync(updated, cancellationToken);
        return MeetingOperation<MeetingRecord>.Ok(updated);
    }

    public async Task<MeetingOperation<MeetingRecord>> AcceptAsync(Guid id, Guid ownerId, string candidateId,
        CandidateAcceptance acceptance, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return MeetingOperation<MeetingRecord>.NotFound();
        var candidate = current.Document.Candidates.FirstOrDefault(item => item.Id == candidateId);
        if (candidate is null) return MeetingOperation<MeetingRecord>.NotFound();
        // Accepting again is the same acceptance: nothing is created twice.
        if (candidate.State == CandidateStates.Accepted) return MeetingOperation<MeetingRecord>.Ok(current);
        if (candidate.Kind == CandidateKinds.Question)
            return MeetingOperation<MeetingRecord>.Invalid("candidateId", "A question is something to ask, not something to accept. Dismiss it once answered.");

        var today = await commitments.TodayAsync(ownerId, cancellationToken);
        var description = Clean(acceptance.Description, InboxRules.MaxDescriptionLength) ?? candidate.Text;
        string resultRef;
        if (candidate.Kind is CandidateKinds.IOwe or CandidateKinds.TheyOwe)
        {
            var due = acceptance.ClearDue ? null : acceptance.DueOn ?? candidate.DueOn;
            var created = await commitments.CreateAsync(ownerId, new CommitmentDraft(
                candidate.Kind == CandidateKinds.IOwe ? CommitmentDirections.IOwe : CommitmentDirections.OwedToMe,
                Clean(acceptance.Counterparty, InboxRules.MaxCounterpartyLength) ?? candidate.Counterparty, description,
                due, CommitmentSources.Meeting), today, cancellationToken);
            if (!created.Succeeded)
                return MeetingOperation<MeetingRecord>.Invalid(created.Field ?? "description", created.Message ?? "Could not add that.");
            resultRef = new EntityRef(EntityTypes.Commitment, created.Value!.Id).ToString();
        }
        else
        {
            if (acceptance.Probability is not { } probability || acceptance.ReviewOn is not { } reviewOn)
                return MeetingOperation<MeetingRecord>.Invalid("probability",
                    "To log a decision say how sure you are it works out, and the day to check.");
            try
            {
                var title = $"Decided in {current.Title}";
                var decision = await decisions.CreateAsync(ownerId, new CreateDecisionRequest(
                    title.Length <= DecisionRules.MaxTitleLength ? title : title[..DecisionRules.MaxTitleLength],
                    "The decision works out as intended.", probability, reviewOn, description), cancellationToken);
                resultRef = new EntityRef(EntityTypes.Decision, decision.Id).ToString();
            }
            catch (ArgumentException exception)
            {
                return MeetingOperation<MeetingRecord>.Invalid(exception.ParamName ?? "reviewOn", exception.Message);
            }
        }

        var accepted = candidate with { State = CandidateStates.Accepted, ResultRef = resultRef };
        var updated = Replace(current, accepted);
        await repository.UpdateAsync(updated, cancellationToken);
        return MeetingOperation<MeetingRecord>.Ok(updated);
    }

    public async Task<MeetingOperation<MeetingRecord>> DismissAsync(Guid id, Guid ownerId, string candidateId,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return MeetingOperation<MeetingRecord>.NotFound();
        var candidate = current.Document.Candidates.FirstOrDefault(item => item.Id == candidateId);
        if (candidate is null) return MeetingOperation<MeetingRecord>.NotFound();
        // Dismissing records the choice and nothing else: no memory, reminder or record changes.
        if (candidate.State == CandidateStates.Accepted)
            return MeetingOperation<MeetingRecord>.Conflict("That was already accepted. Change it where it now lives.");
        var updated = Replace(current, candidate with { State = CandidateStates.Dismissed });
        await repository.UpdateAsync(updated, cancellationToken);
        return MeetingOperation<MeetingRecord>.Ok(updated);
    }

    public async Task<MeetingOperation<FollowUpDraft>> FollowUpAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return MeetingOperation<FollowUpDraft>.NotFound();
        var accepted = current.Document.Candidates.Where(item => item.State == CandidateStates.Accepted).ToArray();
        var shared = current.Document.Agenda.Where(item => item.Shared).Select(item => item.Text).ToArray();
        var text = new StringBuilder();
        var names = current.Document.Participants.Select(p => p.Name.Split(' ')[0]).ToArray();
        text.AppendLine(names.Length == 0 ? "Hi," : $"Hi {string.Join(" and ", names)},").AppendLine();
        text.Append("Thanks for the time on ").Append((current.StartsAt ?? current.CreatedAt).ToString("d MMMM", CultureInfo.InvariantCulture))
            .AppendLine(". A short recap:").AppendLine();
        var included = new List<string>();
        void Section(string heading, IEnumerable<string> lines)
        {
            var list = lines.ToArray();
            if (list.Length == 0) return;
            text.AppendLine(heading);
            foreach (var line in list) text.Append("- ").AppendLine(line);
            text.AppendLine();
            included.AddRange(list);
        }
        Section("Discussed:", shared);
        Section("Decided:", accepted.Where(c => c.Kind == CandidateKinds.Decision).Select(c => c.Text));
        Section("I will:", accepted.Where(c => c.Kind == CandidateKinds.IOwe).Select(Line));
        Section("You will:", accepted.Where(c => c.Kind == CandidateKinds.TheyOwe).Select(Line));
        if (included.Count == 0)
            text.AppendLine("(Accept what was decided or promised first, and it appears here.)").AppendLine();
        text.AppendLine("Let me know if I missed anything.");
        // Private notes never appear: only the agenda lines marked shared and what the owner accepted do.
        return MeetingOperation<FollowUpDraft>.Ok(new FollowUpDraft($"Follow-up: {current.Title}", text.ToString().TrimEnd(), false, included));

        static string Line(DebriefCandidate item) =>
            item.DueOn is { } due ? $"{item.Text} (by {due.ToString("d MMMM", CultureInfo.InvariantCulture)})" : item.Text;
    }

    public async Task<IReadOnlyList<MeetingCalendarEvent>> UpcomingEventsAsync(Guid ownerId, int days,
        CancellationToken cancellationToken)
    {
        if (calendar is null) return [];
        var now = clock.GetUtcNow();
        try
        {
            return (await calendar.ListUpcomingAsync(ownerId, now.AddHours(-1), now.AddDays(Math.Clamp(days, 1, 14)), cancellationToken))
                .OrderBy(item => item.StartAt).Take(30)
                .Select(item => new MeetingCalendarEvent(item.Title, item.StartAt, item.EndAt)).ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or TaskCanceledException)
        {
            // An unavailable calendar is an empty list here, never a guess at free time.
            return [];
        }
    }

    private static MeetingRecord Replace(MeetingRecord record, DebriefCandidate candidate) =>
        record with
        {
            Document = record.Document with
            {
                Candidates = record.Document.Candidates.Select(item => item.Id == candidate.Id ? candidate : item).ToArray()
            },
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private async Task<(IReadOnlyList<MeetingParticipant>, string?)> ParticipantsAsync(Guid ownerId,
        IReadOnlyList<MeetingParticipant>? input, CancellationToken cancellationToken)
    {
        var result = new List<MeetingParticipant>();
        foreach (var participant in input ?? [])
        {
            if (result.Count >= MeetingRules.MaxParticipants) return ([], $"Use at most {MeetingRules.MaxParticipants} participants.");
            var name = Clean(participant.Name, 80);
            if (participant.PersonId is { } personId)
            {
                // A linked person must be the owner's own; their name is taken from People, not from the request.
                var person = await people.GetAsync(personId, ownerId, cancellationToken);
                if (person is null) return ([], "That person was not found.");
                name = person.Name;
            }
            if (name is null) return ([], "Every participant needs a name.");
            if (result.Any(item => SearchRanking.NormalizeQuery(item.Name) == SearchRanking.NormalizeQuery(name))) continue;
            result.Add(new MeetingParticipant(name, participant.PersonId));
        }
        return (result, null);
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }
}
