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
        if (chain.Handlers.Count != 1 || chain.Handlers[0].ReturnVariable != resultVariable)
        {
            throw new InvalidOperationException(
                $"Handler chain '{chain}' must have exactly one handler returning Result or Result<T>.");
        }

        if (chain.Handlers[0] is not RequestMiddlewareHandlerCall)
        {
            chain.Handlers[0] = new RequestMiddlewareHandlerCall(chain.MessageType, chain.Handlers[0], registry);
        }
    }

    internal static bool IsRequestMessageType(Type messageType)
    {
        return typeof(IRequest<Result>).IsAssignableFrom(messageType);
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
