using JasperFx;
using JasperFx.CodeGeneration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class DomainEventPublicationPolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            if (!typeof(DomainEvent).IsAssignableFrom(chain.MessageType) ||
                RequestMiddlewareChainPolicy.IsRequestMessageType(chain.MessageType) ||
                chain.Postprocessors.Any(frame => frame is PublishDomainEventsFrame))
            {
                continue;
            }

            chain.Postprocessors.Insert(0, new PublishDomainEventsFrame(chain.MessageType));
        }
    }
}
