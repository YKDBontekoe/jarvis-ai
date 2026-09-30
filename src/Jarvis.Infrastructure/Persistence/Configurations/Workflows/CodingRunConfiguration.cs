using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class CodingRunConfiguration : IEntityTypeConfiguration<CodingRun>
{
    public void Configure(EntityTypeBuilder<CodingRun> builder)
    {
            builder.ToTable("coding_runs");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Repository).HasMaxLength(120).IsRequired();
            builder.Property(x => x.Task).IsRequired();
            builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
            builder.Property(x => x.WorktreePath).HasColumnName("worktree_path").HasMaxLength(1024).IsRequired();
            builder.Property(x => x.DiffSummary).HasColumnName("diff_summary");
            builder.Property(x => x.ChangedFiles).HasColumnName("changed_files");
            builder.Property(x => x.Summary);
            builder.Property(x => x.Error).HasMaxLength(2000);
            builder.Property(x => x.ExitCode).HasColumnName("exit_code");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
            builder.Property(x => x.BranchName).HasColumnName("branch_name").HasMaxLength(200);
            builder.Property(x => x.PullRequestRepository).HasColumnName("pull_request_repository").HasMaxLength(200);
            builder.Property(x => x.PullRequestNumber).HasColumnName("pull_request_number");
            builder.Property(x => x.PullRequestUrl).HasColumnName("pull_request_url").HasMaxLength(500);
            builder.Property(x => x.PullRequestState).HasColumnName("pull_request_state").HasMaxLength(20);
            builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        }
}
