using Microsoft.EntityFrameworkCore;

using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Minimal EF Core DbContext type used to verify dependency injection registrations.")]
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
