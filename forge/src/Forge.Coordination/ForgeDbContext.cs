using Forge.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Forge.Coordination;

public class ForgeDbContext : DbContext
{
    public DbSet<ForgeRunner> Runners => Set<ForgeRunner>();
    public DbSet<ForgeTask> Tasks => Set<ForgeTask>();
    public DbSet<ForgeRun> Runs => Set<ForgeRun>();

    public ForgeDbContext(DbContextOptions<ForgeDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<RunnerStatus>("runner_status");
        modelBuilder.HasPostgresEnum<ForgeTaskStatus>("task_status");
        modelBuilder.HasPostgresEnum<RunType>("run_type");

        modelBuilder.ApplyConfiguration(new EntityConfigurations.RunnerConfiguration());
        modelBuilder.ApplyConfiguration(new EntityConfigurations.TaskConfiguration());
        modelBuilder.ApplyConfiguration(new EntityConfigurations.RunConfiguration());
    }
}
