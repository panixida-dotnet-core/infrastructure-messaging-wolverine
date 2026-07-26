using System.Reflection;

using Wolverine.Persistence;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public class PersistenceFrameProviderProxy : DispatchProxy
{
    public bool CanApplyResult { get; private set; }

    public int ApplyTransactionSupportCallCount { get; private set; }

    public static IPersistenceFrameProvider Create(
        bool canApply,
        out PersistenceFrameProviderProxy proxy)
    {
        var provider = DispatchProxy.Create<
            IPersistenceFrameProvider,
            PersistenceFrameProviderProxy>();
        proxy = (PersistenceFrameProviderProxy)(object)provider!;
        proxy.CanApplyResult = canApply;

        return provider!;
    }

    protected override object? Invoke(
        MethodInfo? targetMethod,
        object?[]? args)
    {
        _ = args;

        if (targetMethod?.Name == nameof(IPersistenceFrameProvider.CanApply))
        {
            return CanApplyResult;
        }

        if (targetMethod?.Name ==
            nameof(IPersistenceFrameProvider.ApplyTransactionSupport))
        {
            ApplyTransactionSupportCallCount++;
            return null;
        }

        throw new NotSupportedException(
            $"Method '{targetMethod?.Name}' is not supported by the test proxy.");
    }
}
