using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class BeforeRequestMiddlewareFrame(
    Type requestType,
    Variable resultVariable,
    RequestMiddlewareDescriptor[] descriptors) : RequestMiddlewareFrameBase(
    requestType,
    descriptors)
{
    internal static BeforeRequestMiddlewareFrame? TryCreate(
        Type requestType,
        Variable resultVariable,
        Type handlerType,
        IReadOnlyList<Type> middlewareTypes)
    {
        var descriptors = RequestMiddlewareDescriptor.Resolve(
            requestType,
            resultVariable.VariableType,
            typeof(IBeforeRequestBehavior<,,>),
            middlewareTypes,
            handlerType);

        if (descriptors.Length == 0)
        {
            return null;
        }

        return new BeforeRequestMiddlewareFrame(
            requestType,
            resultVariable,
            descriptors);
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        foreach (var middleware in middlewareDescriptors)
        {
            WriteMiddleware(writer, middleware);
        }

        Next?.GenerateCode(method, writer);
    }

    private void WriteMiddleware(
        ISourceWriter writer,
        RequestMiddlewareDescriptor middleware)
    {
        var middlewareVariableName =
            RequestMiddlewareCodeGeneration.BuildVariableName(
                middleware.Type,
                middleware.UniqueSuffix);
        var middlewareTypeName =
            RequestMiddlewareCodeGeneration.GetCodeTypeName(middleware.Type);
        var beforeResultVariableName =
            $"__beforeResult_{middleware.UniqueSuffix}";
        var failureResultCode =
            RequestMiddlewareCodeGeneration.BuildFailureResultCode(
                resultVariable.VariableType,
                beforeResultVariableName);

        writer.WriteLine(string.Empty);
        writer.Write($"BLOCK:if ({resultVariable.Usage} is null)");
        writer.WriteLine(
            $"var {middlewareVariableName} = new {middlewareTypeName}({middleware.ConstructorArguments});");
        writer.WriteLine(
            $"var {beforeResultVariableName} = await {middlewareVariableName}.{nameof(IBeforeRequestBehavior<,,>.BeforeAsync)}({requestVariable.Usage}, {cancellationVariable.Usage}).ConfigureAwait(false);");

        writer.Write(
            $"BLOCK:if ({beforeResultVariableName}.{nameof(Result.IsFailure)})");
        writer.WriteLine(
            $"{resultVariable.Usage} = {failureResultCode};");
        writer.FinishBlock();
        writer.FinishBlock();
    }
}
