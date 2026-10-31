using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

internal sealed class PublishDomainEventsFrame(Type eventType) : AsyncFrame
{
    private Variable message = null!;
    private Variable eventBus = null!;
    private Variable aggregateTracker = null!;
    private Variable cancellation = null!;

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        message = chain.FindVariable(eventType);
        eventBus = chain.FindVariable(typeof(IEventBus));
        aggregateTracker = chain.FindVariable(typeof(IAggregateTracker));
        cancellation = chain.FindVariable(typeof(CancellationToken));
        return [message, eventBus, aggregateTracker, cancellation];
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        var behaviorType = typeof(PublishDomainEventsBehavior<,>).MakeGenericType(eventType, typeof(Result));
        var typeName = RequestMiddlewareCodeGeneration.GetCodeTypeName(behaviorType);
        writer.WriteLine($"var publicationBehavior = new {typeName}({eventBus.Usage}, {aggregateTracker.Usage});");
        writer.WriteLine(
            $"await publicationBehavior.AfterAsync({message.Usage}, global::PANiXiDA.Core.ResultPattern.Result.Success(), {cancellation.Usage}).ConfigureAwait(false);");

        Next?.GenerateCode(method, writer);
    }
}
