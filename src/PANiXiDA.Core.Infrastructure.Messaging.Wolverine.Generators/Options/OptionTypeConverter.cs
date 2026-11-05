using Microsoft.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal static class OptionTypeConverter
{
    internal static string? GetConstruction(ITypeSymbol type, Compilation compilation, out string? error)
    {
        error = null;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        var attribute = FindAttribute(type, compilation);
        if (attribute is null)
        {
            return null;
        }

        var argument = attribute.ConstructorArguments.FirstOrDefault().Value;
        if (argument is null or "")
        {
            return null;
        }

        var converter = argument as INamedTypeSymbol;
        if (argument is string name)
        {
            converter = OptionConverterTypeName.Resolve(name, compilation);
        }

        if (converter is not null && !converter.IsAbstract &&
            !converter.IsUnboundGenericType &&
            !OptionTypeDiscovery.ContainsTypeParameter(converter) &&
            compilation.IsSymbolAccessibleWithin(converter, compilation.Assembly) &&
            IsConverter(converter, compilation))
        {
            var construction = Construct(converter, type, compilation);
            if (construction is not null)
            {
                return construction;
            }
        }

        error = $"TypeConverter for '{type}' must be an accessible concrete TypeConverter with a public parameterless or System.Type constructor";
        return null;
    }

    private static AttributeData? FindAttribute(ITypeSymbol type, Compilation compilation)
    {
        var attributeType = compilation.GetTypeByMetadataName("System.ComponentModel.TypeConverterAttribute");
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            var attribute = current.GetAttributes().FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attributeType));
            if (attribute is not null)
            {
                return attribute;
            }
        }

        return null;
    }

    private static bool IsConverter(INamedTypeSymbol type, Compilation compilation)
    {
        var converterType = compilation.GetTypeByMetadataName("System.ComponentModel.TypeConverter");
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, converterType))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Construct(INamedTypeSymbol converter, ITypeSymbol type, Compilation compilation)
    {
        var constructors = converter.InstanceConstructors.Where(candidate => candidate.DeclaredAccessibility == Accessibility.Public).ToArray();
        var typeSymbol = compilation.GetTypeByMetadataName("System.Type");
        var constructor = constructors.FirstOrDefault(candidate => candidate.Parameters.Length == 1 &&
            candidate.Parameters[0].RefKind == RefKind.None &&
            SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, typeSymbol));
        if (constructor is not null)
        {
            return $"new {converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}(typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}))";
        }

        return constructors.Any(candidate => candidate.Parameters.Length == 0)
            ? $"new {converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()"
            : null;
    }
}
