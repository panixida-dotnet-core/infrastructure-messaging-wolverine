using Microsoft.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.OutboxDispatcher;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
