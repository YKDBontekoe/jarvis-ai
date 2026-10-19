using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Modes;

namespace Jarvis.Agents.Modes;

/// <summary>Tools for the owner's context mode. Switching only changes how Jarvis behaves for this owner.</summary>
internal sealed class ModeAgentTools(IModeService modes, ICurrentUser currentUser, IModeContextService? context = null)
{
    [Description("Show the mode Jarvis is in (normal, focus, commuting, meeting, sleep, travel, weekend), why, and how it changes notifications and replies.")]
    public async Task<string> GetCurrentModeAsync(CancellationToken cancellationToken = default)
    {
        var state = await modes.GetStateAsync(currentUser.OwnerId, cancellationToken);
        var definition = state.Modes.First(x => x.Definition.Id == state.Current.Mode).Definition;
        var text = new StringBuilder($"Mode: {definition.Label} ({state.Current.Source}). {state.Current.Reason}\n");
        text.Append("Phone notifications: ").AppendLine(state.Policy.Notifications switch
        {
            NotificationLevels.None => "none",
            NotificationLevels.Important => "only reminders and approvals",
            _ => "all"
        });
        if (state.Current.Until is { } until) text.Append("Until: ").AppendLine(AgentText.Time(until));
        text.Append("Available modes: ").AppendLine(string.Join(", ", ModeIds.All));
        return text.ToString();
    }

    [Description("Switch Jarvis to a mode when the user asks (\"ik ga een meeting in\", \"focus for two hours\", \"I'm going to sleep\"): focus, commuting, meeting, sleep, travel, weekend, normal. Quiet modes keep pushes off the phone. Pass hours for a timed mode, or mode=auto to let Jarvis decide again.")]
    public async Task<string> SetModeAsync(
        [Description("A mode id, or \"auto\".")] string mode,
        [Description("How many hours it lasts. Omit to keep it until the user changes it.")] double? hours = null,
        CancellationToken cancellationToken = default)
    {
        var minutes = hours is { } h ? (int?)Math.Round(h * 60) : null;
        var result = await modes.SetModeAsync(currentUser.OwnerId, mode, minutes, cancellationToken);
        if (!result.Succeeded) return "I could not switch the mode: " + result.Message;
        var current = result.Value!.Current;
        var label = result.Value.Modes.First(x => x.Definition.Id == current.Mode).Definition.Label;
        return mode.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? $"Automatic modes are back. Right now: {label}."
            : $"Switched to {label}" + (current.Until is { } until ? $" until {AgentText.Time(until)}." : ".");
    }

    [Description("Start a focus session when the user asks (\"focus for 45 minutes on the report\"). Say what they want to work on in their words; do not invent one. Only the phone's pushes and reply length change; nothing is approved or done for them.")]
    public async Task<string> StartFocusAsync(
        [Description("What the user chose to work on, or omit it.")] string? intention = null,
        [Description("Minutes, 5 to 240. Defaults to 45.")] int? minutes = null,
        CancellationToken cancellationToken = default)
    {
        if (context is null) return "Focus sessions are not available.";
        var result = await context.StartFocusAsync(currentUser.OwnerId, intention, minutes, cancellationToken);
        return result.Succeeded
            ? $"Focus is on until {AgentText.Time(result.Value!.Until ?? DateTimeOffset.UtcNow)}." +
              (string.IsNullOrEmpty(result.Value.Focus?.Intention) ? " No next action was chosen." : $" Next action: {result.Value.Focus!.Intention}")
            : "I could not start focus: " + result.Message;
    }
}
