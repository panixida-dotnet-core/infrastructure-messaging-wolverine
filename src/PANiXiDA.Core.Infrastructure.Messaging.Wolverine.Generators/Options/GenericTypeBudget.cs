using Microsoft.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal static class GenericTypeBudget
{
    internal const int MaxRecursiveTypeNodes = 64;

    internal static bool TryConsume(ITypeSymbol type, ref int remaining)
    {
        if (remaining == 0)
        {
            return false;
        }
        remaining--;
        if (type is IArrayTypeSymbol array)
        {
            return TryConsume(array.ElementType, ref remaining);
        }
        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (!TryConsume(argument, ref remaining))
                {
                    return false;
                }
            }
            if (named.ContainingType is { } containing)
            {
                return TryConsume(containing, ref remaining);
            }
        }
        return true;
    }
}
