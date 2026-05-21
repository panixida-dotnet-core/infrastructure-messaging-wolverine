using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class RequestMiddlewareRegistryBuilderTests
{
    [Fact(DisplayName = "Builder registers before, after and finally middleware in order")]
    public void BuilderShouldRegisterMiddlewareInOrder()
    {
        var registry = RequestMiddlewareRegistry.Create(builder =>
        {
            builder.AddBefore(typeof(TestBeforeBehavior<,>));
            builder.AddBefore(typeof(SecondBeforeBehavior<,>));
            builder.AddAfter(typeof(TestAfterBehavior<,>));
            builder.AddFinally(typeof(TestFinallyBehavior<,>));
        });

        registry.BeforeMiddlewareTypes.Should().Equal(
            typeof(TestBeforeBehavior<,>),
            typeof(SecondBeforeBehavior<,>));
        registry.AfterMiddlewareTypes.Should().Equal(typeof(TestAfterBehavior<,>));
        registry.FinallyMiddlewareTypes.Should().Equal(typeof(TestFinallyBehavior<,>));
    }

    [Fact(DisplayName = "Builder generic methods register middleware")]
    public void BuilderGenericMethodsShouldRegisterMiddleware()
    {
        var registry = RequestMiddlewareRegistryBuilder
            .Create()
            .AddBefore<TestBeforeBehavior<TestCommand, Result>>()
            .AddAfter<TestAfterBehavior<TestCommand, Result>>()
            .AddFinally<TestFinallyBehavior<TestCommand, Result>>()
            .Build();

        registry.BeforeMiddlewareTypes.Should().Equal(typeof(TestBeforeBehavior<TestCommand, Result>));
        registry.AfterMiddlewareTypes.Should().Equal(typeof(TestAfterBehavior<TestCommand, Result>));
        registry.FinallyMiddlewareTypes.Should().Equal(typeof(TestFinallyBehavior<TestCommand, Result>));
    }

    [Fact(DisplayName = "Builder registers params middleware arrays")]
    public void BuilderShouldRegisterMiddlewareArrays()
    {
        var builder = RequestMiddlewareRegistryBuilder.Create();

        var registry = builder
            .AddBefore(
                typeof(TestBeforeBehavior<,>),
                typeof(SecondBeforeBehavior<,>))
            .AddAfter(
                typeof(TestAfterBehavior<,>),
                typeof(SecondAfterBehavior<,>))
            .AddFinally(
                typeof(TestFinallyBehavior<,>),
                typeof(SecondFinallyBehavior<,>))
            .Build();

        registry.BeforeMiddlewareTypes.Should().HaveCount(2);
        registry.AfterMiddlewareTypes.Should().HaveCount(2);
        registry.FinallyMiddlewareTypes.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Empty registry contains no middleware")]
    public void EmptyRegistryShouldContainNoMiddleware()
    {
        RequestMiddlewareRegistry.Empty.BeforeMiddlewareTypes.Should().BeEmpty();
        RequestMiddlewareRegistry.Empty.AfterMiddlewareTypes.Should().BeEmpty();
        RequestMiddlewareRegistry.Empty.FinallyMiddlewareTypes.Should().BeEmpty();
    }
}
