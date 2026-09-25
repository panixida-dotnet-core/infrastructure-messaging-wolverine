using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors.Abstractions;
using PANiXiDA.Core.Application.Messaging.Mediator.Contracts;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;
using PANiXiDA.Core.ResultPattern;

var configuration = WolverineRequestBehaviorConfiguration.CreateModularDefault();
configuration.Before.InsertBefore(typeof(SmokeBehavior<,>), typeof(ValidationBehavior<,>));
var registry = configuration.Build();
if (registry.BeforeMiddlewareTypes[1] != typeof(SmokeBehavior<,>))
{
    throw new InvalidOperationException("Behavior ordering changed.");
}

foreach (var pair in new[] { (typeof(SmokeCommand), typeof(Result)), (typeof(SmokeQuery), typeof(Result<int>)) })
{
    foreach (var stage in new[]
             {
                 (registry.BeforeMiddlewareTypes, typeof(IBeforeRequestBehavior<,>)),
                 (registry.AfterMiddlewareTypes, typeof(IAfterRequestBehavior<,>)),
                 (registry.FinallyMiddlewareTypes, typeof(IFinallyRequestBehavior<,>))
             })
    {
        foreach (var behavior in stage.Item1)
        {
            var commandOnly = behavior == typeof(BeginTransactionBehavior<,>) || behavior == typeof(CommitTransactionBehavior<,>) ||
                behavior == typeof(CleanupTransactionBehavior<,>) || behavior == typeof(FlushOutgoingMessagesBehavior<,>);
            var expected = pair.Item1 == typeof(SmokeCommand) || !commandOnly;
            var resolved = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(behavior, pair.Item1, pair.Item2, stage.Item2, out var middleware);
            if (resolved != expected)
            {
                throw new InvalidOperationException($"Unexpected binding for '{behavior}' and '{pair.Item1}'.");
            }

            if (resolved)
            {
                _ = RequestMiddlewareCodeGeneration.ResolveConstructor(middleware);
            }
        }
    }
}

if (!RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
        typeof(SmokeBehavior<,>), typeof(SmokeCommand), typeof(Result), typeof(IBeforeRequestBehavior<,>), out var closed) ||
    closed != typeof(SmokeBehavior<SmokeCommand, Result>) ||
    !RequestMiddlewareCodeGeneration.ResolveConstructor(closed).SequenceEqual([typeof(SmokeDependency)]))
{
    throw new InvalidOperationException("Generated behavior metadata is missing or incorrect.");
}

var dependency = new SmokeDependency();
var result = await new SmokeBehavior<SmokeCommand, Result>(dependency).BeforeAsync(new SmokeCommand(), CancellationToken.None);
if (!result.IsSuccess || !dependency.Called)
{
    throw new InvalidOperationException("Behavior execution failed.");
}

Console.WriteLine("PASS: generated behavior registration, ordering, generic closure, constructor metadata, and execution.");

/// <summary>Provides an AOT smoke request.</summary>
public sealed record SmokeCommand : ICommand<Result>;

/// <summary>Provides an AOT smoke query with a generic result.</summary>
public sealed record SmokeQuery : IQuery<Result<int>>;

/// <summary>Records AOT smoke behavior execution.</summary>
public sealed class SmokeDependency
{
    /// <summary>Gets or sets whether the behavior executed.</summary>
    public bool Called { get; set; }
}

/// <summary>Exercises a generated generic behavior with a constructor dependency.</summary>
public sealed class SmokeBehavior<TRequest, TResult>(SmokeDependency dependency) : IBeforeRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    /// <inheritdoc />
    public Task<Result> BeforeAsync(TRequest request, CancellationToken cancellationToken)
    {
        dependency.Called = true;
        return Task.FromResult(Result.Success());
    }
}
