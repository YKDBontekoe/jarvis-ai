namespace Jarvis.Api.Security;

/// <summary>
/// Scoped owner identity for trusted server-side entry points (voice runtime, messaging channels, A2A,
/// background learning) that run agent turns without an authenticated HTTP user.
/// </summary>
public sealed class OwnerExecutionContext
{
    public Guid? OwnerId { get; private set; }

    public void Use(Guid ownerId)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner id is required.", nameof(ownerId));
        if (OwnerId is { } existing && existing != ownerId)
            throw new InvalidOperationException("This scope already runs as a different owner.");
        OwnerId = ownerId;
    }
}

public static class OwnerScopes
{
    /// <summary>Creates a DI scope whose <see cref="Jarvis.Application.Conversations.ICurrentUser"/> is the given owner.</summary>
    public static AsyncServiceScope CreateOwnerScope(this IServiceScopeFactory scopes, Guid ownerId)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OwnerExecutionContext>().Use(ownerId);
        return scope;
    }
}
