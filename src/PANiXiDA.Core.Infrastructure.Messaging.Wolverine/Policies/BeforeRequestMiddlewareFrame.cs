using JasperFx.CodeGeneration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class BeforeRequestMiddlewareFrame(
    Type requestType,
    Type closedMiddlewareType) : RequestMiddlewareFrameBase(requestType, closedMiddlewareType)
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
            closedMiddlewareType);
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        var middlewareVariableName = BuildMiddlewareVariableName();
        var constructorArguments = GetConstructorArguments();
        var middlewareTypeName = GetMiddlewareTypeName();

        writer.WriteLine(string.Empty);
        writer.WriteComment($"Run {GetFriendlyMiddlewareTypeName()} before handler execution");
        writer.WriteLine($"var {middlewareVariableName} = new {middlewareTypeName}({constructorArguments});");
        writer.WriteLine(
            $"await {middlewareVariableName}.{nameof(IBeforeRequestBehavior<,>.BeforeAsync)}({requestVariable.Usage}, {cancellationVariable.Usage}).ConfigureAwait(false);");

        Next?.GenerateCode(method, writer);
    }
}
