using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class FinallyRequestMiddlewareFrame : AsyncFrame
{
    private readonly Type requestType;
    private readonly Variable resultVariable;
    private readonly FinallyMiddleware[] middlewares;
    private readonly string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

    private Variable requestVariable = null!;
    private Variable cancellationVariable = null!;

    private FinallyRequestMiddlewareFrame(
        Type requestType,
        Variable resultVariable,
        FinallyMiddleware[] middlewares)
    {
        this.requestType = requestType;
        this.resultVariable = resultVariable;
        this.middlewares = middlewares;
    }

    internal static FinallyRequestMiddlewareFrame? TryCreate(
        Type requestType,
        Variable resultVariable,
        IReadOnlyList<Type> middlewareTypes)
    {
        var middlewares = middlewareTypes
            .Select(middlewareType => TryCreateMiddleware(
                requestType,
                resultVariable.VariableType,
                middlewareType))
            .OfType<FinallyMiddleware>()
            .ToArray();

        if (middlewares.Length == 0)
        {
            return null;
        }

        return new FinallyRequestMiddlewareFrame(
            requestType,
            resultVariable,
            middlewares);
    }

    public override IEnumerable<Variable> FindVariables(
        IMethodVariables chain)
    {
        requestVariable = chain.FindVariable(requestType);
        yield return requestVariable;

        cancellationVariable = chain.FindVariable(
            typeof(CancellationToken));
        yield return cancellationVariable;

        foreach (var middleware in middlewares)
        {
            middleware.ConstructorVariables =
                RequestMiddlewareCodeGeneration.ResolveConstructorVariables(
                    chain,
                    middleware.Constructor);

            foreach (var constructorVariable in middleware.ConstructorVariables)
            {
                yield return constructorVariable;
            }
        }
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
        Next?.GenerateCode(method, writer);
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
        var middleware = middlewares[index];

        if (index < middlewares.Length - 1)
        {
            writer.Write("BLOCK:try");
        }

        WriteInvocation(
            writer,
            middleware,
            resultLocalName,
            exceptionLocalName);

        if (index >= middlewares.Length - 1)
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
        FinallyMiddleware middleware,
        string resultLocalName,
        string exceptionLocalName)
    {
        var middlewareVariableName =
            RequestMiddlewareCodeGeneration.BuildVariableName(
                middleware.Type,
                middleware.UniqueSuffix);
        var middlewareTypeName =
            RequestMiddlewareCodeGeneration.GetCodeTypeName(
                middleware.Type);
        var constructorArguments = string.Join(
            ", ",
            middleware.ConstructorVariables.Select(variable =>
                variable.Usage));

        writer.WriteLine(
            $"var {middlewareVariableName} = new {middlewareTypeName}({constructorArguments});");
        writer.WriteLine(
            $"await {middlewareVariableName}.{nameof(IFinallyRequestBehavior<,>.FinallyAsync)}({requestVariable.Usage}, {resultLocalName}, {exceptionLocalName}, {cancellationVariable.Usage}).ConfigureAwait(false);");
    }

    private static FinallyMiddleware? TryCreateMiddleware(
        Type requestType,
        Type resultType,
        Type middlewareType)
    {
        if (!RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
                middlewareType,
                requestType,
                resultType,
                typeof(IFinallyRequestBehavior<,>),
                out var closedMiddlewareType))
        {
            return null;
        }

        return new FinallyMiddleware(
            closedMiddlewareType,
            RequestMiddlewareCodeGeneration.ResolveConstructor(
                closedMiddlewareType));
    }

    private sealed class FinallyMiddleware(
        Type type,
        ConstructorInfo constructor)
    {
        internal Type Type { get; } = type;

        internal ConstructorInfo Constructor { get; } = constructor;

        internal string UniqueSuffix { get; } =
            Guid.NewGuid().ToString("N")[..8];

        internal Variable[] ConstructorVariables { get; set; } = [];
    }
}
