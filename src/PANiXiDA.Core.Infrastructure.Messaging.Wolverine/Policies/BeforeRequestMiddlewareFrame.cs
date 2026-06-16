using JasperFx.CodeGeneration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class BeforeRequestMiddlewareFrame(
    Type requestType,
    Type resultType,
    Type closedMiddlewareType) : RequestMiddlewareFrameBase(
    requestType,
    closedMiddlewareType,
    requiresMessageContext: true)
{
    internal static BeforeRequestMiddlewareFrame? TryCreate(
        Type requestType,
        Type resultType,
        Type middlewareType)
    {
        if (!RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
                middlewareType,
                requestType,
                resultType,
                typeof(IBeforeRequestBehavior<,>),
                out var closedMiddlewareType))
        {
            return null;
        }

        return new BeforeRequestMiddlewareFrame(
            requestType,
            resultType,
            closedMiddlewareType);
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        var middlewareVariableName = BuildMiddlewareVariableName();
        var constructorArguments = GetConstructorArguments();
        var middlewareTypeName = GetMiddlewareTypeName();
        var beforeResultVariableName = $"__beforeResult_{uniqueSuffix}";
        var failureResultCode = RequestMiddlewareCodeGeneration.BuildFailureResultCode(
            resultType,
            beforeResultVariableName);

        writer.WriteLine(string.Empty);
        writer.WriteComment($"Run {GetFriendlyMiddlewareTypeName()} before handler execution");
        writer.WriteLine($"var {middlewareVariableName} = new {middlewareTypeName}({constructorArguments});");
        writer.WriteLine(
            $"var {beforeResultVariableName} = await {middlewareVariableName}.{nameof(IBeforeRequestBehavior<,>.BeforeAsync)}({requestVariable.Usage}, {cancellationVariable.Usage}).ConfigureAwait(false);");

        writer.Write($"BLOCK:if ({beforeResultVariableName}.{nameof(Result.IsFailure)})");
        writer.WriteLine(
            $"await {messageContextVariable.Usage}.EnqueueCascadingAsync({failureResultCode}).ConfigureAwait(false);");
        writer.WriteLine("return;");
        writer.FinishBlock();

        Next?.GenerateCode(method, writer);
    }
}
