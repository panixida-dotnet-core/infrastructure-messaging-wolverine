using JasperFx;
using JasperFx.CodeGeneration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

using Wolverine.Attributes;
using Wolverine.Configuration;
using Wolverine.Persistence.Sagas;
using Wolverine.Runtime.Handlers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class WolverineModuleTransactionPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            if (RequestMiddlewareChainPolicy.IsResultRequest(chain.MessageType) ||
                chain.HasAttribute<TransactionalAttribute>() ||
                chain.HasAttribute<NonTransactionalAttribute>())
            {
                continue;
            }

            chain.ApplyImpliedMiddlewareFromHandlers(rules);

            var providers = rules
                .PersistenceProviders()
                .Where(provider => provider.CanApply(chain, container))
                .ToArray();

            if (providers.Length != 1)
            {
                continue;
            }

            providers[0].ApplyTransactionSupport(chain, container);
            chain.IsTransactional = true;
        }
    }
}
