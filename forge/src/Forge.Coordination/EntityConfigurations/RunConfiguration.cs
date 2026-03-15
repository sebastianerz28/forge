using Forge.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Coordination.EntityConfigurations;

public class RunConfiguration : IEntityTypeConfiguration<ForgeRun>
{
    public void Configure(EntityTypeBuilder<ForgeRun> builder)
    {
        builder.ToTable("runs");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").UseSerialColumn();
        builder.Property(r => r.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(r => r.RunnerId).HasColumnName("runner_id").IsRequired();
        builder.Property(r => r.RunType).HasColumnName("run_type");
        builder.Property(r => r.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone");
        builder.Property(r => r.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamp with time zone");
        builder.Property(r => r.DurationSeconds).HasColumnName("duration_seconds");
        builder.Property(r => r.TokenUsage).HasColumnName("token_usage").HasColumnType("jsonb");
        builder.Property(r => r.Success).HasColumnName("success");
        builder.Property(r => r.ErrorMessage).HasColumnName("error_message");
        builder.Property(r => r.PromptHash).HasColumnName("prompt_hash");
        builder.Property(r => r.TargetRepo).HasColumnName("target_repo");

        builder.HasIndex(r => r.TaskId);
        builder.HasIndex(r => r.RunnerId);
        builder.HasIndex(r => r.StartedAt);

        builder.HasOne(r => r.Task)
            .WithMany()
            .HasForeignKey(r => r.TaskId);

        builder.HasOne(r => r.Runner)
            .WithMany()
            .HasForeignKey(r => r.RunnerId);
    }
}
