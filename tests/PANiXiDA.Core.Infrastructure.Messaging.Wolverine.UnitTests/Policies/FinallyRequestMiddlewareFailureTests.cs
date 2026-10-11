using System.Reflection;
using System.Runtime.Loader;

using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class FinallyRequestMiddlewareFailureTests
{
    private static readonly Lazy<Func<FailureState, Task>> Pipeline = new(() => Compile(
        typeof(FirstFinallyBehavior), typeof(SecondFinallyBehavior)));
    private static readonly Lazy<Func<FailureState, Task>> TransactionPipeline = new(() => Compile(
        typeof(CleanupTransactionBehavior<TestCommand, Result>), typeof(SecondFinallyBehavior)));

    [Theory(DisplayName = "Generated finally middleware preserves all processing and cleanup failures")]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task PipelineShouldPreserveAllFailures(bool failHandler, bool failFirst, bool failSecond)
    {
        // Arrange
        var state = new FailureState
        {
            HandlerException = failHandler ? new InvalidOperationException("Handler failed.") : null,
            FirstException = failFirst ? new InvalidOperationException("First cleanup failed.") : null,
            SecondException = failSecond ? new InvalidOperationException("Second cleanup failed.") : null
        };
        var expected = new[] { state.HandlerException, state.FirstException, state.SecondException }
            .OfType<Exception>().ToArray();

        // Act
        var actual = await Record.ExceptionAsync(() => Pipeline.Value(state));

        // Assert
        state.Calls.ShouldBe(["first", "second"]);
        state.ReceivedExceptions.ShouldBe(new[] { state.HandlerException, state.HandlerException });
        AssertFailures(actual, expected);
        foreach (var exception in expected)
        {
            exception.StackTrace.ShouldNotBeNull();
            exception.StackTrace.ShouldContain(exception == state.HandlerException
                ? nameof(FailureState.HandleAsync)
                : nameof(FailureState.CleanupAsync));
        }
    }

    [Fact(DisplayName = "Generated finally middleware continues after a behavior constructor fails")]
    public async Task PipelineShouldContinueAfterConstructorFailure()
    {
        // Arrange
        var state = new FailureState
        {
            HandlerException = new InvalidOperationException("Handler failed."),
            FirstException = new InvalidOperationException("Constructor failed."),
            SecondException = new InvalidOperationException("Second cleanup failed."),
            FailConstructor = true
        };

        // Act
        var actual = await Record.ExceptionAsync(() => Pipeline.Value(state));

        // Assert
        state.Calls.ShouldBe(["second"]);
        AssertFailures(actual, [state.HandlerException, state.FirstException, state.SecondException]);
    }

    [Theory(DisplayName = "Generated finally middleware preserves a single cancellation and aggregates concurrent failures")]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task PipelineShouldPreserveCancellation(bool cancelHandler, bool failSecond)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var canceled = new OperationCanceledException(cancellation.Token);
        var state = new FailureState
        {
            HandlerException = cancelHandler ? canceled : null,
            FirstException = cancelHandler ? null : canceled,
            SecondException = failSecond ? new InvalidOperationException("Second cleanup failed.") : null
        };

        // Act
        var task = Pipeline.Value(state);
        var actual = await Record.ExceptionAsync(() => task);

        // Assert
        AssertFailures(actual, [.. new[] { canceled, state.SecondException }.OfType<Exception>()]);
        task.IsCanceled.ShouldBe(!failSecond);
        task.IsFaulted.ShouldBe(failSecond);
        canceled.CancellationToken.ShouldBe(cancellation.Token);
        state.Calls.ShouldBe(["first", "second"]);
    }

    [Theory(DisplayName = "Generated finally middleware retains cleanup aggregates without duplicating the original failure")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PipelineShouldRetainAggregatedOriginalFailure(bool aggregateOriginal, bool failSecond)
    {
        // Arrange
        Exception original = new InvalidOperationException("Handler failed.");
        if (aggregateOriginal)
        {
            original = new AggregateException(original);
        }

        var cleanup = new AggregateException(original, new InvalidOperationException("Rollback failed."));
        var state = new FailureState
        {
            HandlerException = original,
            FirstException = cleanup,
            SecondException = failSecond ? new InvalidOperationException("Second cleanup failed.") : null
        };

        // Act
        var actual = await Record.ExceptionAsync(() => Pipeline.Value(state));

        // Assert
        AssertFailures(actual, [.. new[] { cleanup, state.SecondException }.OfType<Exception>()]);
        state.ReceivedExceptions.ShouldBe(new[] { original, original });
    }

    [Fact(DisplayName = "Generated finally middleware preserves an existing aggregate when it is the only failure")]
    public async Task PipelineShouldPreserveSingleAggregate()
    {
        // Arrange
        var aggregate = new AggregateException(new InvalidOperationException("Cleanup failed."));
        var state = new FailureState { FirstException = aggregate };

        // Act
        var actual = await Record.ExceptionAsync(() => Pipeline.Value(state));

        // Assert
        actual.ShouldBeSameAs(aggregate);
    }

    [Theory(DisplayName = "Generated finally middleware retains repeated cleanup failure occurrences")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineShouldRetainRepeatedCleanupFailures(bool failHandler)
    {
        // Arrange
        var cleanup = new InvalidOperationException("Cleanup failed.");
        var state = new FailureState
        {
            HandlerException = failHandler ? new InvalidOperationException("Handler failed.") : null,
            FirstException = cleanup,
            SecondException = cleanup
        };
        var expected = new[] { state.HandlerException, cleanup, cleanup }.OfType<Exception>().ToArray();

        // Act
        var actual = await Record.ExceptionAsync(() => Pipeline.Value(state));

        // Assert
        AssertFailures(actual, expected);
        state.Calls.ShouldBe(["first", "second"]);
    }

    [Theory(DisplayName = "Generated finally middleware does not duplicate an unchanged original exception")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineShouldPreserveRethrownOriginal(bool rethrowAgain)
    {
        // Arrange
        var original = new InvalidOperationException("Handler failed.");
        var state = new FailureState
        {
            HandlerException = original,
            FirstException = original,
            SecondException = rethrowAgain ? original : null
        };

        // Act
        var actual = await Record.ExceptionAsync(() => Pipeline.Value(state));

        // Assert
        actual.ShouldBeSameAs(original);
        original.StackTrace.ShouldNotBeNull();
        original.StackTrace.ShouldContain(nameof(FailureState.HandleAsync));
        original.StackTrace.ShouldNotContain(nameof(FailureState.CleanupAsync));
    }

    [Theory(DisplayName = "Generated finally middleware retains the processing stack when cleanup aggregates a rethrown original")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineShouldPreserveRethrownOriginalStackInAggregate(bool aggregateOriginal)
    {
        // Arrange
        var original = new InvalidOperationException("Handler failed.");
        var second = new InvalidOperationException("Second cleanup failed.");
        var state = new FailureState
        {
            HandlerException = original,
            FirstException = original,
            SecondException = aggregateOriginal ? new AggregateException(original, second) : second
        };

        // Act
        var actual = await Record.ExceptionAsync(() => Pipeline.Value(state));

        // Assert
        actual.ShouldBeOfType<AggregateException>().Flatten().InnerExceptions.ShouldBe([original, second]);
        original.StackTrace.ShouldNotBeNull();
        original.StackTrace.ShouldContain(nameof(FailureState.HandleAsync));
        original.StackTrace.ShouldNotContain(nameof(FailureState.CleanupAsync));
    }

    [Theory(DisplayName = "Generated finally middleware retains Application rollback and disposal failures")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineShouldPreserveTransactionCleanupFailures(bool failSecond)
    {
        // Arrange
        var unitOfWork = new TestUnitOfWork
        {
            HasActiveTransaction = true,
            RollbackException = new InvalidOperationException("Rollback failed."),
            DisposeException = new InvalidOperationException("Dispose failed.")
        };
        var state = new FailureState
        {
            HandlerException = new InvalidOperationException("Handler failed."),
            SecondException = failSecond ? new InvalidOperationException("Second cleanup failed.") : null,
            UnitOfWork = unitOfWork
        };
        var expected = new[] { state.HandlerException, unitOfWork.RollbackException, unitOfWork.DisposeException, state.SecondException }
            .OfType<Exception>().ToArray();

        // Act
        var actual = await Record.ExceptionAsync(() => TransactionPipeline.Value(state));

        // Assert
        actual.ShouldBeOfType<AggregateException>().Flatten().InnerExceptions.ShouldBe(expected, ignoreOrder: true);
        unitOfWork.RollbackTransactionCallCount.ShouldBe(1);
        unitOfWork.DisposeTransactionCallCount.ShouldBe(1);
        state.Calls.ShouldBe(["second"]);
        state.ReceivedExceptions.ShouldBe([state.HandlerException]);
    }

    private static void AssertFailures(Exception? actual, Exception[] expected)
    {
        if (expected.Length == 0)
        {
            actual.ShouldBeNull();
        }
        else if (expected.Length == 1)
        {
            actual.ShouldBeSameAs(expected[0]);
        }
        else
        {
            actual.ShouldBeOfType<AggregateException>().InnerExceptions.ShouldBe(expected);
        }
    }

    private static Func<FailureState, Task> Compile(params Type[] behaviors)
    {
        var frame = FinallyRequestMiddlewareFrame.TryCreate(
            typeof(TestCommand), new Variable(typeof(Result), "result"), behaviors)!;
        _ = frame.FindVariables(new TestMethodVariables()).ToArray();
        using var writer = new SourceWriter();
        frame.Wrap(writer, () => writer.WriteLine("await state.HandleAsync();"));
        var source = $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using PANiXiDA.Core.ResultPattern;
            using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages;
            using static PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies.FinallyRequestMiddlewareFailureTests;

            public static class GeneratedPipeline
            {
                public static async Task ExecuteAsync(FailureState state)
                {
                    var request = new TestCommand(Guid.Empty);
                    var cancellationToken = CancellationToken.None;
                    Result result = null!;
                    {{writer.Code()}}
                }
            }
            """;
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create(
            "FinallyFailures_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        return assembly.GetType("GeneratedPipeline")!.GetMethod("ExecuteAsync")!
            .CreateDelegate<Func<FailureState, Task>>();
    }

    public sealed class FailureState
    {
        public Exception? HandlerException { get; init; }
        public Exception? FirstException { get; init; }
        public Exception? SecondException { get; init; }
        public bool FailConstructor { get; init; }
        public IUnitOfWork UnitOfWork { get; init; } = new TestUnitOfWork();
        public List<string> Calls { get; } = [];
        public List<Exception?> ReceivedExceptions { get; } = [];

        public async Task HandleAsync()
        {
            await Task.Yield();
            if (HandlerException is not null)
            {
                throw HandlerException;
            }
        }

        public async Task CleanupAsync(string name, Exception? original, Exception? failure)
        {
            Calls.Add(name);
            ReceivedExceptions.Add(original);
            await Task.Yield();
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    public sealed class FirstFinallyBehavior : IFinallyRequestBehavior<TestCommand, Result>
    {
        private readonly FailureState state;

        public FirstFinallyBehavior(FailureState state)
        {
            if (state.FailConstructor)
            {
                throw state.FirstException!;
            }

            this.state = state;
        }

        public Task FinallyAsync(TestCommand request, Result? result, Exception? exception, CancellationToken cancellationToken)
        {
            return state.CleanupAsync("first", exception, state.FirstException);
        }
    }

    public sealed class SecondFinallyBehavior(FailureState state) : IFinallyRequestBehavior<TestCommand, Result>
    {
        public Task FinallyAsync(TestCommand request, Result? result, Exception? exception, CancellationToken cancellationToken)
        {
            return state.CleanupAsync("second", exception, state.SecondException);
        }
    }

    private sealed class TestMethodVariables : IMethodVariables
    {
        public Variable FindVariable(Type type)
        {
            if (type == typeof(TestCommand))
            {
                return new Variable(type, "request");
            }

            if (type == typeof(CancellationToken))
            {
                return new Variable(type, "cancellationToken");
            }

            return new Variable(type, type == typeof(IUnitOfWork) ? "state.UnitOfWork" : "state");
        }

        public Variable FindVariable(ParameterInfo parameter) => FindVariable(parameter.ParameterType);
        public Variable FindVariableByName(Type dependency, string name) => FindVariable(dependency);
        public Variable TryFindVariable(Type type, VariableSource source) => FindVariable(type);

        public bool TryFindVariableByName(Type dependency, string name, out Variable variable)
        {
            variable = FindVariable(dependency);
            return true;
        }
    }
}
