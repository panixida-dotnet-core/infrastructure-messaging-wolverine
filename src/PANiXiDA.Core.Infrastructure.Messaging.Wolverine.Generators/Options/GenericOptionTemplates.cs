using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Validation;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal sealed class GenericOptionTemplates(Compilation compilation, INamedTypeSymbol? metadataAttribute, Func<INamedTypeSymbol, bool> isOption)
{
    private const string KeyPrefix = "PANiXiDA.Wolverine.GenericOptionTemplate.v1:";
    private readonly Dictionary<SyntaxTree, SemanticModel> models = [];
    private readonly Dictionary<IAssemblySymbol, ILookup<string, string>> imports = new(SymbolEqualityComparer.Default);

    internal bool IsOption(INamedTypeSymbol type) => isOption(type);

    internal bool HasExports(IAssemblySymbol assembly) => GetCatalog(assembly).Count != 0;

    internal SemanticModel GetModel(SyntaxTree tree)
    {
        if (!models.TryGetValue(tree, out var model))
        {
            model = compilation.GetSemanticModel(tree);
            models.Add(tree, model);
        }
        return model;
    }

    internal IEnumerable<ITypeSymbol> Read(ISymbol member)
    {
        var assembly = member.ContainingAssembly!;
        if (SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
        {
            return [];
        }
        var catalog = GetCatalog(assembly);
        return catalog.Count == 0
            ? []
            : catalog[KeyPrefix + member.GetDocumentationCommentId()]
                .SelectMany(id => DocumentationCommentId.GetSymbolsForReferenceId(id, compilation)).OfType<ITypeSymbol>();
    }

    private ILookup<string, string> GetCatalog(IAssemblySymbol assembly)
    {
        if (!imports.TryGetValue(assembly, out var catalog))
        {
            catalog = assembly.GetAttributes()
                .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, metadataAttribute))
                .Select(attribute => (Key: attribute.ConstructorArguments[0].Value as string, Id: attribute.ConstructorArguments[1].Value as string))
                .Where(entry => entry.Key?.StartsWith(KeyPrefix, StringComparison.Ordinal) == true && entry.Id is not null)
                .ToLookup(entry => entry.Key!, entry => entry.Id!, StringComparer.Ordinal);
            imports.Add(assembly, catalog);
        }
        return catalog;
    }

    internal void AppendExports(StringBuilder source, CancellationToken token)
    {
        var members = ValidatorRegistrationGenerator.EnumerateTypes(compilation.Assembly.GlobalNamespace)
            .Where(IsPublishedType).SelectMany(type => type.GetMembers()).Where(IsPublishedMember)
            .OrderBy(member => member.GetDocumentationCommentId(), StringComparer.Ordinal);
        foreach (var member in members)
        {
            token.ThrowIfCancellationRequested();
            var discovery = new OptionTypeDiscovery(compilation, this, collectTemplates: true, token);
            var types = discovery.DiscoverTemplates(member).Select(DocumentationCommentId.CreateReferenceId)
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
            foreach (var id in types)
            {
                source.Append("[assembly: global::System.Reflection.AssemblyMetadata(")
                    .Append(SymbolDisplay.FormatLiteral(KeyPrefix + member.GetDocumentationCommentId(), true)).Append(", ")
                    .Append(SymbolDisplay.FormatLiteral(id, true)).Append(")]\n");
            }
        }
    }

    private static bool IsPublishedMember(ISymbol member)
        => member switch
        {
            IMethodSymbol method => (method.IsGenericMethod || method.ContainingType.IsGenericType) &&
                (method.DeclaredAccessibility != Accessibility.Private || !method.ExplicitInterfaceImplementations.IsEmpty) &&
                (method.DeclaringSyntaxReferences.Length != 0 ||
                 method is { MethodKind: MethodKind.Constructor, IsImplicitlyDeclared: true, Parameters.IsEmpty: true }),
            IFieldSymbol field => field.ContainingType.IsGenericType && field.DeclaringSyntaxReferences.Length != 0 &&
                field.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal,
            _ => false,
        };

    private static bool IsPublishedType(INamedTypeSymbol? type)
        => type is null || !type.IsFileLocal && type.DeclaredAccessibility != Accessibility.Private && IsPublishedType(type.ContainingType);
}
