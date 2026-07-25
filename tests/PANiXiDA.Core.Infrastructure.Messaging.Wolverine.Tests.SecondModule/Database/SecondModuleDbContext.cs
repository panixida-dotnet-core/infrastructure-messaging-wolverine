using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database.Entities;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database;

public sealed class SecondModuleDbContext(
    DbContextOptions<SecondModuleDbContext> options) : DbContext(options)
{
    public DbSet<SecondModuleRecord> Records => Set<SecondModuleRecord>();

    public DbSet<SecondModuleHandledEvent> HandledEvents =>
        Set<SecondModuleHandledEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("second_module");

        modelBuilder.Entity<SecondModuleRecord>(entity =>
        {
            entity.ToTable("records");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).IsRequired();
        });

        modelBuilder.Entity<SecondModuleHandledEvent>(entity =>
        {
            entity.ToTable("handled_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EventId).IsRequired();
            entity.Property(item => item.EventType).IsRequired();
        });
    }
}
