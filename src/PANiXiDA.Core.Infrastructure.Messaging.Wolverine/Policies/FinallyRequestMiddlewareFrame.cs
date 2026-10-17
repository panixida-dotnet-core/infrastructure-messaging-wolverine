using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class FinallyRequestMiddlewareFrame : RequestMiddlewareFrameBase
{
    private readonly Variable resultVariable;
    private readonly string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

    private FinallyRequestMiddlewareFrame(
        Type requestType,
        Variable resultVariable,
        RequestMiddlewareDescriptor[] descriptors) : base(
        requestType,
        descriptors)
    {
        this.resultVariable = resultVariable;
    }

    internal static FinallyRequestMiddlewareFrame? TryCreate(
        Type requestType,
        Variable resultVariable,
        IReadOnlyList<Type> middlewareTypes)
    {
        var descriptors = RequestMiddlewareDescriptor.Resolve(
            requestType,
            resultVariable.VariableType,
            typeof(IFinallyRequestBehavior<,>),
            middlewareTypes);

        if (descriptors.Length == 0)
        {
            return null;
        }

        return new FinallyRequestMiddlewareFrame(
            requestType,
            resultVariable,
            descriptors);
    }

    public override void GenerateCode(
        GeneratedMethod method,
        ISourceWriter writer)
    {
        Wrap(writer, () => Next?.GenerateCode(method, writer));
    }

    internal void Wrap(ISourceWriter writer, Action generateBody)
    {
        var exceptionLocalName = $"__finallyException_{uniqueSuffix}";
        var exceptionDispatchName = $"__finallyExceptionDispatch_{uniqueSuffix}";
        var cleanupExceptionsName = $"__cleanupExceptions_{uniqueSuffix}";
        var exceptionHandledName = $"__exceptionHandled_{uniqueSuffix}";
        var cleanupFailedName = $"__cleanupFailed_{uniqueSuffix}";

        writer.WriteLine(string.Empty);
        writer.WriteLine(
            $"global::System.Exception? {exceptionLocalName} = null;");
        writer.WriteLine(
            $"global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? {exceptionDispatchName} = null;");

        writer.Write("BLOCK:try");
        generateBody();
        writer.FinishBlock();

        writer.Write("BLOCK:catch (global::System.Exception ex)");
        writer.WriteLine($"{exceptionLocalName} = ex;");
        writer.WriteLine($"{exceptionDispatchName} = global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);");
        writer.FinishBlock();

        writer.Write("BLOCK:finally");
        writer.WriteLine($"global::System.Collections.Generic.List<global::System.Exception>? {cleanupExceptionsName} = null;");
        writer.WriteLine($"bool {exceptionHandledName} = false;");
        writer.WriteLine($"bool {cleanupFailedName} = false;");
        foreach (var middleware in middlewareDescriptors)
        {
            writer.Write("BLOCK:try");
            WriteInvocation(writer, middleware, resultVariable.Usage, exceptionLocalName, exceptionHandledName);
            writer.FinishBlock();

            var cleanupExceptionName = $"__cleanupException_{uniqueSuffix}";
            writer.Write($"BLOCK:catch (global::System.Exception {cleanupExceptionName})");
            writer.WriteLine($"{cleanupFailedName} = true;");
            writer.Write($"BLOCK:if (!global::System.Object.ReferenceEquals({cleanupExceptionName}, {exceptionLocalName}))");
            writer.WriteLine($"{cleanupExceptionsName} ??= new();");
            writer.WriteLine($"{cleanupExceptionsName}.Add({cleanupExceptionName});");
            writer.FinishBlock();
            writer.FinishBlock();
        }

        WriteRethrow(writer, exceptionLocalName, exceptionDispatchName, cleanupExceptionsName, exceptionHandledName, cleanupFailedName);
        writer.FinishBlock();
    }

    private void WriteRethrow(
        ISourceWriter writer,
        string exceptionLocalName,
        string exceptionDispatchName,
        string cleanupExceptionsName,
        string exceptionHandledName,
        string cleanupFailedName)
    {
        writer.Write("BLOCK:try");
        writer.WriteComment("Restore the processing stack before propagating or aggregating cleanup failures.");
        writer.Write($"BLOCK:if (!{exceptionHandledName} || {cleanupFailedName})");
        writer.WriteLine($"{exceptionDispatchName}?.Throw();");
        writer.FinishBlock();
        writer.FinishBlock();

        writer.Write("BLOCK:finally");
        var containsOriginalName = $"__containsOriginal_{uniqueSuffix}";
        writer.Write($"BLOCK:if ({cleanupExceptionsName} is not null)");
        writer.Write($"BLOCK:if ({exceptionLocalName} is not null)");
        writer.WriteComment("Cleanup may already include the original exception in an aggregate.");
        writer.WriteLine($"bool {containsOriginalName}(global::System.Exception candidate) =>");
        writer.WriteLine($"    global::System.Object.ReferenceEquals(candidate, {exceptionLocalName}) ||");
        writer.WriteLine("    candidate is global::System.AggregateException aggregate &&");
        writer.WriteLine($"    global::System.Linq.Enumerable.Any(aggregate.InnerExceptions, {containsOriginalName});");
        writer.Write($"BLOCK:if (!global::System.Linq.Enumerable.Any({cleanupExceptionsName}, {containsOriginalName}))");
        writer.WriteLine($"{cleanupExceptionsName}.Insert(0, {exceptionLocalName});");
        writer.FinishBlock();
        writer.FinishBlock();

        writer.Write($"BLOCK:if ({cleanupExceptionsName}.Count > 1)");
        writer.WriteLine($"throw new global::System.AggregateException(\"Multiple errors occurred while completing the request.\", {cleanupExceptionsName});");
        writer.FinishBlock();
        writer.WriteLine($"global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw({cleanupExceptionsName}[0]);");
        writer.FinishBlock();
        writer.FinishBlock();
    }

    private void WriteInvocation(
        ISourceWriter writer,
        RequestMiddlewareDescriptor middleware,
        string resultLocalName,
        string exceptionLocalName,
        string exceptionHandledName)
    {
        var middlewareVariableName =
            RequestMiddlewareCodeGeneration.BuildVariableName(
                middleware.Type,
                middleware.UniqueSuffix);
        var middlewareTypeName =
            RequestMiddlewareCodeGeneration.GetCodeTypeName(middleware.Type);
        var returnedResultName = $"__finallyResult_{middleware.UniqueSuffix}";

        writer.WriteLine(
            $"var {middlewareVariableName} = new {middlewareTypeName}({middleware.ConstructorArguments});");
        writer.WriteLine(
            $"var {returnedResultName} = await {middlewareVariableName}.{nameof(IFinallyRequestBehavior<,>.FinallyAsync)}({requestVariable.Usage}, {resultLocalName}, {exceptionLocalName}, {cancellationVariable.Usage}).ConfigureAwait(false);");
        writer.Write($"BLOCK:if (!global::System.Object.ReferenceEquals({returnedResultName}, {resultLocalName}))");
        writer.Write($"BLOCK:if ({returnedResultName} is null || {resultLocalName} is {{ IsFailure: true }})");
        writer.WriteLine("throw new global::System.InvalidOperationException(\"Finally behaviors must preserve existing failure results and must not discard an existing result.\");");
        writer.FinishBlock();

        WriteResultAssignment(writer, returnedResultName, resultLocalName);
        writer.Write($"BLOCK:if ({exceptionLocalName} is not null && {returnedResultName}.IsFailure)");
        writer.WriteLine($"{exceptionHandledName} = true;");
        writer.FinishBlock();
        writer.FinishBlock();
    }

    private void WriteResultAssignment(ISourceWriter writer, string returnedResultName, string resultLocalName)
    {
        if (resultVariable.VariableType == typeof(Result))
        {
            writer.WriteLine($"{resultLocalName} = {returnedResultName};");
            return;
        }

        var resultTypeName = RequestMiddlewareCodeGeneration.GetCodeTypeName(resultVariable.VariableType);
        var typedResultName = $"__typedFinallyResult_{uniqueSuffix}";
        writer.Write($"BLOCK:if ({returnedResultName} is {resultTypeName} {typedResultName})");
        writer.WriteLine($"{resultLocalName} = {typedResultName};");
        writer.FinishBlock();
        writer.Write($"BLOCK:else if ({returnedResultName}.IsFailure)");
        writer.WriteLine($"{resultLocalName} = {RequestMiddlewareCodeGeneration.BuildFailureResultCode(resultVariable.VariableType, returnedResultName)};");
        writer.FinishBlock();
        writer.Write("BLOCK:else");
        writer.WriteLine("throw new global::System.InvalidOperationException(\"A successful finally result must match the declared request result type.\");");
        writer.FinishBlock();
    }
}
