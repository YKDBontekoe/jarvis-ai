namespace Jarvis.Application.Workflows;

public sealed record CodingRunRecord(Guid Id, Guid OwnerId, string Repository, string Task, string Status,
    string WorktreePath, string? DiffSummary, IReadOnlyList<string> ChangedFiles, string? Summary, string? Error,
    int? ExitCode, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record SaveCodingRunRequest(Guid Id, Guid OwnerId, string Repository, string Task, string Status,
    string WorktreePath, string? DiffSummary, IReadOnlyList<string>? ChangedFiles, string? Summary, string? Error,
    int? ExitCode);

public interface ICodingRunStore
{
    Task<CodingRunRecord> UpsertAsync(SaveCodingRunRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodingRunRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<CodingRunRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public static class GeoDistance
{
    /// <summary>Great-circle distance in meters between two WGS84 points.</summary>
    public static double Meters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthMeters = 6_371_000;
        var lat1 = DegreesToRadians(latitude1);
        var lat2 = DegreesToRadians(latitude2);
        var dLat = DegreesToRadians(latitude2 - latitude1);
        var dLon = DegreesToRadians(longitude2 - longitude1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * earthMeters * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
