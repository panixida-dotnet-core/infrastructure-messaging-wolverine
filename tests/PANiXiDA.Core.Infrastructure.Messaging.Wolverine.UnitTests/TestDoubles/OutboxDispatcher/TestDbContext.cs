using Microsoft.EntityFrameworkCore;

using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.OutboxDispatcher;

[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Minimal EF Core DbContext type used to close generic outbox dispatcher tests.")]
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
