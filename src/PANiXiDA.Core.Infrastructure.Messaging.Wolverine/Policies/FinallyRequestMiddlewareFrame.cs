using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class FinallyRequestMiddlewareFrame(
    Type requestType,
    Variable resultVariable,
    Type closedMiddlewareType) : RequestMiddlewareFrameBase(requestType, closedMiddlewareType)
{
    private readonly Type resultType = resultVariable.VariableType;

    internal static FinallyRequestMiddlewareFrame? TryCreate(
        Type requestType,
        Variable resultVariable,
        Type middlewareType)
    {
        var resultType = resultVariable.VariableType;

        if (!RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
                middlewareType,
                requestType,
                resultType,
                typeof(IFinallyRequestBehavior<,>),
                out var closedMiddlewareType))
        {
            return null;
        }

        return new FinallyRequestMiddlewareFrame(
            requestType,
            resultVariable,
            closedMiddlewareType);
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        var middlewareVariableName = BuildMiddlewareVariableName();
        var constructorArguments = GetConstructorArguments();
        var middlewareTypeName = GetMiddlewareTypeName();
        var resultTypeName = RequestMiddlewareCodeGeneration.GetCodeTypeName(resultType);

        var resultLocalName = $"__finallyResult_{uniqueSuffix}";
        var exceptionLocalName = $"__finallyException_{uniqueSuffix}";

        writer.WriteLine(string.Empty);
        writer.WriteComment($"Wrap handler execution with {GetFriendlyMiddlewareTypeName()} finally middleware");

        writer.WriteLine($"{resultTypeName} {resultLocalName} = default!;");
        writer.WriteLine($"global::System.Exception? {exceptionLocalName} = null;");

        writer.Write("BLOCK:try");
        Next?.GenerateCode(method, writer);
        writer.WriteLine($"{resultLocalName} = {resultVariable.Usage};");
        writer.FinishBlock();

        writer.Write("BLOCK:catch (global::System.Exception ex)");
        writer.WriteLine($"{exceptionLocalName} = ex;");
        writer.WriteLine("throw;");
        writer.FinishBlock();

        writer.Write("BLOCK:finally");
        writer.WriteLine($"var {middlewareVariableName} = new {middlewareTypeName}({constructorArguments});");
        writer.WriteLine(
            $"await {middlewareVariableName}.{nameof(IFinallyRequestBehavior<,>.FinallyAsync)}({requestVariable.Usage}, {resultLocalName}, {exceptionLocalName}, {cancellationVariable.Usage}).ConfigureAwait(false);");
        writer.FinishBlock();
    }
}
