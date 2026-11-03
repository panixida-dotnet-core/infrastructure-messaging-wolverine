using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

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
                !RequestBehaviorMetadata.TryGetMessage(chain.MessageType, chain.Handlers.FirstOrDefault()?.HandlerType, out var isRequest))
            {
                continue;
            }

            if (!isRequest)
            {
                ApplyToNativeChain(chain);
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

    private void ApplyToNativeChain(HandlerChain chain)
    {
        for (var index = 0; index < chain.Handlers.Count; index++)
        {
            var handler = chain.Handlers[index];
            if (handler is RequestMiddlewareHandlerCall)
            {
                continue;
            }

            if (handler.ReturnVariable is not null)
            {
                throw new InvalidOperationException("Native message handlers must return void, Task or ValueTask.");
            }

            chain.Handlers[index] = new RequestMiddlewareHandlerCall(chain.MessageType, handler, registry);
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

    internal static bool IsRequestMessageType(Type messageType, Type? handlerType = null)
    {
        return RequestBehaviorMetadata.TryGetMessage(messageType, handlerType, out var isRequest) && isRequest;
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
