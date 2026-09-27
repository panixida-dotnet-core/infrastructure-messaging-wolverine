using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Validation;

/// <summary>
/// Generates scoped FluentValidation registrations for the current and referenced assemblies.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ValidatorRegistrationGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var source = context.CompilationProvider
            .Select(static (compilation, token) => Generate((CSharpCompilation)compilation, token))
            .WithComparer(StringComparer.Ordinal);

        context.RegisterSourceOutput(source, static (output, text) =>
        {
            if (text.Length > 0)
            {
                output.AddSource("ValidatorRegistrations.g.cs", text);
            }
        });
    }

    private static string Generate(
        CSharpCompilation compilation,
        CancellationToken token)
    {
        compilation = compilation.WithOptions(
            compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
        var validatorContract = compilation.GetTypeByMetadataName("FluentValidation.IValidator`1");
        if (validatorContract is null || compilation.GetTypeByMetadataName(
                "PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation.ValidatorRegistry") is null)
        {
            return string.Empty;
        }

        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols
            .Append(compilation.Assembly)
            .OrderBy(assembly => assembly.Identity.ToString(), StringComparer.Ordinal)
            .ToArray();
        var builder = new ValidatorRegistrationSourceBuilder(assemblies);
        foreach (var assembly in assemblies)
        {
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                token.ThrowIfCancellationRequested();
                if (type.IsAbstract || HasTypeParameters(type))
                {
                    continue;
                }

                var contract = type.AllInterfaces.FirstOrDefault(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, validatorContract));
                if (contract is null)
                {
                    continue;
                }

                builder.Add(
                    type,
                    contract,
                    type.TypeKind == TypeKind.Class && CanReference(compilation, type) &&
                    compilation.IsSymbolAccessibleWithin(contract, compilation.Assembly));
            }
        }

        return builder.Build();
    }

    private static bool CanReference(
        Compilation compilation,
        INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return false;
            }
        }

        return compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
    }

    internal static bool HasTypeParameters(INamedTypeSymbol type)
    {
        return type.Arity > 0 || type.ContainingType is not null && HasTypeParameters(type.ContainingType);
    }

    internal static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers().OrderBy(member => member.MetadataName, StringComparer.Ordinal))
        {
            if (member is INamedTypeSymbol type)
            {
                yield return type;
            }

            if (member is INamespaceOrTypeSymbol nested)
            {
                foreach (var child in EnumerateTypes(nested))
                {
                    yield return child;
                }
            }
        }
    }
}
