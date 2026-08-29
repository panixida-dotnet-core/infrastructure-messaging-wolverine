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
        var resultTypeName =
            RequestMiddlewareCodeGeneration.GetCodeTypeName(
                resultVariable.VariableType);
        var resultLocalName = $"__finallyResult_{uniqueSuffix}";
        var exceptionLocalName = $"__finallyException_{uniqueSuffix}";

        writer.WriteLine(string.Empty);
        writer.WriteComment("Wrap handler execution with finally middleware");
        writer.WriteLine(
            $"{resultTypeName} {resultLocalName} = default!;");
        writer.WriteLine(
            $"global::System.Exception? {exceptionLocalName} = null;");

        writer.Write("BLOCK:try");
        GenerateNextFrame(method, writer);
        writer.WriteLine($"{resultLocalName} = {resultVariable.Usage};");
        writer.FinishBlock();

        writer.Write("BLOCK:catch (global::System.Exception ex)");
        writer.WriteLine($"{exceptionLocalName} = ex;");
        writer.WriteLine("throw;");
        writer.FinishBlock();

        writer.Write("BLOCK:finally");
        WriteMiddleware(
            writer,
            resultLocalName,
            exceptionLocalName,
            index: 0);
        writer.FinishBlock();
    }

    private void WriteMiddleware(
        ISourceWriter writer,
        string resultLocalName,
        string exceptionLocalName,
        int index)
    {
        var middleware = middlewareDescriptors[index];

        if (index < middlewareDescriptors.Count - 1)
        {
            writer.Write("BLOCK:try");
        }

        WriteInvocation(
            writer,
            middleware,
            resultLocalName,
            exceptionLocalName);

        if (index >= middlewareDescriptors.Count - 1)
        {
            return;
        }

        writer.FinishBlock();
        writer.Write("BLOCK:finally");
        WriteMiddleware(
            writer,
            resultLocalName,
            exceptionLocalName,
            index + 1);
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
