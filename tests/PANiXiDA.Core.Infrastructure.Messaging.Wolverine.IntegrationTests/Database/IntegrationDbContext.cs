using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;

public sealed class IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : DbContext(options)
{
    public DbSet<IntegrationRecord> Records => Set<IntegrationRecord>();

    public DbSet<HandledEventRecord> HandledEvents => Set<HandledEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationRecord>(entity =>
        {
            entity.ToTable("integration_records");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired();
        });

        modelBuilder.Entity<HandledEventRecord>(entity =>
        {
            entity.ToTable("handled_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventId).IsRequired();
            entity.Property(x => x.Name).IsRequired();
        });
    }
}
