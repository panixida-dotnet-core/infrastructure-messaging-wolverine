using System.Reflection.Metadata;

using Microsoft.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal static class OptionConverterTypeName
{
    internal static INamedTypeSymbol? Resolve(string name, Compilation compilation)
    {
        return TypeName.TryParse(name.AsSpan(), out var parsed)
            ? ResolveType(parsed, compilation) as INamedTypeSymbol
            : null;
    }

    private static ITypeSymbol? ResolveType(TypeName name, Compilation compilation)
    {
        if (name.IsArray)
        {
            if (name.IsVariableBoundArrayType && name.GetArrayRank() == 1)
            {
                return null;
            }

            var element = ResolveType(name.GetElementType(), compilation);
            return element is null ? null : compilation.CreateArrayTypeSymbol(element, name.GetArrayRank());
        }

        if (!name.IsSimple && !name.IsConstructedGenericType)
        {
            return null;
        }

        var definitionName = name.IsConstructedGenericType ? name.GetGenericTypeDefinition() : name;
        var definition = ResolveDefinition(definitionName, compilation);
        if (definition is null || !name.IsConstructedGenericType)
        {
            return definition;
        }

        var arguments = name.GetGenericArguments().Select(argument => ResolveType(argument, compilation)).ToArray();
        if (arguments.Any(argument => argument is null))
        {
            return null;
        }

        var offset = 0;
        var constructed = Construct(definition, arguments!, ref offset);
        return offset == arguments.Length ? constructed : null;
    }

    private static INamedTypeSymbol? ResolveDefinition(TypeName name, Compilation compilation)
    {
        if (name.AssemblyName is null || name.AssemblyName.Name is "mscorlib" or "System.Private.CoreLib")
        {
            return compilation.GetTypeByMetadataName(name.FullName);
        }

        var assembly = compilation.SourceModule.ReferencedAssemblySymbols.Append(compilation.Assembly)
            .FirstOrDefault(candidate => string.Equals(candidate.Identity.Name, name.AssemblyName.Name, StringComparison.OrdinalIgnoreCase));
        return assembly?.GetTypeByMetadataName(name.FullName) ?? assembly?.ResolveForwardedType(name.FullName);
    }

    private static INamedTypeSymbol? Construct(INamedTypeSymbol definition, ITypeSymbol[] arguments, ref int offset)
    {
        if (definition.ContainingType is not null)
        {
            var containing = Construct(definition.ContainingType, arguments, ref offset);
            if (containing is null)
            {
                return null;
            }

            definition = containing.GetTypeMembers(definition.Name, definition.Arity).Single();
        }

        if (offset + definition.Arity > arguments.Length)
        {
            return null;
        }

        var result = definition.Arity == 0
            ? definition
            : definition.Construct([.. arguments.Skip(offset).Take(definition.Arity)]);
        offset += definition.Arity;
        return result;
    }
}
