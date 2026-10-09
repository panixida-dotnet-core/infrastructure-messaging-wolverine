using Microsoft.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

public sealed class SecondTestDbContext(
    DbContextOptions<SecondTestDbContext> options) : DbContext(options);
