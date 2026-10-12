using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal sealed class OptionTypeDiscovery(Compilation compilation, CancellationToken token)
{
    private readonly HashSet<INamedTypeSymbol> types = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> visited = new(StringComparer.Ordinal);
    private readonly HashSet<IMethodSymbol> active = new(SymbolEqualityComparer.Default);

    internal IEnumerable<INamedTypeSymbol> Discover()
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            Visit(tree.GetRoot(token), new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default));
        }
        return types;
    }

    private void Visit(SyntaxNode root, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        var model = compilation.GetSemanticModel(root.SyntaxTree);
        foreach (var node in root.DescendantNodes().Where(node => node is GenericNameSyntax or InvocationExpressionSyntax))
        {
            token.ThrowIfCancellationRequested();
            var symbol = model.GetSymbolInfo(node, token).Symbol;
            if (symbol is INamedTypeSymbol type)
            {
                AddType(Substitute(type, substitutions));
            }
            else if (symbol is IMethodSymbol method)
            {
                VisitMethod(method, substitutions);
            }
        }
    }

    private void VisitMethod(IMethodSymbol method, Dictionary<ITypeParameterSymbol, ITypeSymbol> inherited)
    {
        var substitutions = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(inherited, SymbolEqualityComparer.Default);
        for (var containing = method.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            for (var index = 0; index < containing.TypeArguments.Length; index++)
            {
                substitutions[containing.OriginalDefinition.TypeParameters[index]] = Substitute(containing.TypeArguments[index], inherited);
            }
        }
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        for (var index = 0; index < method.TypeArguments.Length; index++)
        {
            var argument = Substitute(method.TypeArguments[index], inherited);
            substitutions[definition.TypeParameters[index]] = argument;
            AddType(argument);
        }
        AddType(Substitute(method.ReturnType, substitutions));

        if (definition.DeclaringSyntaxReferences.Length == 0 || substitutions.Values.Any(ContainsTypeParameter) || active.Contains(definition))
        {
            return;
        }

        var key = definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + "|" + string.Join(";", substitutions
            .Select(pair => pair.Key.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + "." + pair.Key.Name + "=" + pair.Value.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .OrderBy(value => value, StringComparer.Ordinal));
        if (!visited.Add(key))
        {
            return;
        }

        active.Add(definition);
        foreach (var reference in definition.DeclaringSyntaxReferences)
        {
            Visit(reference.GetSyntax(token), substitutions);
        }
        active.Remove(definition);
    }

    private void AddType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsAbstract: false, IsUnboundGenericType: false } named && !ContainsTypeParameter(named))
        {
            types.Add(named);
        }
    }

    private ITypeSymbol Substitute(ITypeSymbol type, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        if (type is ITypeParameterSymbol parameter)
        {
            return substitutions.TryGetValue(parameter, out var argument) ? argument : parameter;
        }
        if (type is IArrayTypeSymbol array)
        {
            return compilation.CreateArrayTypeSymbol(Substitute(array.ElementType, substitutions), array.Rank);
        }
        if (type is not INamedTypeSymbol named || !ContainsTypeParameter(named))
        {
            return type;
        }

        var definition = named.ConstructedFrom;
        if (named.ContainingType is not null)
        {
            var containing = (INamedTypeSymbol)Substitute(named.ContainingType, substitutions);
            definition = containing.GetTypeMembers(named.Name, named.Arity)
                .First(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, named.OriginalDefinition));
        }
        return named.Arity == 0 ? definition : definition.Construct(named.TypeArguments.Select(argument => Substitute(argument, substitutions)).ToArray());
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        return type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter) ||
                named.ContainingType is not null && ContainsTypeParameter(named.ContainingType),
            _ => false,
        };
    }
}
