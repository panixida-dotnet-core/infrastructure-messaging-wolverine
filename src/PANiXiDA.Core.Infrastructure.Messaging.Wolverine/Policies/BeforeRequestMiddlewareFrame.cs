using JasperFx.CodeGeneration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class BeforeRequestMiddlewareFrame(
    Type requestType,
    Type resultType,
    RequestMiddlewareDescriptor[] descriptors) : RequestMiddlewareFrameBase(
    requestType,
    descriptors,
    requiresMessageContext: true)
{
    internal static BeforeRequestMiddlewareFrame? TryCreate(
        Type requestType,
        Type resultType,
        IReadOnlyList<Type> middlewareTypes)
    {
        var descriptors = RequestMiddlewareDescriptor.Resolve(
            requestType,
            resultType,
            typeof(IBeforeRequestBehavior<,>),
            middlewareTypes);

        if (descriptors.Length == 0)
        {
            return null;
        }

        return new BeforeRequestMiddlewareFrame(
            requestType,
            resultType,
            descriptors);
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        foreach (var middleware in middlewareDescriptors)
        {
            WriteMiddleware(writer, middleware);
        }

        GenerateNextFrame(method, writer);
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
                resultType,
                beforeResultVariableName);

        writer.WriteLine(string.Empty);
        writer.WriteComment(
            $"Run {RequestMiddlewareCodeGeneration.GetFriendlyTypeName(middleware.Type)} before handler execution");
        writer.WriteLine(
            $"var {middlewareVariableName} = new {middlewareTypeName}({middleware.ConstructorArguments});");
        writer.WriteLine(
            $"var {beforeResultVariableName} = await {middlewareVariableName}.{nameof(IBeforeRequestBehavior<,>.BeforeAsync)}({requestVariable.Usage}, {cancellationVariable.Usage}).ConfigureAwait(false);");

        writer.Write(
            $"BLOCK:if ({beforeResultVariableName}.{nameof(Result.IsFailure)})");
        writer.WriteLine(
            $"await {messageContextVariable.Usage}.EnqueueCascadingAsync({failureResultCode}).ConfigureAwait(false);");
        writer.WriteLine("return;");
        writer.FinishBlock();
    }
}
