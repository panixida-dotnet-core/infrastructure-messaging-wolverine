using System.Reflection;

using JasperFx.CodeGeneration.Frames;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

using Wolverine.Runtime.Handlers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class RequestMiddlewareChainPolicyTests
{
    [Fact(DisplayName = "Apply skips request handler chain without Result return variable")]
    public void ApplyShouldSkipRequestHandlerChainWithoutResultReturnVariable()
    {
        var registry = RequestMiddlewareRegistry.Create(builder =>
        {
            builder.AddBefore(typeof(TestBeforeBehavior<,>));
        });
        var policy = new RequestMiddlewareChainPolicy(registry);
        var chain = new HandlerChain(typeof(TestCommand), new HandlerGraph());

        policy.Apply([chain], null!, null!);

        chain.Middleware.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Apply throws when request handler chain has more than one Result return variable")]
    public void ApplyShouldThrowWhenRequestHandlerChainHasMoreThanOneResultReturnVariable()
    {
        var policy = new RequestMiddlewareChainPolicy(RequestMiddlewareRegistry.Empty);
        var chain = CreateHandlerChain(nameof(ResultHandler.Handle));

        AddHandlerCall(chain, nameof(ResultHandler.HandleAgain));

        var act = () => policy.Apply([chain], null!, null!);

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldStartWith("Handler chain '");
        exception.Message.ShouldContain("' has more than one Result return variable.");
    }

    private static void AddHandlerCall(
        HandlerChain chain,
        string methodName)
    {
        var handlersField = typeof(HandlerChain).GetField(
            "Handlers",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var handlers = (List<MethodCall>?)handlersField!.GetValue(chain);

        if (handlers is null)
        {
            handlers = [];
            handlersField.SetValue(chain, handlers);
        }

        handlers.Add(new MethodCall(typeof(ResultHandler), methodName));
    }

    private static HandlerChain CreateHandlerChain(string methodName)
    {
        var constructor = typeof(HandlerChain).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(MethodCall), typeof(HandlerGraph)],
            modifiers: null);

        return (HandlerChain)constructor!.Invoke(
            [new MethodCall(typeof(ResultHandler), methodName), new HandlerGraph()]);
    }
}
