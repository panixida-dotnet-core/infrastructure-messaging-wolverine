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
        var cleanupExceptionsName = $"__cleanupExceptions_{uniqueSuffix}";

        writer.WriteLine(string.Empty);
        writer.WriteLine(
            $"global::System.Exception? {exceptionLocalName} = null;");

        writer.Write("BLOCK:try");
        generateBody();
        writer.FinishBlock();

        writer.Write("BLOCK:catch (global::System.Exception ex)");
        writer.WriteLine($"{exceptionLocalName} = ex;");
        writer.WriteLine("throw;");
        writer.FinishBlock();

        writer.Write("BLOCK:finally");
        writer.WriteLine($"global::System.Collections.Generic.List<global::System.Exception>? {cleanupExceptionsName} = null;");
        foreach (var middleware in middlewareDescriptors)
        {
            writer.Write("BLOCK:try");
            WriteInvocation(writer, middleware, resultVariable.Usage, exceptionLocalName);
            writer.FinishBlock();

            var cleanupExceptionName = $"__cleanupException_{uniqueSuffix}";
            writer.Write($"BLOCK:catch (global::System.Exception {cleanupExceptionName})");
            writer.WriteLine($"{cleanupExceptionsName} ??= new();");
            writer.Write($"BLOCK:if (!global::System.Linq.Enumerable.Any({cleanupExceptionsName}, existing => global::System.Object.ReferenceEquals(existing, {cleanupExceptionName})))");
            writer.WriteLine($"{cleanupExceptionsName}.Add({cleanupExceptionName});");
            writer.FinishBlock();
            writer.FinishBlock();
        }

        WriteRethrow(writer, exceptionLocalName, cleanupExceptionsName);
        writer.FinishBlock();
    }

    private void WriteRethrow(
        ISourceWriter writer,
        string exceptionLocalName,
        string cleanupExceptionsName)
    {
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
    }

    private void WriteInvocation(
        ISourceWriter writer,
        RequestMiddlewareDescriptor middleware,
        string resultLocalName,
        string exceptionLocalName)
    {
        var middlewareVariableName =
            RequestMiddlewareCodeGeneration.BuildVariableName(
                middleware.Type,
                middleware.UniqueSuffix);
        var middlewareTypeName =
            RequestMiddlewareCodeGeneration.GetCodeTypeName(middleware.Type);

        writer.WriteLine(
            $"var {middlewareVariableName} = new {middlewareTypeName}({middleware.ConstructorArguments});");
        writer.WriteLine(
            $"await {middlewareVariableName}.{nameof(IFinallyRequestBehavior<,>.FinallyAsync)}({requestVariable.Usage}, {resultLocalName}, {exceptionLocalName}, {cancellationVariable.Usage}).ConfigureAwait(false);");
    }
}
