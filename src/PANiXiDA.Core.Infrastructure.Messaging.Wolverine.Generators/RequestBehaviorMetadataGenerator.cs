using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators;

/// <summary>
/// Generates the type and constructor metadata used by Wolverine request behavior policies.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RequestBehaviorMetadataGenerator : IIncrementalGenerator
{
    private const string ApplicationAssembly = "PANiXiDA.Core.Application";
    private const string AdapterAssembly = "PANiXiDA.Core.Infrastructure.Messaging.Wolverine";
    private const string ContractNamespace = "PANiXiDA.Core.Application.Messaging.Mediator.Behaviors.Abstractions.";
    private const string ResultName = "PANiXiDA.Core.ResultPattern.Result";
    private static readonly string[] contractNames =
    [
        ContractNamespace + "IBeforeRequestBehavior`2",
        ContractNamespace + "IAfterRequestBehavior`2",
        ContractNamespace + "IFinallyRequestBehavior`2"
    ];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var referencedTypes = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is TypeOfExpressionSyntax or GenericNameSyntax,
            static (syntaxContext, token) => GetReferencedTypes(syntaxContext, token)).Collect();
        var source = context.CompilationProvider.Combine(referencedTypes)
            .Select(static (input, token) => Generate((CSharpCompilation)input.Left, input.Right, token))
            .WithComparer(StringComparer.Ordinal);

        context.RegisterSourceOutput(source, static (output, text) =>
        {
            if (text.Length > 0)
            {
                output.AddSource("RequestBehaviorMetadata.g.cs", text);
            }
        });
    }

    private static ImmutableArray<INamedTypeSymbol> GetReferencedTypes(GeneratorSyntaxContext context, CancellationToken token)
    {
        if (context.Node is TypeOfExpressionSyntax typeOf)
        {
            return context.SemanticModel.GetTypeInfo(typeOf.Type, token).Type is INamedTypeSymbol type
                ? [type] : [];
        }

        return context.SemanticModel.GetSymbolInfo(context.Node, token).Symbol switch
        {
            INamedTypeSymbol type => [type],
            IMethodSymbol method => [.. method.TypeArguments.OfType<INamedTypeSymbol>()],
            _ => []
        };
    }

    private static string Generate(CSharpCompilation compilation, ImmutableArray<ImmutableArray<INamedTypeSymbol>> referencedTypes, CancellationToken token)
    {
        if (compilation.GetTypeByMetadataName(AdapterAssembly + ".Generation.RequestBehaviorMetadata") is null)
        {
            return string.Empty;
        }

        var contracts = contractNames.Select(compilation.GetTypeByMetadataName).OfType<INamedTypeSymbol>().ToArray();
        var requestContract = compilation.GetTypeByMetadataName("PANiXiDA.Core.Application.Messaging.Mediator.Contracts.IRequest`1");
        var resultBase = compilation.GetTypeByMetadataName(ResultName);
        if (contracts.Length != 3 || requestContract is null || resultBase is null)
        {
            return string.Empty;
        }

        var types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols
            .Where(assembly => assembly.Name == ApplicationAssembly || assembly.Name == AdapterAssembly ||
                assembly.Modules.Any(module => module.ReferencedAssemblySymbols.Any(reference =>
                    reference.Name == ApplicationAssembly || reference.Name == AdapterAssembly)))
            .Concat([compilation.Assembly]).ToArray();

        foreach (var assembly in assemblies)
        {
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                token.ThrowIfCancellationRequested();
                if (CanReference(compilation, type))
                {
                    types.Add(type);
                }
            }
        }

        var explicitlyReferenced = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var referencedType in referencedTypes.SelectMany(item => item))
        {
            var type = referencedType.IsUnboundGenericType ? referencedType.OriginalDefinition : referencedType;
            if (CanReference(compilation, type) && (!HasTypeParameter(type) || SymbolEqualityComparer.Default.Equals(type, type.OriginalDefinition)))
            {
                types.Add(type);
                if (assemblies.Any(assembly => SymbolEqualityComparer.Default.Equals(assembly, type.ContainingAssembly)))
                {
                    explicitlyReferenced.Add(type);
                }
            }
        }

        var behaviors = types.Where(type => type.AllInterfaces.Any(contract => IsContract(contract, contracts)) ||
                explicitlyReferenced.Contains(type))
            .OrderBy(TypeName, StringComparer.Ordinal).ToArray();
        var pairs = new HashSet<(INamedTypeSymbol Request, INamedTypeSymbol Result)>(new RequestPairComparer());
        foreach (var type in types.Where(type => !HasTypeParameter(type) && !type.IsUnboundGenericType))
        {
            foreach (var contract in type.AllInterfaces.Where(item => SymbolEqualityComparer.Default.Equals(item.OriginalDefinition, requestContract)))
            {
                if (contract.TypeArguments[0] is INamedTypeSymbol result && IsAssignable(compilation, result, resultBase))
                {
                    pairs.Add((type, result));
                }
            }

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(method => !method.IsGenericMethod))
            {
                foreach (var request in method.Parameters.Select(parameter => parameter.Type).OfType<INamedTypeSymbol>()
                             .Where(parameter => parameter.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, requestContract))))
                {
                    foreach (var result in ReturnResults(method.ReturnType, compilation, resultBase))
                    {
                        pairs.Add((request, result));
                    }
                }
            }
        }

        var registrations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var behavior in behaviors)
        {
            AddBehavior(registrations, behavior, contracts);
        }

        foreach (var pair in pairs.OrderBy(pair => TypeName(pair.Request), StringComparer.Ordinal).ThenBy(pair => TypeName(pair.Result), StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (!CanReference(compilation, pair.Request) || !CanReference(compilation, pair.Result))
            {
                continue;
            }

            foreach (var behavior in behaviors)
            {
                var closed = Close(compilation, behavior, pair.Request, pair.Result);
                foreach (var contract in contracts.Where(contract => behavior.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item.OriginalDefinition, contract))))
                {
                    var supported = closed is not null && closed.AllInterfaces.Any(item =>
                        SymbolEqualityComparer.Default.Equals(item.OriginalDefinition, contract) &&
                        IsAssignable(compilation, pair.Request, item.TypeArguments[0]) &&
                        IsAssignable(compilation, pair.Result, item.TypeArguments[1]));
                    if (supported)
                    {
                        AddBehavior(registrations, closed!, contracts);
                    }

                    var closedArgument = supported ? $"typeof({TypeName(closed!)})" : "null";
                    registrations.Add($"RegisterBinding(typeof({TypeName(behavior)}), typeof({TypeName(pair.Request)}), typeof({TypeName(pair.Result)}), typeof({TypeName(contract)}), {closedArgument});");
                }
            }
        }

        if (registrations.Count == 0)
        {
            return string.Empty;
        }

        var source = new StringBuilder("#nullable enable\nnamespace PANiXiDA.Generated;\ninternal static class WolverineRequestBehaviors\n{\n    [global::System.Runtime.CompilerServices.ModuleInitializer]\n    internal static void Register()\n    {\n");
        foreach (var registration in registrations)
        {
            source.Append("        global::").Append(AdapterAssembly).Append(".Generation.RequestBehaviorMetadata.").Append(registration).Append('\n');
        }

        return source.Append("    }\n}\n").ToString();
    }

    private static void AddBehavior(SortedSet<string> registrations, INamedTypeSymbol type, INamedTypeSymbol[] contracts)
    {
        var constructors = type.InstanceConstructors.Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public).ToArray();
        var supported = type.AllInterfaces.Where(contract => IsContract(contract, contracts))
            .Select(contract => $"typeof({TypeName(contract.OriginalDefinition)})").Distinct().OrderBy(name => name, StringComparer.Ordinal);
        var parameters = constructors.Length == 1 && !HasTypeParameter(type) && !type.IsUnboundGenericType
            ? string.Join(", ", constructors[0].Parameters.Select(parameter => $"typeof({TypeName(parameter.Type)})"))
            : string.Empty;
        registrations.Add($"RegisterBehavior(typeof({TypeName(type)}), {constructors.Length}, new global::System.Type[] {{ {string.Join(", ", supported)} }}, new global::System.Type[] {{ {parameters} }});");
    }

    private static INamedTypeSymbol? Close(CSharpCompilation compilation, INamedTypeSymbol behavior, INamedTypeSymbol request, INamedTypeSymbol result)
    {
        if (!behavior.IsGenericType)
        {
            return behavior;
        }

        if (!HasTypeParameter(behavior) && !behavior.IsUnboundGenericType)
        {
            return behavior;
        }

        var definition = behavior.OriginalDefinition;
        if (definition.Arity != 2)
        {
            return null;
        }

        INamedTypeSymbol[] arguments = [request, result];
        for (var i = 0; i < 2; i++)
        {
            var parameter = definition.TypeParameters[i];
            var argument = arguments[i];
            if ((parameter.HasReferenceTypeConstraint && !argument.IsReferenceType) ||
                (parameter.HasValueTypeConstraint && !argument.IsValueType) ||
                (parameter.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType) ||
                (parameter.HasConstructorConstraint && !argument.IsValueType &&
                    (argument.IsAbstract ||
                        !argument.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public))))
            {
                return null;
            }

            foreach (var constraint in parameter.ConstraintTypes)
            {
                if (!IsAssignable(compilation, argument, Substitute(constraint, definition, arguments)))
                {
                    return null;
                }
            }
        }

        return definition.Construct(arguments);
    }

    private static ITypeSymbol Substitute(ITypeSymbol type, INamedTypeSymbol definition, ITypeSymbol[] arguments)
    {
        if (type is ITypeParameterSymbol parameter && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, definition))
        {
            return arguments[parameter.Ordinal];
        }

        return type is INamedTypeSymbol { IsGenericType: true } named
            ? named.ConstructedFrom.Construct(named.TypeArguments.Select(argument => Substitute(argument, definition, arguments)).ToArray())
            : type;
    }

    private static IEnumerable<INamedTypeSymbol> ReturnResults(ITypeSymbol type, CSharpCompilation compilation, INamedTypeSymbol resultBase)
    {
        if (type is not INamedTypeSymbol named)
        {
            yield break;
        }

        if (IsAssignable(compilation, named, resultBase))
        {
            yield return named;
        }
        else if (named.IsTupleType || named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
        {
            foreach (var result in named.TypeArguments.SelectMany(argument => ReturnResults(argument, compilation, resultBase)))
            {
                yield return result;
            }
        }
    }

    private static bool IsContract(INamedTypeSymbol type, INamedTypeSymbol[] contracts)
    {
        return contracts.Any(contract => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, contract));
    }

    private static bool IsAssignable(CSharpCompilation compilation, ITypeSymbol source, ITypeSymbol destination)
    {
        var conversion = compilation.ClassifyConversion(source, destination);
        return conversion.IsIdentity || conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing);
    }

    private static bool CanReference(Compilation compilation, INamedTypeSymbol type)
    {
        return !type.IsFileLocal && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) &&
            type.TypeKind != TypeKind.Error && type.ContainingType is not { IsGenericType: true };
    }

    private static bool HasTypeParameter(ITypeSymbol type)
    {
        return type is ITypeParameterSymbol || type is INamedTypeSymbol named && named.TypeArguments.Any(HasTypeParameter);
    }

    private static string TypeName(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType && HasTypeParameter(named))
        {
            type = named.OriginalDefinition.ConstructUnboundGenericType();
        }

        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers));
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol parent)
    {
        foreach (var member in parent.GetMembers())
        {
            if (member is INamespaceSymbol space)
            {
                foreach (var type in EnumerateTypes(space))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in EnumerateTypes(type))
                {
                    yield return nested;
                }
            }
        }
    }

    private sealed class RequestPairComparer : IEqualityComparer<(INamedTypeSymbol Request, INamedTypeSymbol Result)>
    {
        public bool Equals((INamedTypeSymbol Request, INamedTypeSymbol Result) x, (INamedTypeSymbol Request, INamedTypeSymbol Result) y)
        {
            return SymbolEqualityComparer.Default.Equals(x.Request, y.Request) && SymbolEqualityComparer.Default.Equals(x.Result, y.Result);
        }

        public int GetHashCode((INamedTypeSymbol Request, INamedTypeSymbol Result) value)
        {
            return SymbolEqualityComparer.Default.GetHashCode(value.Request) * 397 ^ SymbolEqualityComparer.Default.GetHashCode(value.Result);
        }
    }
}
