using JasperFx.CodeGeneration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

using Wolverine.Persistence;
using Wolverine.Persistence.Sagas;
using Wolverine.Runtime.Handlers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class WolverineModuleTransactionPolicyTests
{
    [Fact(DisplayName = "Apply skips event handler without a matching persistence provider")]
    public void ApplyShouldSkipEventHandlerWithoutMatchingPersistenceProvider()
    {
        var policy = new WolverineModuleTransactionPolicy();
        var chain = CreateHandlerChain();
        var rules = CreateRules(
            PersistenceFrameProviderProxy.Create(
                canApply: false,
                out var provider));

        policy.Apply([chain], rules, null!);

        chain.IsTransactional.ShouldBeFalse();
        provider.ApplyTransactionSupportCallCount.ShouldBe(0);
    }

    [Fact(DisplayName = "Apply uses the single matching persistence provider")]
    public void ApplyShouldUseSingleMatchingPersistenceProvider()
    {
        var policy = new WolverineModuleTransactionPolicy();
        var chain = CreateHandlerChain();
        var rules = CreateRules(
            PersistenceFrameProviderProxy.Create(
                canApply: true,
                out var provider));

        policy.Apply([chain], rules, null!);

        chain.IsTransactional.ShouldBeTrue();
        provider.ApplyTransactionSupportCallCount.ShouldBe(1);
    }

    [Fact(DisplayName = "Apply rejects event handler with multiple matching persistence providers")]
    public void ApplyShouldRejectEventHandlerWithMultipleMatchingPersistenceProviders()
    {
        var policy = new WolverineModuleTransactionPolicy();
        var chain = CreateHandlerChain();
        var rules = CreateRules(
            PersistenceFrameProviderProxy.Create(
                canApply: true,
                out _),
            PersistenceFrameProviderProxy.Create(
                canApply: true,
                out _));

        void act() => policy.Apply([chain], rules, null!);

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldBe(
            $"Handler chain for message '{typeof(TestDomainEvent).FullName}' matches more than one persistence provider. " +
            "Use a single transactional persistence provider or opt out with NonTransactionalAttribute.");
    }

    private static HandlerChain CreateHandlerChain()
    {
        return new HandlerChain(
            typeof(TestDomainEvent),
            new HandlerGraph());
    }

    private static GenerationRules CreateRules(
        params IPersistenceFrameProvider[] providers)
    {
        var rules = new GenerationRules();
        rules.Properties[GenerationRulesExtensions.PersistenceKey] =
            providers.ToList();

        return rules;
    }
}
