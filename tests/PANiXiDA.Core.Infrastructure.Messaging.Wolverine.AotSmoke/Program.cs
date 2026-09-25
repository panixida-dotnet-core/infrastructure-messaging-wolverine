using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors.Abstractions;
using PANiXiDA.Core.Application.Messaging.Mediator.Contracts;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;
using PANiXiDA.Core.ResultPattern;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.AotSmoke;

internal static class Program
{
    private static async Task Main()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateModularDefault();
        configuration.Before.InsertBefore(typeof(SmokeBehavior<,>), typeof(ValidationBehavior<,>));
        var registry = configuration.Build();
        if (registry.BeforeMiddlewareTypes[1] != typeof(SmokeBehavior<,>))
        {
            throw new InvalidOperationException("Behavior ordering changed.");
        }

        foreach (var pair in new[] { (typeof(SmokeCommand), typeof(Result)), (typeof(SmokeQuery), typeof(Result<int>)) })
        {
            VerifyStage(registry.BeforeMiddlewareTypes, pair.Item1, pair.Item2, typeof(IBeforeRequestBehavior<,>));
            VerifyStage(registry.AfterMiddlewareTypes, pair.Item1, pair.Item2, typeof(IAfterRequestBehavior<,>));
            VerifyStage(registry.FinallyMiddlewareTypes, pair.Item1, pair.Item2, typeof(IFinallyRequestBehavior<,>));
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
    }

    private static void VerifyStage(IReadOnlyList<Type> behaviors, Type requestType, Type resultType, Type contractType)
    {
        foreach (var behavior in behaviors)
        {
            var commandOnly = behavior == typeof(BeginTransactionBehavior<,>) || behavior == typeof(CommitTransactionBehavior<,>) ||
                behavior == typeof(CleanupTransactionBehavior<,>) || behavior == typeof(FlushOutgoingMessagesBehavior<,>);
            var expected = requestType == typeof(SmokeCommand) || !commandOnly;
            var resolved = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(behavior, requestType, resultType, contractType, out var middleware);
            if (resolved != expected)
            {
                throw new InvalidOperationException($"Unexpected binding for '{behavior}' and '{requestType}'.");
            }

            if (resolved)
            {
                _ = RequestMiddlewareCodeGeneration.ResolveConstructor(middleware);
            }
        }
    }
}

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
