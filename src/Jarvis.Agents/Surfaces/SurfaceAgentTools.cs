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
    [Description("Show one native card in the Jarvis app when a tap, a short typed answer, or a visual summary beats Markdown. Only one card stays open: rendering another closes the previous one. Do not call this twice in one turn. choice: tappable options in items, each with its own action (multiSelect lets the owner pick several and adds a Done button). form: one question per field (label is the question, placeholder is an example) and one primary action. list/status: items with optional icon, value and badge. custom: compose your own card from blocks (stats, bars, steps, progress, pairs, chips, callout, heading, text, divider, columns); blocks can also sit above the items or fields of any kind. Use type secret plus credentialProvider for tokens so they never enter the transcript. Action url opens an authorization page. Keep the written reply to a single short sentence.")]
    public async Task<string> RenderUiAsync(
        [Description("card, form, choice, status, list, or custom.")] string kind,
        [Description("Short title shown at the top of the card.")] string title,
        [Description("Optional supporting text under the title.")] string? body = null,
        [Description("List items as JSON: [{id,title,subtitle,detail,icon,value,badge}]. value is a short trailing figure such as '€12' or '3 h'; badge is a short tag only for what stands out (Due Fri, Higher), left out otherwise. icon is one of: " + IconNames + ".")] string? itemsJson = null,
        [Description("Form fields as JSON: [{id,label,type,placeholder,options,secretName,min,max,step,unit}]. type is text, longtext, number, slider (min, max, step, unit), date, time, toggle, choice (options; up to 5 show as chips), or secret.")] string? fieldsJson = null,
        [Description("Buttons as JSON: [{id,label,style,url}]. style is primary, secondary, or danger. url is an optional public HTTPS link to open.")] string? actionsJson = null,
        [Description("Credential provider slug when the form has a secret field, such as github or a jarvis-mcp- id.")] string? credentialProvider = null,
        [Description("Custom blocks as JSON, drawn natively in order. Types: {type:'heading',text} {type:'text',text,tone} {type:'stats',items:[{label,value,delta,trend:'up'|'down'|'flat'}]} {type:'progress',label,value:0..1,detail,tone} {type:'bars',unit,items:[{label,value}]} {type:'steps',items:[{title,detail,done}]} {type:'pairs',items:[{label,value}]} {type:'chips',labels:[]} {type:'callout',text,tone,icon} {type:'divider'} {type:'columns',children:[up to 3 blocks]}. tone is neutral, info, success, warning, or danger. At most 24 blocks and 8 entries per block.")] string? blocksJson = null,
        [Description("For choice cards: let the owner pick several items before sending.")] bool multiSelect = false,
        CancellationToken cancellationToken = default)
    {
        if (conversationId is not { } id)
            return "RenderUi is only available during an interactive conversation.";
        return await UiSurfaceRender.RenderAsync(surfaces, realtime, currentUser, id, kind, title, body,
            itemsJson, fieldsJson, actionsJson, credentialProvider, cancellationToken, blocksJson, multiSelect);
    }

    private const string IconNames =
        "alarm, bell, book, brain, calendar, car, chart, check, clock, fire, food, gift, globe, heart, home, idea, " +
        "info, key, lightning, list, location, lock, money, phone, plane, receipt, rocket, shopping, sparkle, star, " +
        "target, timer, trophy, trend-up, trend-down, user, users, warning, work";
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

internal sealed class SurfaceContextContributor(IUiSurfaceRepository surfaces) : IAgentContextContributor
{
    public int Order => 40;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new SurfaceContextProvider(surfaces, context.OwnerId, context.ConversationId)];
}

internal sealed class SurfaceContextProvider(IUiSurfaceRepository surfaces, Guid ownerId, Guid? conversationId)
    : MessageAIContextProvider
{
    internal const string Prefix = "Generative UI";

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var text = Prefix +
            ": call RenderUi at most once per turn, and only when a tap, a short typed answer, or a visual summary beats Markdown. If a card is already open, wait for it. " +
            "Choices go in items with one action each (multiSelect for several). Typed answers go in form fields whose label is the question and whose placeholder is an example; use date, time, slider, or choice fields when they fit better than text. " +
            "Do not write Markdown tables: when a reply would need one (figures side by side, a comparison, progress, a plan), compose a custom card from blocks instead and keep the text to one sentence. " +
            "When the owner asks to see several things that each carry an amount, a time, or a state (bills, appointments, orders), show them as a list card with value and badge rather than a Markdown list. " +
            "Tokens use type secret plus credentialProvider — never ask the user to paste a secret into chat. Authorization buttons may include a url. " +
            "The written reply is one short sentence; do not repeat the card.";
        if (await LatestCardAsync(cancellationToken) is { } note) text += " " + note;
        return [new ChatMessage(ChatRole.User, text)];
    }

    /// <summary>What happened to the conversation's newest card, when the owner still has to act or chose not to.</summary>
    private async Task<string?> LatestCardAsync(CancellationToken cancellationToken)
    {
        if (conversationId is not { } id) return null;
        var latest = (await surfaces.ListForConversationAsync(ownerId, id, cancellationToken)).LastOrDefault();
        // The title is the model's own words, quoted as data.
        return latest?.Status switch
        {
            "open" => $"The card \"{AgentText.Limit(latest.Title, 80)}\" is still open. If the owner answers it in " +
                      "words instead, use that answer and do not show the card again.",
            "dismissed" => $"The owner closed the card \"{AgentText.Limit(latest.Title, 80)}\" instead of tapping it; " +
                           "their next message may answer it in words. Do not show it again unless they ask.",
            _ => null
        };
    }
}

internal static class UiSurfaceRender
{
    public static async Task<string> RenderAsync(IUiSurfaceRepository surfaces, IRealtimePublisher realtime,
        ICurrentUser currentUser, Guid conversationId, string kind, string title, string? body,
        string? itemsJson, string? fieldsJson, string? actionsJson, string? credentialProvider,
        CancellationToken cancellationToken, string? blocksJson = null, bool multiSelect = false)
    {
        try
        {
            var schema = UiSurfaceSchema.Normalize(kind, title, body, Parse(itemsJson), Parse(fieldsJson),
                Parse(actionsJson), credentialProvider, Parse(blocksJson), multiSelect);
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
        catch (JsonException) { throw new ArgumentException("Items, fields, actions, and blocks must be JSON arrays."); }
    }
}
