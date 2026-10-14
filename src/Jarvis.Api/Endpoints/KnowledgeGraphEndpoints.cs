using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;

namespace Jarvis.Api.Endpoints;

internal static class KnowledgeGraphEndpoints
{
    public static RouteGroupBuilder MapKnowledgeGraphEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/graph/overview", async (int? limit, IKnowledgeGraphRepository graph, ICurrentUser currentUser,
                CancellationToken ct) =>
                Results.Ok(await graph.GetOverviewAsync(currentUser.OwnerId, Math.Clamp(limit ?? 60, 1, KnowledgeGraphLimits.MaxOverviewEntities), ct)))
            .WithName("GetKnowledgeGraphOverview");

        api.MapPost("/graph/ask", async (AskGraphRequest request, IGraphQuestionService questions,
                ICurrentUser currentUser, CancellationToken ct) =>
            {
                var question = request.Question?.Trim();
                if (string.IsNullOrEmpty(question))
                    return EndpointHelpers.Invalid("question", "Ask a question.");
                if (question.Length > GraphQuestionService.MaxQuestionLength)
                    return EndpointHelpers.Invalid("question",
                        $"Ask in {GraphQuestionService.MaxQuestionLength} characters or fewer.");
                var focus = request.FocusIds ?? [];
                if (focus.Count > GraphQuestionService.MaxFocus)
                    return EndpointHelpers.Invalid("focusIds",
                        $"Focus on {GraphQuestionService.MaxFocus} entities or fewer.");
                var history = (request.History ?? [])
                    .Where(turn => turn is not null && !string.IsNullOrWhiteSpace(turn.Question))
                    .TakeLast(GraphQuestionService.MaxHistory)
                    .Select(turn => new GraphQuestionTurn(Clip(turn.Question, GraphQuestionService.MaxQuestionLength),
                        Clip(turn.Answer ?? "", 2_000)))
                    .ToArray();
                return Results.Ok(await questions.AskAsync(currentUser.OwnerId, question, focus, history, ct));
            })
            .WithName("AskKnowledgeGraph");

        api.MapGet("/graph/entities", async (string? search, IKnowledgeGraphRepository graph, ICurrentUser currentUser,
                CancellationToken ct) =>
            {
                if (search?.Length > 200) return EndpointHelpers.Invalid("search", "Search with 200 characters or fewer.");
                return Results.Ok(await graph.ListEntitiesAsync(currentUser.OwnerId, search, 100, ct));
            })
            .WithName("ListKnowledgeGraphEntities");

        api.MapGet("/graph/entities/{id:guid}", async (Guid id, IKnowledgeGraphRepository graph,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var details = await graph.GetEntityAsync(currentUser.OwnerId, id, ct);
            return details is null ? Results.NotFound() : Results.Ok(details);
        }).WithName("GetKnowledgeGraphEntity");

        api.MapDelete("/graph/entities/{id:guid}", async (Guid id, IKnowledgeGraphRepository graph,
                ICurrentUser currentUser, CancellationToken ct) =>
            await graph.DeleteEntityAsync(currentUser.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteKnowledgeGraphEntity");

        api.MapGet("/memory/index-status", async (IMemoryIndexRepository index, ICurrentUser currentUser,
                CancellationToken ct) => Results.Ok(await index.GetStatusAsync(currentUser.OwnerId, ct)))
            .WithName("GetMemoryIndexStatus");

        return api;
    }

    private static string Clip(string value, int length) =>
        value.Length <= length ? value.Trim() : value[..length].Trim();
}
