using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal sealed class RequestMiddlewareChainPolicy(RequestMiddlewareRegistry registry) : IChainPolicy
{
    public void Apply(
        IReadOnlyList<IChain> chains,
        GenerationRules rules,
        IServiceContainer container)
    {
        for (var i = 0; i < chains.Count; i++)
        {
            if (chains[i] is not HandlerChain chain ||
                !IsRequestMessageType(chain.MessageType))
            {
                continue;
            }

            var resultVariable = ResolveResultVariable(chain);
            if (resultVariable is null)
            {
                continue;
            }

            ApplyToChain(chain, resultVariable);
        }
    }

    private void ApplyToChain(HandlerChain chain, Variable resultVariable)
    {
        AddFinallyMiddleware(chain, resultVariable);
        AddBeforeMiddleware(chain, resultVariable.VariableType);
        AddAfterMiddleware(chain, resultVariable);
    }

    private void AddFinallyMiddleware(HandlerChain chain, Variable resultVariable)
    {
        var frame = FinallyRequestMiddlewareFrame.TryCreate(
            chain.MessageType,
            resultVariable,
            registry.FinallyMiddlewareTypes);

        if (frame is not null)
        {
            chain.Middleware.Add(frame);
        }
    }

    private void AddBeforeMiddleware(HandlerChain chain, Type resultType)
    {
        var frame = BeforeRequestMiddlewareFrame.TryCreate(
            chain.MessageType,
            resultType,
            registry.BeforeMiddlewareTypes);

        if (frame is not null)
        {
            chain.Middleware.Add(frame);
        }
    }

    private void AddAfterMiddleware(HandlerChain chain, Variable resultVariable)
    {
        var frame = AfterRequestMiddlewareFrame.TryCreate(
            chain.MessageType,
            resultVariable,
            registry.AfterMiddlewareTypes);

        if (frame is not null)
        {
            chain.Middleware.Add(frame);
        }
    }

    internal static bool IsRequestMessageType(Type messageType)
    {
        return messageType
            .GetInterfaces()
            .Any(item =>
                item.IsGenericType &&
                item.GetGenericTypeDefinition() == typeof(IRequest<>) &&
                typeof(Result).IsAssignableFrom(item.GenericTypeArguments[0]));
    }

    private static Variable? ResolveResultVariable(HandlerChain chain)
    {
        var resultVariables = chain.ReturnVariablesOfType<Result>().ToArray();

        if (resultVariables.Length == 0)
        {
            return null;
        }

        if (resultVariables.Length > 1)
        {
            throw new InvalidOperationException(
                $"Handler chain '{chain}' has more than one Result return variable. " +
                "This custom request middleware supports exactly one Result return variable.");
        }

        return resultVariables[0];
    }
}
