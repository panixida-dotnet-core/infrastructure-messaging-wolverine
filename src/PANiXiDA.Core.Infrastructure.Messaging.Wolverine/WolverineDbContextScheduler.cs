using Microsoft.EntityFrameworkCore;

using Wolverine.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine;

/// <summary>
/// Schedules messages through the transactional outbox of a specific DbContext.
/// </summary>
/// <typeparam name="TDbContext">The DbContext that owns the scheduling transaction.</typeparam>
/// <param name="outbox">The scoped EF Core outbox.</param>
public sealed class WolverineDbContextScheduler<TDbContext>(IDbContextOutbox<TDbContext> outbox)
    : WolverineScheduler(outbox)
    where TDbContext : DbContext;
