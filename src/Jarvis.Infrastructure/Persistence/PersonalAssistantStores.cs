using Jarvis.Application.Devices;
using Jarvis.Application.Integrations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Devices;
using Jarvis.Domain.Integrations;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class DeviceTelemetryStore(JarvisDbContext db) : IDeviceTelemetryStore
{
    public async Task<DeviceTelemetryRecord> SaveAsync(Guid ownerId, SaveDeviceTelemetryRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.DeviceTelemetry.SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        if (row is null)
        {
            row = new DeviceTelemetry { OwnerId = ownerId };
            db.DeviceTelemetry.Add(row);
        }
        if (request.Latitude is not null) row.Latitude = request.Latitude;
        if (request.Longitude is not null) row.Longitude = request.Longitude;
        if (request.AccuracyMeters is not null) row.AccuracyMeters = request.AccuracyMeters;
        if (request.BatteryPercent is not null) row.BatteryPercent = request.BatteryPercent;
        if (request.Charging is not null) row.Charging = request.Charging;
        row.ReportedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    public async Task<DeviceTelemetryRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.DeviceTelemetry.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken))
        is { } row ? ToRecord(row) : null;

    private static DeviceTelemetryRecord ToRecord(DeviceTelemetry row) =>
        new(row.OwnerId, row.Latitude, row.Longitude, row.AccuracyMeters, row.BatteryPercent, row.Charging,
            row.ReportedAt);
}

public sealed class McpOAuthSessionStore(JarvisDbContext db) : IMcpOAuthSessionStore
{
    public async Task<McpOAuthSessionRecord> SavePendingAsync(McpOAuthSessionDraft draft,
        CancellationToken cancellationToken)
    {
        var expired = DateTimeOffset.UtcNow.AddHours(-1);
        await db.McpOAuthSessions.Where(x => x.ExpiresAt < expired || x.Status != "pending")
            .ExecuteDeleteAsync(cancellationToken);
        db.McpOAuthSessions.Add(new McpOAuthSession
        {
            Id = draft.Id,
            OwnerId = draft.OwnerId,
            State = draft.State,
            Provider = draft.Provider,
            ServerKey = draft.ServerKey,
            CodeVerifier = draft.CodeVerifier,
            RedirectUri = draft.RedirectUri,
            AuthorizationEndpoint = draft.AuthorizationEndpoint,
            TokenEndpoint = draft.TokenEndpoint,
            RegistrationEndpoint = draft.RegistrationEndpoint,
            ClientId = draft.ClientId,
            Resource = draft.Resource,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = draft.ExpiresAt
        });
        await db.SaveChangesAsync(cancellationToken);
        return new McpOAuthSessionRecord(draft.Id, draft.Provider, "pending", draft.AuthorizationUrl, null,
            draft.ExpiresAt);
    }

    public async Task<McpOAuthSessionDraft?> GetByStateAsync(string state, CancellationToken cancellationToken)
    {
        var row = await db.McpOAuthSessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.State == state, cancellationToken);
        return row is null ? null : ToDraft(row);
    }

    public async Task<McpOAuthSessionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var row = await db.McpOAuthSessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        return row is null ? null : new McpOAuthSessionRecord(row.Id, row.Provider, row.Status, null, row.Error,
            row.ExpiresAt);
    }

    public async Task MarkCompletedAsync(Guid id, bool success, string? error, CancellationToken cancellationToken)
    {
        var row = await db.McpOAuthSessions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return;
        row.Status = success ? "completed" : "failed";
        row.Error = error;
        row.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static McpOAuthSessionDraft ToDraft(McpOAuthSession row) =>
        new(row.Id, row.OwnerId, row.State, row.Provider, row.ServerKey, row.CodeVerifier, row.RedirectUri,
            row.AuthorizationEndpoint, row.TokenEndpoint, row.RegistrationEndpoint, row.ClientId, row.Resource,
            row.ExpiresAt, row.Status, row.Error);
}

public sealed class CodingRunStore(JarvisDbContext db) : ICodingRunStore
{
    public async Task<CodingRunRecord> UpsertAsync(SaveCodingRunRequest request, CancellationToken cancellationToken)
    {
        var row = await db.CodingRuns.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (row is null)
        {
            row = new CodingRun { Id = request.Id, OwnerId = request.OwnerId, CreatedAt = DateTimeOffset.UtcNow };
            db.CodingRuns.Add(row);
        }
        row.Repository = request.Repository;
        row.Task = request.Task.Length <= 4000 ? request.Task : request.Task[..4000];
        row.Status = request.Status;
        row.WorktreePath = request.WorktreePath;
        row.DiffSummary = Truncate(request.DiffSummary, 8000);
        row.ChangedFiles = request.ChangedFiles is { Count: > 0 }
            ? string.Join('\n', request.ChangedFiles.Take(200))
            : null;
        row.Summary = Truncate(request.Summary, 16_000);
        row.Error = Truncate(request.Error, 2000);
        row.ExitCode = request.ExitCode;
        if (request.Status is "completed" or "failed") row.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    public async Task<IReadOnlyList<CodingRunRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.CodingRuns.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(cancellationToken))
        .Select(ToRecord).ToArray();

    public async Task<CodingRunRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.CodingRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken)) is { } row
            ? ToRecord(row)
            : null;

    public async Task<CodingRunRecord?> SetPullRequestAsync(Guid id, Guid ownerId, string? branchName,
        string repository, int number, string url, string state, CancellationToken cancellationToken)
    {
        var row = await db.CodingRuns.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (row is null) return null;
        if (!string.IsNullOrWhiteSpace(branchName)) row.BranchName = branchName;
        row.PullRequestRepository = repository;
        row.PullRequestNumber = number;
        row.PullRequestUrl = Truncate(url, 500);
        row.PullRequestState = state;
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    private static CodingRunRecord ToRecord(CodingRun row) =>
        new(row.Id, row.OwnerId, row.Repository, row.Task, row.Status, row.WorktreePath, row.DiffSummary,
            string.IsNullOrWhiteSpace(row.ChangedFiles) ? [] : row.ChangedFiles.Split('\n'),
            row.Summary, row.Error, row.ExitCode, row.CreatedAt, row.CompletedAt, row.BranchName,
            row.PullRequestRepository, row.PullRequestNumber, row.PullRequestUrl, row.PullRequestState);

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
