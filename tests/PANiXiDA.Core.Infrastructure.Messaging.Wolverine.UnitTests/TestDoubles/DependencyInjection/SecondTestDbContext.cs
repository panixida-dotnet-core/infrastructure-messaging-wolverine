using Microsoft.EntityFrameworkCore;

using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Minimal second EF Core DbContext type used to verify modular registrations.")]
public sealed class SecondTestDbContext(
    DbContextOptions<SecondTestDbContext> options) : DbContext(options);
