using Forge.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Coordination.EntityConfigurations;

public class TaskConfiguration : IEntityTypeConfiguration<ForgeTask>
{
    public void Configure(EntityTypeBuilder<ForgeTask> builder)
    {
        builder.ToTable("tasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").UseSerialColumn();
        builder.Property(t => t.GitHubIssueNumber).HasColumnName("github_issue_number").IsRequired();
        builder.Property(t => t.GitHubRepo).HasColumnName("github_repo").IsRequired();
        builder.Property(t => t.Status).HasColumnName("status");
        builder.Property(t => t.ClaimedBy).HasColumnName("claimed_by");
        builder.Property(t => t.ClaimedAt).HasColumnName("claimed_at").HasColumnType("timestamp with time zone");
        builder.Property(t => t.PrNumber).HasColumnName("pr_number");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.ClaimedBy);
        builder.HasIndex(new[] { "GitHubIssueNumber", "GitHubRepo" }).IsUnique();

        builder.HasOne(t => t.ClaimedByRunner)
            .WithMany()
            .HasForeignKey(t => t.ClaimedBy);
    }
}
