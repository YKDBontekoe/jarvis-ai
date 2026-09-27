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
    [Description("Render an interactive native card, form, choice list, status, or list in the Jarvis app. Use this when the user should pick an option, fill a short form, confirm a plan, or review structured items instead of reading a long Markdown reply. Keep copy short. Actions come back as the user's next message.")]
    public async Task<string> RenderUiAsync(
        [Description("card, form, choice, status, or list.")] string kind,
        [Description("Short title shown at the top of the card.")] string title,
        [Description("Optional supporting text under the title.")] string? body = null,
        [Description("List items as JSON: [{id,title,subtitle,detail}].")] string? itemsJson = null,
        [Description("Form fields as JSON: [{id,label,type,placeholder,options}]. type is text, number, toggle, or choice.")] string? fieldsJson = null,
        [Description("Buttons as JSON: [{id,label,style}]. style is primary, secondary, or danger.")] string? actionsJson = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId is not { } id)
            return "RenderUi is only available during an interactive conversation.";
        try
        {
            var schema = UiSurfaceSchema.Normalize(kind, title, body, Parse(itemsJson), Parse(fieldsJson),
                Parse(actionsJson));
            var surface = await surfaces.CreateAsync(currentUser.OwnerId, id,
                (kind ?? UiSurfaceKinds.Card).Trim().ToLowerInvariant(), title.Trim(), schema, cancellationToken);
            await realtime.PublishToConversationAsync(id, "ui.surface", new
            {
                id = surface.Id,
                conversationId = id,
                kind = surface.Kind,
                title = surface.Title,
                status = surface.Status,
                schema = JsonSerializer.Deserialize<JsonElement>(surface.SchemaJson)
            }, cancellationToken);
            return $"Rendered a {surface.Kind} titled '{surface.Title}' in the app (surface {surface.Id:N}). Keep your spoken or written reply short; the card is the main answer.";
        }
        catch (ArgumentException exception)
        {
            return "The UI was not shown: " + exception.Message;
        }
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { throw new ArgumentException("Items, fields, and actions must be JSON arrays."); }
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
            ": when a choice, form, plan, or short list would be easier in the app than as Markdown, call RenderUi. Keep the text reply brief. Do not invent extra chrome.";
        return new ValueTask<IEnumerable<ChatMessage>>([new ChatMessage(ChatRole.User, text)]);
    }
}
