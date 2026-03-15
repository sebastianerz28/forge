using Forge.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Coordination.EntityConfigurations;

public class RunnerConfiguration : IEntityTypeConfiguration<ForgeRunner>
{
    public void Configure(EntityTypeBuilder<ForgeRunner> builder)
    {
        builder.ToTable("runners");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.Hostname).HasColumnName("hostname").IsRequired();
        builder.Property(r => r.Name).HasColumnName("name").HasDefaultValue("");
        builder.Property(r => r.LastHeartbeat).HasColumnName("last_heartbeat").HasColumnType("timestamp with time zone");
        builder.Property(r => r.RegisteredAt).HasColumnName("registered_at").HasColumnType("timestamp with time zone");
        builder.Property(r => r.Status).HasColumnName("status");
    }
}
