using System.Globalization;
using Jarvis.Application.Decisions;
using Jarvis.Application.Inbox;
using Jarvis.Application.Projects;
using Jarvis.Application.Search;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Meetings;

public sealed record PrepFile(Guid Id, string Name);

public sealed record PrepInputs(
    string Title,
    string? Purpose,
    string Template,
    DateOnly Today,
    IReadOnlyList<string> ParticipantNames,
    Guid? ProjectId,
    string? ProjectName,
    string? ProjectOutcome,
    ProjectNextActionView? NextAction,
    IReadOnlyList<Commitment> Commitments,
    IReadOnlyList<DecisionView> Decisions,
    IReadOnlyList<PrepFile> ProjectFiles,
    IReadOnlyList<MeetingRecord> EarlierMeetings,
    Guid MeetingId);

/// <summary>
/// Builds the prep pack from facts the owner already has: what is open with the people involved, decisions still
/// waiting, the project's outcome and next step, the sources in the project and what an earlier meeting left unanswered.
/// Pure and deterministic. Every fact points back to where it came from; what is missing becomes a question to ask,
/// never an invented agreement.
/// </summary>
public static class MeetingPrepBuilder
{
    public const int MaxLoops = 8;
    public const int MaxDecisions = 5;
    public const int MaxFiles = 6;
    public const int MaxEarlier = 5;

    public static (MeetingPrep Prep, IReadOnlyList<AgendaItem> Agenda) Build(PrepInputs input, DateTimeOffset now)
    {
        var items = new List<PrepItem>();
        var agenda = new List<AgendaItem>();
        void AddAgenda(string text) => agenda.Add(new AgendaItem($"a{agenda.Count + 1}", Limit(text), true));

        if (!string.IsNullOrWhiteSpace(input.Purpose)) AddAgenda(input.Purpose.Trim());

        var people = input.ParticipantNames.Select(name => (Name: name, Key: SearchRanking.NormalizeQuery(name))).ToArray();
        var projectKey = input.ProjectName is null ? null : SearchRanking.NormalizeQuery(input.ProjectName);

        // Project: what it is for and the one next step.
        if (input.ProjectId is { } projectId && input.ProjectName is not null)
        {
            var project = $"project:{projectId:D}";
            items.Add(input.ProjectOutcome is { Length: > 0 } outcome
                ? new PrepItem("Project", PrepItemKinds.Fact, $"“{input.ProjectName}” is for: {outcome}", project)
                : new PrepItem("Project", PrepItemKinds.Question,
                    $"“{input.ProjectName}” has no outcome yet. What do you want it to achieve?", project));
            if (input.NextAction is { } next)
            {
                var label = next.Title ?? next.Note ?? "(nothing written)";
                var state = next.State == ProjectNextActionStates.Done ? " (done)" : next.State == ProjectNextActionStates.Gone ? " (gone)" : string.Empty;
                items.Add(new PrepItem("Project", PrepItemKinds.Fact, $"Next step: {label}{state}",
                    next.Type is not null && next.Id is not null ? $"{next.Type}:{next.Id:D}" : project));
                if (next.State == ProjectNextActionStates.Open) AddAgenda($"Next step: {label}");
            }
            else items.Add(new PrepItem("Project", PrepItemKinds.Question,
                $"“{input.ProjectName}” has no next step chosen. What should it be?", project));
        }

        // Open loops with the people in the room.
        var loops = input.Commitments
            .Where(item => item is { Suggested: false, Status: CommitmentStatuses.Open })
            .Select(item => (Item: item, Person: people.FirstOrDefault(person => SameName(item.Counterparty, person.Key))))
            .Where(pair => pair.Person.Name is not null)
            .OrderBy(pair => pair.Item.DueOn is { } due && due < input.Today ? 0 : 1)
            .ThenBy(pair => pair.Item.DueOn ?? DateOnly.MaxValue)
            .ThenBy(pair => pair.Item.Id)
            .Take(MaxLoops).ToArray();
        foreach (var (commitment, person) in loops)
        {
            var due = commitment.DueOn is { } date
                ? date < input.Today
                    ? $" — {input.Today.DayNumber - date.DayNumber} days overdue"
                    : $" — due {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                : string.Empty;
            var mine = commitment.Direction == CommitmentDirections.IOwe;
            items.Add(new PrepItem("Open loops", PrepItemKinds.Fact,
                (mine ? $"You owe {person.Name}: " : $"{person.Name} owes you: ") + commitment.Description + due,
                $"commitment:{commitment.Id:D}"));
            if (mine && commitment.DueOn is { } overdue && overdue < input.Today)
                AddAgenda($"Close the loop with {person.Name}: {commitment.Description}");
        }
        foreach (var person in people.Where(person => loops.All(pair => pair.Person.Key != person.Key)))
            items.Add(new PrepItem("Open loops", PrepItemKinds.Question,
                $"Nothing is tracked with {person.Name}. Is there anything you owe each other?"));

        // Decisions still waiting that touch the people or the project.
        var decisions = input.Decisions
            .Where(item => item.Status != DecisionStatuses.Resolved)
            .Where(item =>
            {
                var text = SearchRanking.NormalizeQuery($"{item.Title} {item.Prediction} {item.Context}");
                return people.Any(person => text.Contains(person.Key, StringComparison.Ordinal)) ||
                       projectKey is not null && text.Contains(projectKey, StringComparison.Ordinal);
            })
            .OrderBy(item => item.ReviewOn).ThenBy(item => item.Id).Take(MaxDecisions).ToArray();
        foreach (var decision in decisions)
        {
            items.Add(new PrepItem("Decisions", PrepItemKinds.Fact,
                $"Open decision: {decision.Title}. You predicted “{decision.Prediction}” " +
                $"({(decision.Probability * 100).ToString("0", CultureInfo.InvariantCulture)}% sure), " +
                $"review {decision.ReviewOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.",
                $"decision:{decision.Id:D}"));
        }

        // Sources the owner can bring.
        foreach (var file in input.ProjectFiles.OrderBy(file => file.Name, StringComparer.Ordinal).Take(MaxFiles))
            items.Add(new PrepItem("Sources", PrepItemKinds.Fact, $"File in the project: {file.Name}", $"file:{file.Id:D}"));

        // What an earlier meeting with the same people or project left open.
        var earlier = input.EarlierMeetings
            .Where(meeting => meeting.Id != input.MeetingId)
            .Where(meeting => meeting.ProjectId is not null && meeting.ProjectId == input.ProjectId ||
                              meeting.Document.Participants.Any(p => people.Any(person => SameName(p.Name, person.Key))))
            .OrderByDescending(meeting => meeting.StartsAt ?? meeting.CreatedAt).ThenBy(meeting => meeting.Id)
            .Take(MaxEarlier);
        foreach (var meeting in earlier)
        {
            var on = (meeting.StartsAt ?? meeting.CreatedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var open in meeting.Document.Candidates.Where(c => c.State == CandidateStates.Pending))
            {
                var label = open.Kind == CandidateKinds.Question ? "Still unanswered" : "Not reviewed yet";
                items.Add(new PrepItem("From earlier meetings", PrepItemKinds.Fact,
                    $"{label} from “{meeting.Title}” ({on}): {open.Text}", $"meeting:{meeting.Id:D}"));
            }
        }

        // Questions: gaps first, then the template's own.
        if (string.IsNullOrWhiteSpace(input.Purpose))
            items.Add(new PrepItem("Questions", PrepItemKinds.Question, "What is this meeting for, and what would make it a success?"));
        var asked = 0;
        foreach (var question in TemplateQuestions(input.Template))
        {
            items.Add(new PrepItem("Questions", PrepItemKinds.Question, question));
            if (asked++ < 2) AddAgenda(question);
        }

        return (new MeetingPrep(now, items), agenda.Take(MeetingRules.MaxAgendaItems).ToArray());
    }

    public static IReadOnlyList<string> TemplateQuestions(string template) => template switch
    {
        MeetingTemplates.OneOnOne =>
        [
            "What is going well, and what is getting in the way?",
            "Is there anything they need from you?",
            "What should happen before the next time you talk?"
        ],
        MeetingTemplates.ProjectUpdate =>
        [
            "What has moved since the last update, and what is stuck?",
            "What is the next step, who owns it and by when?",
            "What needs a decision?"
        ],
        MeetingTemplates.Interview =>
        [
            "What do you most need to learn about them?",
            "Which of their answers would change your mind?",
            "What will you tell them about next steps?"
        ],
        MeetingTemplates.Decision =>
        [
            "What exactly has to be decided, and by when?",
            "Who decides, and what do they need to see?",
            "What would you do if you could not decide today?"
        ],
        _ =>
        [
            "What do you want to leave the meeting with?",
            "What needs a decision, and who owns each next step?"
        ]
    };

    private static bool SameName(string name, string key) =>
        SearchRanking.NormalizeQuery(name) == key;

    private static string Limit(string text) =>
        text.Length <= MeetingRules.MaxAgendaTextLength ? text : text[..(MeetingRules.MaxAgendaTextLength - 1)].TrimEnd() + "…";
}
