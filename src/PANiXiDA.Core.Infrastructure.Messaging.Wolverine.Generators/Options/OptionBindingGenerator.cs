using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Validation;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

/// <summary>
/// Generates reflection-free configuration binders for Wolverine options and their derived types.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class OptionBindingGenerator : IIncrementalGenerator
{
    private const string OptionsNamespace = "PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.";
    private const string GeneratedBinderKey = "PANiXiDA.Wolverine.GeneratedOptionBinder";
    internal const string Registry = "global::PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation.OptionBindingRegistry";

    private static readonly string[] OptionTypeNames = ["KafkaBrokerOption", "KafkaProducerOption", "KafkaConsumerOption", "RecurringCommandOption"];

    private static readonly DiagnosticDescriptor UnsupportedOption = new(
        "PANWOLVSG002", "Options cannot be bound by the source generator",
        "Options '{0}' cannot be bound without reflection: {1}", "OptionBinding",
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, Generate);
    }

    private static void Generate(SourceProductionContext context, Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(Registry.Substring("global::".Length)) is null)
        {
            return;
        }

        var optionTypes = OptionTypeNames
            .Select(name => compilation.GetTypeByMetadataName(OptionsNamespace + name))
            .Where(type => type is not null).ToArray();
        var keyNameAttribute = compilation.GetTypeByMetadataName("Microsoft.Extensions.Configuration.ConfigurationKeyNameAttribute");
        var metadataAttribute = compilation.GetTypeByMetadataName("System.Reflection.AssemblyMetadataAttribute");
        var source = new StringBuilder("""
            #nullable disable
            #pragma warning disable CA2255, CS0618

            [global::System.CodeDom.Compiler.GeneratedCode("PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators", "1.0.0")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            file static class GeneratedWolverineOptionBindings
            {
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Initialize()
                {

            """);
        var optionIndex = 0;
        var metadata = new StringBuilder();
        var methods = new StringBuilder();
        var candidates = GetCandidates(compilation, context.CancellationToken)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal).ToArray();
        foreach (var type in candidates.Where(type => IsOption(type, optionTypes) && !HasGeneratedBinder(type, metadataAttribute)))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (type.IsFileLocal || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedOption, type.Locations.FirstOrDefault(),
                        type.Name, "the type must be accessible to generated code"));
                }

                continue;
            }

            var builder = new OptionBindingSourceBuilder(compilation, keyNameAttribute, optionIndex++);
            var method = builder.Build(type);
            var bindingSource = builder.Complete();
            if (builder.Error is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedOption,
                    type.Locations.FirstOrDefault(location => location.IsInSource), type.Name, builder.Error));
                continue;
            }

            source.Append("        ").Append(Registry).Append(".Register<")
                .Append(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(">(static section => ").Append(method).Append("(section, default));\n");
            methods.Append(bindingSource);
            if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
            {
                metadata.Append("[assembly: global::System.Reflection.AssemblyMetadata(")
                    .Append(SymbolDisplay.FormatLiteral(GeneratedBinderKey, true)).Append(", ")
                    .Append(SymbolDisplay.FormatLiteral(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), true)).Append(")]\n");
            }
        }

        source.Append("    }\n").Append("""
                private sealed class UnsupportedRuntimeTypeException(global::System.Type type, global::System.Type declaredType, string path)
                    : global::System.NotSupportedException($"Runtime configuration type '{type}' at '{path}' differs from the declared model type '{declaredType}'. Declare the concrete configuration model type.") { }

            """).Append(methods).Append("}\n");
        context.AddSource("OptionBindings.g.cs", metadata.Append(source).ToString());
    }

    private static bool HasGeneratedBinder(INamedTypeSymbol type, INamedTypeSymbol? metadataAttribute)
    {
        return type.ContainingAssembly.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, metadataAttribute) &&
            attribute.ConstructorArguments.Length == 2 &&
            attribute.ConstructorArguments[0].Value as string == GeneratedBinderKey &&
            attribute.ConstructorArguments[1].Value as string == type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static IEnumerable<INamedTypeSymbol> GetCandidates(Compilation compilation, CancellationToken token)
    {
        var declaredTypes = compilation.SourceModule.ReferencedAssemblySymbols.Append(compilation.Assembly)
            .SelectMany(assembly => ValidatorRegistrationGenerator.EnumerateTypes(assembly.GlobalNamespace))
            .Where(type => !type.IsAbstract && !ValidatorRegistrationGenerator.HasTypeParameters(type));
        return declaredTypes.Concat(GetClosedTypes(compilation, token));
    }

    private static IEnumerable<INamedTypeSymbol> GetClosedTypes(Compilation compilation, CancellationToken token)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var symbols = tree.GetRoot(token).DescendantNodes().OfType<GenericNameSyntax>()
                .Select(name => model.GetSymbolInfo(name, token).Symbol).ToArray();
            var types = symbols.OfType<IMethodSymbol>().SelectMany(method => method.TypeArguments)
                .OfType<INamedTypeSymbol>().Concat(symbols.OfType<INamedTypeSymbol>());
            foreach (var type in types.Where(type => !type.IsAbstract && !ContainsTypeParameter(type)))
            {
                yield return type;
            }
        }
    }

    private static bool ContainsTypeParameter(INamedTypeSymbol type)
    {
        return type.TypeArguments.Any(argument => argument is ITypeParameterSymbol ||
            argument is INamedTypeSymbol named && ContainsTypeParameter(named)) ||
            type.ContainingType is not null && ContainsTypeParameter(type.ContainingType);
    }

    private static bool IsOption(INamedTypeSymbol type, INamedTypeSymbol?[] optionTypes)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (optionTypes.Any(option => SymbolEqualityComparer.Default.Equals(current, option)))
            {
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<IPropertySymbol> GetProperties(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.SetMethod?.IsOverride != true && !property.IsStatic && !property.IsIndexer &&
                    property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
                {
                    yield return property;
                }
            }
        }
    }

    internal static string? GetConversion(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            type = named.TypeArguments[0];
        }

        type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type.TypeKind == TypeKind.Enum)
        {
            return $"global::System.Enum.Parse<{typeName}>(value, ignoreCase: true)";
        }

        if (type.ToDisplayString() == "System.Uri")
        {
            return $"({typeName})new global::System.UriTypeConverter().ConvertFromInvariantString(value)!";
        }

        var converter = type.SpecialType switch
        {
            SpecialType.System_String => "StringConverter",
            SpecialType.System_Boolean => "BooleanConverter",
            SpecialType.System_Char => "CharConverter",
            SpecialType.System_Byte => "ByteConverter",
            SpecialType.System_SByte => "SByteConverter",
            SpecialType.System_Int16 => "Int16Converter",
            SpecialType.System_UInt16 => "UInt16Converter",
            SpecialType.System_Int32 => "Int32Converter",
            SpecialType.System_UInt32 => "UInt32Converter",
            SpecialType.System_Int64 => "Int64Converter",
            SpecialType.System_UInt64 => "UInt64Converter",
            SpecialType.System_Single => "SingleConverter",
            SpecialType.System_Double => "DoubleConverter",
            SpecialType.System_Decimal => "DecimalConverter",
            SpecialType.System_DateTime => "DateTimeConverter",
            _ => type.ToDisplayString() switch
            {
                "System.Guid" => "GuidConverter",
                "System.TimeSpan" => "TimeSpanConverter",
                "System.DateTimeOffset" => "DateTimeOffsetConverter",
                "System.DateOnly" => "DateOnlyConverter",
                "System.TimeOnly" => "TimeOnlyConverter",
                "System.Version" => "VersionConverter",
                "System.Half" => "HalfConverter",
                "System.Int128" => "Int128Converter",
                "System.UInt128" => "UInt128Converter",
                "System.Globalization.CultureInfo" => "CultureInfoConverter",
                _ => null,
            },
        };
        return converter is null ? null : $"({typeName})new global::System.ComponentModel.{converter}().ConvertFromInvariantString(value)!";
    }
}
