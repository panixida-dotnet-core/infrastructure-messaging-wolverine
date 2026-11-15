using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal sealed class OptionTypeDiscovery(Compilation compilation, CancellationToken token)
{
    private readonly HashSet<INamedTypeSymbol> types = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> visited = new(StringComparer.Ordinal);
    private readonly HashSet<ISymbol> active = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> activeInstanceInitializers = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, MethodDispatch> methodDispatches = new(SymbolEqualityComparer.Default);

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
        foreach (var node in root.DescendantNodes().Where(node => node is GenericNameSyntax or InvocationExpressionSyntax or
            BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax or
            MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or ElementAccessExpressionSyntax or ElementBindingExpressionSyntax ||
            substitutions.Count != 0 && node is IdentifierNameSyntax))
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
            else if (symbol is IPropertySymbol property)
            {
                VisitAccessors(property.GetMethod, property.SetMethod, substitutions);
            }
            else if (symbol is IEventSymbol @event)
            {
                VisitAccessors(@event.AddMethod, @event.RemoveMethod, substitutions);
            }
            else if (symbol is IFieldSymbol { ContainingType.IsGenericType: true } field)
            {
                var containing = field.ContainingType;
                var arguments = ContainingArguments(containing, substitutions);
                VisitDefinition(field.OriginalDefinition, arguments);
                VisitStaticInitialization(containing, arguments);
            }
        }
    }

    private void VisitAccessors(IMethodSymbol? first, IMethodSymbol? second, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        foreach (var accessor in new[] { first, second }.OfType<IMethodSymbol>())
        {
            VisitMethod(accessor, substitutions);
        }
    }

    private void VisitMethod(IMethodSymbol method, Dictionary<ITypeParameterSymbol, ITypeSymbol> inherited)
    {
        var containing = method.ContainingType!;
        var substitutions = ContainingArguments(containing, inherited);
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        var implementation = definition.PartialImplementationPart ?? definition;
        for (var index = 0; index < method.TypeArguments.Length; index++)
        {
            var argument = Substitute(method.TypeArguments[index], inherited);
            substitutions[definition.TypeParameters[index]] = argument;
            substitutions[implementation.TypeParameters[index]] = argument;
            AddType(argument);
        }
        AddType(Substitute(method.ReturnType, substitutions));
        VisitDefinition(DispatchDefinition(definition), substitutions);
        if (method.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove &&
            (containing.TypeKind == TypeKind.Interface || method.IsVirtual || method.IsAbstract || method.IsOverride))
        {
            VisitDispatchCall(method, substitutions);
        }
        if (containing.IsGenericType)
        {
            VisitStaticInitialization(containing, substitutions);
            if (method.MethodKind == MethodKind.Constructor)
            {
                VisitDefinition(containing.OriginalDefinition, substitutions, instanceInitializers: true);
            }
        }
        if (method.MethodKind == MethodKind.Constructor)
        {
            AddType(Substitute(containing, substitutions));
            VisitImplicitBaseConstructor(implementation, containing, substitutions);
        }
    }

    private void VisitImplicitBaseConstructor(IMethodSymbol method, INamedTypeSymbol containing,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        var baseType = containing.BaseType;
        if (baseType is null || baseType.DeclaringSyntaxReferences.Length == 0 ||
            method.OriginalDefinition.DeclaringSyntaxReferences.SelectMany(reference => reference.GetSyntax(token).ChildNodes())
                .Any(IsExplicitConstructorInitializer))
        {
            return;
        }

        var candidates = baseType.InstanceConstructors
            .Where(constructor => constructor.Parameters.All(parameter => parameter.IsOptional || parameter.IsParams) &&
                compilation.IsSymbolAccessibleWithin(constructor, containing))
            .OrderBy(constructor => constructor.Parameters.Any(parameter => parameter.IsParams))
            .ThenBy(constructor => constructor.Parameters.Length);
        foreach (var constructor in candidates.Take(1))
        {
            VisitMethod(constructor, substitutions);
        }
    }

    private static bool IsExplicitConstructorInitializer(SyntaxNode node)
        => node is ConstructorInitializerSyntax ||
           node is BaseListSyntax baseList && baseList.Types.OfType<PrimaryConstructorBaseTypeSyntax>().Any();

    private void VisitStaticInitialization(INamedTypeSymbol type, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        foreach (var initializer in type.OriginalDefinition.StaticConstructors)
        {
            VisitDefinition(initializer, substitutions);
        }
        VisitDefinition(type.OriginalDefinition, substitutions);
    }

    private Dictionary<ITypeParameterSymbol, ITypeSymbol> ContainingArguments(INamedTypeSymbol? type,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> inherited)
    {
        var substitutions = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(inherited, SymbolEqualityComparer.Default);
        for (var containing = type; containing is not null; containing = containing.ContainingType)
        {
            for (var index = 0; index < containing.TypeArguments.Length; index++)
            {
                substitutions[containing.OriginalDefinition.TypeParameters[index]] = Substitute(containing.TypeArguments[index], inherited);
            }
        }
        return substitutions;
    }

    private void VisitDefinition(ISymbol definition, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions, bool instanceInitializers = false)
    {
        var activeDefinitions = instanceInitializers ? activeInstanceInitializers : active;
        if (definition.DeclaringSyntaxReferences.Length == 0 || substitutions.Values.Any(type => ContainsTypeParameter(type)) || activeDefinitions.Contains(definition))
        {
            return;
        }

        var key = definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + "|" + instanceInitializers + "|" + string.Join(";", substitutions
            .Select(pair => pair.Key.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + "." + pair.Key.Name + "=" + pair.Value.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .OrderBy(value => value, StringComparer.Ordinal));
        if (!visited.Add(key))
        {
            return;
        }

        activeDefinitions.Add(definition);
        if (definition is INamedTypeSymbol typeDefinition)
        {
            VisitMemberInitializers(typeDefinition, substitutions, instanceInitializers);
        }
        else
        {
            foreach (var reference in definition.DeclaringSyntaxReferences)
            {
                Visit(reference.GetSyntax(token), substitutions);
            }
        }
        activeDefinitions.Remove(definition);
    }

    private void VisitMemberInitializers(INamedTypeSymbol type, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions, bool instanceInitializers)
    {
        var initializers = type.GetMembers()
            .Where(member => member is IFieldSymbol or IPropertySymbol && member.IsStatic != instanceInitializers)
            .SelectMany(member => member.DeclaringSyntaxReferences)
            .SelectMany(reference => reference.GetSyntax(token).ChildNodes().OfType<EqualsValueClauseSyntax>());
        foreach (var initializer in initializers)
        {
            Visit(initializer, substitutions);
        }
    }

    private void AddType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsAbstract: false, IsUnboundGenericType: false } named || ContainsTypeParameter(named) ||
            !types.Add(named) || named.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        foreach (var contract in DispatchContracts(named).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            var dispatch = GetDispatch(contract);
            dispatch.Implementations.Add(named);
            foreach (var call in dispatch.Calls.ToArray())
            {
                VisitImplementations(named, call);
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> DispatchContracts(INamedTypeSymbol type)
    {
        foreach (var contract in type.AllInterfaces)
        {
            yield return contract.OriginalDefinition;
        }
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            yield return baseType;
        }
    }

    private void VisitDispatchCall(IMethodSymbol method, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        var contract = (INamedTypeSymbol)Substitute(method.ContainingType!, substitutions);
        var arguments = method.TypeArguments.Select(argument => Substitute(argument, substitutions)).ToArray();
        if (ContainsTypeParameter(contract) || arguments.Any(argument => ContainsTypeParameter(argument)))
        {
            return;
        }

        var declaration = FindDeclaration(contract, method);
        var call = arguments.Length == 0 ? declaration : declaration.Construct(arguments);
        var dispatch = GetDispatch(contract.TypeKind == TypeKind.Interface ? contract.OriginalDefinition : contract);
        if (dispatch.Calls.Add(call))
        {
            foreach (var implementation in dispatch.Implementations.ToArray())
            {
                VisitImplementations(implementation, call);
            }
        }
    }

    private static IMethodSymbol FindDeclaration(INamedTypeSymbol type, IMethodSymbol method)
        => type.GetMembers(method.Name).OfType<IMethodSymbol>()
            .First(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, method.OriginalDefinition));

    private MethodDispatch GetDispatch(INamedTypeSymbol contract)
    {
        if (!methodDispatches.TryGetValue(contract, out var dispatch))
        {
            dispatch = new MethodDispatch();
            methodDispatches.Add(contract, dispatch);
        }
        return dispatch;
    }

    private void VisitImplementations(INamedTypeSymbol type, IMethodSymbol call)
    {
        foreach (var implementation in FindImplementations(type, call).Where(implementation => !active.Contains(DispatchDefinition(implementation.OriginalDefinition))))
        {
            var method = call.Arity == 0 ? implementation : implementation.Construct([.. call.TypeArguments]);
            VisitMethod(method, new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default));
        }
    }

    private static ISymbol DispatchDefinition(IMethodSymbol method) => method.AssociatedSymbol switch
    {
        IPropertySymbol property => property.PartialImplementationPart ?? property,
        IEventSymbol @event => @event.PartialImplementationPart ?? @event,
        _ => method.PartialImplementationPart ?? method,
    };

    private IEnumerable<IMethodSymbol> FindImplementations(INamedTypeSymbol type, IMethodSymbol call)
    {
        var declaringType = call.ContainingType!;
        if (declaringType.TypeKind == TypeKind.Interface)
        {
            return type.AllInterfaces
                .Where(contract => SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, declaringType.OriginalDefinition) &&
                    compilation.ClassifyCommonConversion(contract, declaringType).IsImplicit)
                .Select(contract => type.FindImplementationForInterfaceMember(FindDeclaration(contract, call)))
                .OfType<IMethodSymbol>();
        }

        for (var current = type; !SymbolEqualityComparer.Default.Equals(current, declaringType); current = current.BaseType!)
        {
            var implementation = current.GetMembers(call.Name).OfType<IMethodSymbol>()
                .FirstOrDefault(candidate => Overrides(candidate, call));
            if (implementation is not null)
            {
                return [implementation];
            }
        }
        return [];
    }

    private static bool Overrides(IMethodSymbol candidate, IMethodSymbol call)
    {
        for (var overridden = candidate.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(overridden, call.ConstructedFrom))
            {
                return true;
            }
        }
        return false;
    }

    private sealed class MethodDispatch
    {
        internal HashSet<IMethodSymbol> Calls { get; } = new(SymbolEqualityComparer.Default);
        internal HashSet<INamedTypeSymbol> Implementations { get; } = new(SymbolEqualityComparer.Default);
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
        return named.Arity == 0 ? definition : definition.Construct([.. named.TypeArguments.Select(argument => Substitute(argument, substitutions))]);
    }

    internal static bool ContainsTypeParameter(ITypeSymbol type)
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
