namespace Jarvis.Domain.Workflows;

public sealed class CodingRun
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Repository { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public string WorktreePath { get; set; } = string.Empty;
    public string? DiffSummary { get; set; }
    public string? ChangedFiles { get; set; }
    public string? Summary { get; set; }
    public string? Error { get; set; }
    public int? ExitCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
