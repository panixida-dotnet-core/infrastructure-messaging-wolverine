using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class AfterRequestMiddlewareFrame(
    Type requestType,
    Variable resultVariable,
    RequestMiddlewareDescriptor[] descriptors) : RequestMiddlewareFrameBase(
    requestType,
    descriptors)
{
    internal static AfterRequestMiddlewareFrame? TryCreate(
        Type requestType,
        Variable resultVariable,
        IReadOnlyList<Type> middlewareTypes)
    {
        var resultType = resultVariable.VariableType;

        var descriptors = RequestMiddlewareDescriptor.Resolve(
            requestType,
            resultType,
            typeof(IAfterRequestBehavior<,>),
            middlewareTypes);

        if (descriptors.Length == 0)
        {
            return null;
        }

        return new AfterRequestMiddlewareFrame(
            requestType,
            resultVariable,
            descriptors);
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        Next?.GenerateCode(method, writer);

        foreach (var middleware in middlewareDescriptors)
        {
            WriteMiddleware(writer, middleware);
        }
    }

    private void WriteMiddleware(
        ISourceWriter writer,
        RequestMiddlewareDescriptor middleware)
    {
        var middlewareVariableName = middleware.BuildVariableName();
        var constructorArguments = middleware.GetConstructorArguments();
        var middlewareTypeName = middleware.GetTypeName();

        writer.WriteLine(string.Empty);
        writer.WriteComment(
            $"Run {middleware.GetFriendlyTypeName()} after handler execution");
        writer.WriteLine(
            $"var {middlewareVariableName} = new {middlewareTypeName}({constructorArguments});");
        writer.WriteLine(
            $"await {middlewareVariableName}.{nameof(IAfterRequestBehavior<,>.AfterAsync)}({requestVariable.Usage}, {resultVariable.Usage}, {cancellationVariable.Usage}).ConfigureAwait(false);");
    }
}
