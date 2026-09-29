using System.ComponentModel;
using System.Text.Json;
using Jarvis.Application.Conversations;
using Jarvis.Application.Realtime;
using Jarvis.Application.Surfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Surfaces;

internal sealed class SurfaceAgentTools(
    IUiSurfaceRepository surfaces,
    Guid? conversationId,
    IRealtimePublisher realtime,
    ICurrentUser currentUser)
{
    [Description("Show one interactive card in the Jarvis app when a tap or a short typed answer is easier than Markdown. Only one card stays open: rendering another closes the previous one. Do not call this twice in one turn. choice: tappable options in items, each with its own action. form: one question per field (label is the question, placeholder is an example) and one primary action. Use type secret plus credentialProvider for tokens so they never enter the transcript. Action url opens an authorization page. Keep the written reply to a single short sentence.")]
    public async Task<string> RenderUiAsync(
        [Description("card, form, choice, status, or list.")] string kind,
        [Description("Short title shown at the top of the card.")] string title,
        [Description("Optional supporting text under the title.")] string? body = null,
        [Description("List items as JSON: [{id,title,subtitle,detail}].")] string? itemsJson = null,
        [Description("Form fields as JSON: [{id,label,type,placeholder,options,secretName}]. type is text, number, toggle, choice, or secret.")] string? fieldsJson = null,
        [Description("Buttons as JSON: [{id,label,style,url}]. style is primary, secondary, or danger. url is an optional public HTTPS link to open.")] string? actionsJson = null,
        [Description("Credential provider slug when the form has a secret field, such as github or a jarvis-mcp- id.")] string? credentialProvider = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId is not { } id)
            return "RenderUi is only available during an interactive conversation.";
        return await UiSurfaceRender.RenderAsync(surfaces, realtime, currentUser, id, kind, title, body,
            itemsJson, fieldsJson, actionsJson, credentialProvider, cancellationToken);
    }
}

internal sealed class SurfaceToolContributor(
    IUiSurfaceRepository surfaces,
    IRealtimePublisher realtime,
    ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context) =>
        [AIFunctionFactory.Create(new SurfaceAgentTools(surfaces, context.ConversationId, realtime, currentUser)
            .RenderUiAsync)];
}

internal sealed class SurfaceContextContributor : IAgentContextContributor
{
    public int Order => 40;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new SurfaceContextProvider()];
}

internal sealed class SurfaceContextProvider : MessageAIContextProvider
{
    internal const string Prefix = "Generative UI";

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        const string text = Prefix +
            ": call RenderUi at most once per turn, and only when a tap or a short typed answer beats Markdown. If a card is already open, wait for it. " +
            "Choices go in items with one action each. Typed answers go in form fields whose label is the question and whose placeholder is an example. " +
            "Tokens use type secret plus credentialProvider — never ask the user to paste a secret into chat. Authorization buttons may include a url. " +
            "The written reply is one short sentence; do not repeat the card.";
        return new ValueTask<IEnumerable<ChatMessage>>([new ChatMessage(ChatRole.User, text)]);
    }
}

internal static class UiSurfaceRender
{
    public static async Task<string> RenderAsync(IUiSurfaceRepository surfaces, IRealtimePublisher realtime,
        ICurrentUser currentUser, Guid conversationId, string kind, string title, string? body,
        string? itemsJson, string? fieldsJson, string? actionsJson, string? credentialProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var schema = UiSurfaceSchema.Normalize(kind, title, body, Parse(itemsJson), Parse(fieldsJson),
                Parse(actionsJson), credentialProvider);
            var created = await surfaces.CreateAsync(currentUser.OwnerId, conversationId,
                (kind ?? UiSurfaceKinds.Card).Trim().ToLowerInvariant(), title.Trim(), schema, cancellationToken);
            foreach (var previous in created.Replaced)
                await PublishAsync(realtime, conversationId, previous, cancellationToken);
            await PublishAsync(realtime, conversationId, created.Surface, cancellationToken);
            var closed = created.Replaced.Count == 0
                ? ""
                : $" Closed {created.Replaced.Count} older card{(created.Replaced.Count == 1 ? "" : "s")} so only this one is active.";
            return $"Rendered a {created.Surface.Kind} titled '{created.Surface.Title}' in the app (surface {created.Surface.Id:N}).{closed} Keep your reply to one short sentence; the card asks the question.";
        }
        catch (ArgumentException exception)
        {
            return "The UI was not shown: " + exception.Message;
        }
    }

    private static Task PublishAsync(IRealtimePublisher realtime, Guid conversationId, UiSurfaceRecord surface,
        CancellationToken cancellationToken) =>
        realtime.PublishToConversationAsync(conversationId, "ui.surface", new
        {
            id = surface.Id,
            conversationId,
            kind = surface.Kind,
            title = surface.Title,
            status = surface.Status,
            schema = JsonSerializer.Deserialize<JsonElement>(surface.SchemaJson)
        }, cancellationToken);

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { throw new ArgumentException("Items, fields, and actions must be JSON arrays."); }
    }
}
