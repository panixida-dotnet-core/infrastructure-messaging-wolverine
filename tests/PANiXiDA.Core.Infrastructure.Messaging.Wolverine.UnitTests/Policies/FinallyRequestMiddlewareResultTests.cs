using System.Reflection;
using System.Runtime.Loader;

using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Application.Persistence.Errors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class FinallyRequestMiddlewareResultTests
{
    private static readonly Lazy<Func<ResultState, Task<Result?>>> CommandPipeline = new(() => Compile(false, false));
    private static readonly Lazy<Func<ResultState, Task<Result?>>> QueryPipeline = new(() => Compile(true, false));
    private static readonly Lazy<Func<ResultState, Task<Result?>>> ConflictFirstPipeline = new(() => Compile(false, true));

    [Theory(DisplayName = "Generated finally middleware converts conflicts and forwards the declared result type")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PipelineShouldConvertConflict(bool typed, bool hasResult)
    {
        var state = new ResultState
        {
            InitialResult = hasResult ? Success(typed) : null,
            Exception = new ConcurrencyException("Private database details")
        };

        var result = await GetPipeline(typed)(state);

        result.ShouldNotBeNull().IsFailure.ShouldBeTrue();
        result.Errors.Single().Type.ShouldBe(ErrorType.Conflict);
        result.Errors.Single().Message.ShouldNotContain("Private database details");
        (result is Result<TestQueryView>).ShouldBe(typed);
        state.SeenResults[1].ShouldBeSameAs(result);
        state.SeenExceptions.ShouldBe([state.Exception, state.Exception]);
    }

    [Theory(DisplayName = "Generated finally middleware preserves successful values and existing failures")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PipelineShouldPreserveExistingResult(bool typed, bool failed)
    {
        var original = failed ? Failure(typed) : Success(typed);
        var state = new ResultState { InitialResult = original };

        var result = await GetPipeline(typed)(state);

        result.ShouldBeSameAs(original);
        state.SeenResults.ShouldBe([original, original]);
    }

    [Theory(DisplayName = "Passing an existing failed result through finally does not handle a processing exception")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineShouldNotHandleExceptionWithExistingFailure(bool typed)
    {
        var original = Failure(typed);
        var state = new ResultState { InitialResult = original, Exception = new ConcurrencyException() };

        var exception = await Record.ExceptionAsync(() => GetPipeline(typed)(state));

        exception.ShouldBeSameAs(state.Exception);
        state.SeenResults.ShouldBe([original, original]);
    }

    [Theory(DisplayName = "Cleanup failures prevent conflict handling regardless of behavior order")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PipelineShouldRetainCleanupFailures(bool conflictFirst, bool rethrowOriginal)
    {
        var original = new ConcurrencyException();
        Exception cleanup = rethrowOriginal ? original : new InvalidOperationException("Cleanup failed");
        var state = new ResultState { Exception = original, CleanupException = cleanup };

        var exception = await Record.ExceptionAsync(() => (conflictFirst ? ConflictFirstPipeline.Value : CommandPipeline.Value)(state));

        if (rethrowOriginal)
        {
            exception.ShouldBeSameAs(original);
            original.StackTrace.ShouldNotBeNull().ShouldContain(nameof(ResultState.HandleAsync));
        }
        else
        {
            exception.ShouldBeOfType<AggregateException>().InnerExceptions.ShouldBe([original, cleanup]);
        }
        state.SeenResults.Count.ShouldBe(2);
        state.SeenResults[1].ShouldNotBeNull().IsFailure.ShouldBeTrue();
        state.SeenExceptions.ShouldBe([original, original]);
    }

    [Fact(DisplayName = "A concurrency exception thrown by cleanup is not handled as a processing conflict")]
    public async Task PipelineShouldNotHandleCleanupConflict()
    {
        var state = new ResultState { InitialResult = Result.Success(), CleanupException = new ConcurrencyException() };

        var exception = await Record.ExceptionAsync(() => CommandPipeline.Value(state));

        exception.ShouldBeSameAs(state.CleanupException);
        state.SeenResults.ShouldBe([state.InitialResult, state.InitialResult]);
    }

    [Theory(DisplayName = "Generated finally middleware propagates unrelated and wrapped exceptions")]
    [InlineData("unknown")]
    [InlineData("canceled")]
    [InlineData("wrapped")]
    [InlineData("aggregate")]
    public async Task PipelineShouldPreserveOtherExceptions(string kind)
    {
        Exception original = kind switch
        {
            "canceled" => new OperationCanceledException(),
            "wrapped" => new InvalidOperationException("Wrapper", new ConcurrencyException()),
            "aggregate" => new AggregateException(new ConcurrencyException()),
            _ => new InvalidOperationException("Unknown")
        };
        var state = new ResultState { Exception = original };

        var exception = await Record.ExceptionAsync(() => CommandPipeline.Value(state));

        exception.ShouldBeSameAs(original);
        state.SeenResults.ShouldBe([null, null]);
    }

    [Theory(DisplayName = "Generated finally middleware accepts assignable replacements and converts untyped failures")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PipelineShouldAcceptReplacement(bool failed, bool typedReplacement)
    {
        var replacement = failed ? Failure(typedReplacement) : Success(true);
        var state = new ResultState { Replace = true, Replacement = replacement };

        var result = await QueryPipeline.Value(state);

        var typed = result.ShouldBeOfType<Result<TestQueryView>>();
        typed.IsFailure.ShouldBe(failed);
        if (failed && !typedReplacement)
        {
            typed.Errors.ShouldBe(replacement.Errors);
        }
        else
        {
            typed.ShouldBeSameAs(replacement);
        }
        state.SeenResults[1].ShouldBeSameAs(result);
    }

    [Theory(DisplayName = "Invalid finally result replacements fail without skipping later cleanup")]
    [InlineData("discard")]
    [InlineData("replaceFailure")]
    [InlineData("untypedSuccess")]
    [InlineData("wrongSuccessType")]
    public async Task PipelineShouldRejectInvalidReplacement(string kind)
    {
        var initial = kind == "replaceFailure" ? Failure(true) : Success(true);
        var replacement = kind switch
        {
            "discard" => null,
            "replaceFailure" => Failure(true),
            "untypedSuccess" => Result.Success(),
            _ => Result.Success(42)
        };
        var state = new ResultState { InitialResult = initial, Replace = true, Replacement = replacement };

        var exception = await Record.ExceptionAsync(() => QueryPipeline.Value(state));

        exception.ShouldBeOfType<InvalidOperationException>();
        state.SeenResults.ShouldBe([initial, initial]);
    }

    [Fact(DisplayName = "A successful replacement never handles a processing exception")]
    public async Task PipelineShouldNotHandleExceptionWithSuccess()
    {
        var state = new ResultState { Exception = new InvalidOperationException(), Replace = true, Replacement = Success(true) };

        var exception = await Record.ExceptionAsync(() => QueryPipeline.Value(state));

        exception.ShouldBeSameAs(state.Exception);
        state.SeenResults[1].ShouldBeSameAs(state.Replacement);
    }

    private static Func<ResultState, Task<Result?>> GetPipeline(bool typed) => typed ? QueryPipeline.Value : CommandPipeline.Value;
    private static Result Success(bool typed) => typed ? Result.Success(new TestQueryView(Guid.NewGuid())) : Result.Success();
    private static Result Failure(bool typed) => typed ? Result.Failure<TestQueryView>(Error.Failure("Existing")) : Result.Failure(Error.Failure("Existing"));

    private static Func<ResultState, Task<Result?>> Compile(bool typed, bool conflictFirst)
    {
        Type requestType = typed ? typeof(TestQuery) : typeof(TestCommand);
        Type resultType = typed ? typeof(Result<TestQueryView>) : typeof(Result);
        Type first = typed ? typeof(ReplaceBehavior<TestQuery, Result<TestQueryView>>) : typeof(ReplaceBehavior<TestCommand, Result>);
        Type last = typed ? typeof(ObserveBehavior<TestQuery, Result<TestQueryView>>) : typeof(ObserveBehavior<TestCommand, Result>);
        Type conflict = typed ? typeof(ConcurrencyExceptionBehavior<TestQuery, Result<TestQueryView>>) : typeof(ConcurrencyExceptionBehavior<TestCommand, Result>);
        Type handlerType = typed ? typeof(TestRequestHandler<TestQuery, Result<TestQueryView>>) : typeof(TestRequestHandler<TestCommand, Result>);
        var frame = FinallyRequestMiddlewareFrame.TryCreate(requestType, new Variable(resultType, "result"),
            handlerType,
            conflictFirst ? [conflict, first, last] : [first, conflict, last])!;
        _ = frame.FindVariables(new MethodVariables(requestType)).ToArray();
        using var writer = new SourceWriter();
        frame.Wrap(writer, () => writer.WriteLine("await state.HandleAsync();"));
        var resultTypeName = RequestMiddlewareCodeGeneration.GetCodeTypeName(resultType);
        var source = $$"""
            #nullable enable
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using PANiXiDA.Core.ResultPattern;
            using static PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies.FinallyRequestMiddlewareResultTests;
            public static class GeneratedResultPipeline
            {
                public static async Task<Result?> ExecuteAsync(ResultState state)
                {
                    var request = new {{RequestMiddlewareCodeGeneration.GetCodeTypeName(requestType)}}(Guid.Empty);
                    var cancellationToken = CancellationToken.None;
                    {{resultTypeName}} result = ({{resultTypeName}})state.InitialResult!;
                    {{writer.Code()}}
                    return result;
                }
            }
            """;
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("FinallyResults_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream).GetType("GeneratedResultPipeline")!
            .GetMethod("ExecuteAsync")!.CreateDelegate<Func<ResultState, Task<Result?>>>();
    }

    public sealed class ResultState
    {
        public Result? InitialResult { get; init; }
        public Exception? Exception { get; init; }
        public Exception? CleanupException { get; init; }
        public bool Replace { get; init; }
        public Result? Replacement { get; init; }
        public List<Result?> SeenResults { get; } = [];
        public List<Exception?> SeenExceptions { get; } = [];

        public async Task HandleAsync()
        {
            await Task.Yield();
            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }

    public sealed class ReplaceBehavior<TRequest, TResult>(ResultState state) : IFinallyRequestBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult> where TResult : Result
    {
        public Task<Result?> FinallyAsync(TRequest request, TResult? result, Exception? exception, CancellationToken cancellationToken)
        {
            state.SeenResults.Add(result);
            state.SeenExceptions.Add(exception);
            if (state.CleanupException is not null)
            {
                throw state.CleanupException;
            }
            return Task.FromResult(state.Replace ? state.Replacement : result);
        }
    }

    public sealed class ObserveBehavior<TRequest, TResult>(ResultState state) : IFinallyRequestBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult> where TResult : Result
    {
        public Task<Result?> FinallyAsync(TRequest request, TResult? result, Exception? exception, CancellationToken cancellationToken)
        {
            state.SeenResults.Add(result);
            state.SeenExceptions.Add(exception);
            return Task.FromResult<Result?>(result);
        }
    }

    private sealed class MethodVariables(Type requestType) : IMethodVariables
    {
        public Variable FindVariable(Type type) => new(type, type == requestType ? "request" : type == typeof(CancellationToken) ? "cancellationToken" : "state");
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
