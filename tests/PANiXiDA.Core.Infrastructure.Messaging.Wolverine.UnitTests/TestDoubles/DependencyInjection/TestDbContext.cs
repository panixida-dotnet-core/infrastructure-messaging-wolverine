using Microsoft.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
