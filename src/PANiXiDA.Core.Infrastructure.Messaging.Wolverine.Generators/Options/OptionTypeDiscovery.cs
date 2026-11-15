using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

using TypeSubstitutions = System.Collections.Generic.Dictionary<Microsoft.CodeAnalysis.ITypeParameterSymbol, Microsoft.CodeAnalysis.ITypeSymbol>;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal sealed class OptionTypeDiscovery(Compilation compilation, CancellationToken token, GenericOptionTemplates templates, bool collectTemplates = false)
{
    private readonly HashSet<INamedTypeSymbol> types = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> optionTemplates = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> visited = new(StringComparer.Ordinal);
    private readonly RecursionGuard active = new();
    private readonly RecursionGuard activeMethods = new();
    private readonly RecursionGuard activeInstanceInitializers = new();
    private readonly Dictionary<INamedTypeSymbol, MethodDispatch> methodDispatches = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(SyntaxTree Tree, int Start, int Length), IInvocationOperation[]> disposalCalls = [];
    private readonly INamedTypeSymbol? notifyCompletion = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.INotifyCompletion");
    private readonly INamedTypeSymbol? criticalNotifyCompletion = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ICriticalNotifyCompletion");

    internal bool HasOptionTemplates => optionTemplates.Count != 0;

    internal IEnumerable<INamedTypeSymbol> Discover()
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            Visit(tree.GetRoot(token), new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default));
        }
        return types;
    }

    internal IEnumerable<INamedTypeSymbol> DiscoverTemplates(IMethodSymbol method)
    {
        VisitMethod(method, new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default));
        return optionTemplates;
    }

    private void Visit(SyntaxNode root, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        var model = templates.GetModel(root.SyntaxTree);
        foreach (var node in root.DescendantNodes())
        {
            token.ThrowIfCancellationRequested();
            VisitImplicitCalls(model, node, substitutions);
            if (!IsSymbolReference(node, substitutions.Count != 0))
            {
                continue;
            }
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
                VisitMethods(substitutions, property.GetMethod, property.SetMethod);
            }
            else if (symbol is IEventSymbol @event)
            {
                VisitMethods(substitutions, @event.AddMethod, @event.RemoveMethod);
            }
            else if (symbol is IFieldSymbol { ContainingType.IsGenericType: true } field)
            {
                VisitGenericField(field, substitutions);
            }
        }
    }

    private static bool IsSymbolReference(SyntaxNode node, bool hasSubstitutions)
        => node is GenericNameSyntax or InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax or
            PrimaryConstructorBaseTypeSyntax or MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or ElementAccessExpressionSyntax or
            ElementBindingExpressionSyntax || hasSubstitutions && node is IdentifierNameSyntax;

    private void VisitImplicitCalls(SemanticModel model, SyntaxNode node, TypeSubstitutions substitutions)
    {
        if (node is ExpressionSyntax expression)
        {
            VisitOperators(model, expression, substitutions);
        }

        switch (node)
        {
            case InitializerExpressionSyntax initializer when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                VisitCollectionInitializer(model, initializer, substitutions);
                break;
            case CommonForEachStatementSyntax enumeration:
                VisitEnumeration(model, enumeration, substitutions);
                break;
            case CollectionExpressionSyntax collection:
                VisitCollectionExpression(model, collection, substitutions);
                break;
            case QueryExpressionSyntax query:
                VisitQuery(model, query, substitutions);
                break;
            case AssignmentExpressionSyntax { Left: DeclarationExpressionSyntax or TupleExpressionSyntax } assignment:
                VisitDeconstruction(model.GetDeconstructionInfo(assignment), substitutions);
                break;
            case PatternSyntax pattern when pattern.Parent is not (PatternSyntax or SubpatternSyntax):
                VisitPattern(model, pattern, substitutions);
                break;
            case AwaitExpressionSyntax awaitExpression:
                VisitAwait(model.GetAwaitExpressionInfo(awaitExpression), substitutions);
                break;
            case UsingStatementSyntax:
            case LocalDeclarationStatementSyntax declaration when declaration.UsingKeyword.RawKind != 0:
                VisitDisposals(model, node, substitutions);
                break;
        }
    }

    private void VisitEnumeration(SemanticModel model, CommonForEachStatementSyntax enumeration, TypeSubstitutions substitutions)
    {
        var iteration = model.GetForEachStatementInfo(enumeration);
        VisitIteration(iteration, substitutions);
        if (enumeration.AwaitKeyword.RawKind != 0)
        {
            VisitImplicitAwaits(model, enumeration.SpanStart, substitutions, iteration.MoveNextMethod, iteration.DisposeMethod);
        }
        if (enumeration is ForEachVariableStatementSyntax deconstruction)
        {
            VisitDeconstruction(model.GetDeconstructionInfo(deconstruction), substitutions);
        }
    }

    private void VisitQuery(SemanticModel model, QueryExpressionSyntax query, TypeSubstitutions substitutions)
    {
        IOperation?[] operations = [model.GetOperation(query, token)];
        var calls = operations.OfType<ITranslatedQueryOperation>()
            .SelectMany(operation => operation.Operation.DescendantsAndSelf()).OfType<IInvocationOperation>();
        foreach (var call in calls)
        {
            VisitMethod(call.TargetMethod, substitutions);
        }
    }

    private void VisitCollectionExpression(SemanticModel model, CollectionExpressionSyntax collection, TypeSubstitutions substitutions)
    {
        IOperation?[] operations = [model.GetOperation(collection, token)];
        foreach (var operation in operations.OfType<ICollectionExpressionOperation>())
        {
            VisitMethods(substitutions, operation.ConstructMethod);
            foreach (var spread in operation.Elements.OfType<ISpreadOperation>())
            {
                VisitMethods(substitutions, spread.ElementConversion.MethodSymbol);
                VisitIteration(BindSpreadIteration(model, (SpreadElementSyntax)spread.Syntax), substitutions);
            }
        }
    }

    private static ForEachStatementInfo BindSpreadIteration(SemanticModel model, SpreadElementSyntax spread)
    {
        // ISpreadOperation exposes the item conversion but not the enumeration members.
        // Bind a foreach at the same location so Roslyn selects the same pattern and extension methods.
        var loop = SyntaxFactory.ForEachStatement(SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.Identifier("__wolverineSpreadItem"), spread.Expression.WithoutTrivia(), SyntaxFactory.Block());
        if (model.TryGetSpeculativeSemanticModel(spread.SpanStart, loop, out var body))
        {
            return body.GetForEachStatementInfo(loop);
        }

        // Field and auto-property initializers use a separate semantic model.
        var lambda = SyntaxFactory.ParenthesizedLambdaExpression(SyntaxFactory.Block(loop));
        var cast = SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName("global::System.Action"),
            SyntaxFactory.ParenthesizedExpression(lambda));
        var initializer = SyntaxFactory.EqualsValueClause(cast);
        return model.TryGetSpeculativeSemanticModel(spread.SpanStart, initializer, out var field)
            ? field.GetForEachStatementInfo(initializer.DescendantNodes().OfType<ForEachStatementSyntax>().First())
            : default;
    }

    private void VisitIteration(ForEachStatementInfo iteration, TypeSubstitutions substitutions)
        => VisitMethods(substitutions, iteration.GetEnumeratorMethod, iteration.MoveNextMethod, iteration.CurrentProperty?.GetMethod,
            iteration.DisposeMethod, iteration.CurrentConversion.MethodSymbol, iteration.ElementConversion.MethodSymbol);

    private void VisitImplicitAwaits(SemanticModel model, int position, TypeSubstitutions substitutions, params IMethodSymbol?[] methods)
    {
        foreach (var method in methods.OfType<IMethodSymbol>())
        {
            // Reuse Roslyn's await binding, including extension-method lookup at the original location.
            var type = SyntaxFactory.ParseTypeName(method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            var statement = SyntaxFactory.ExpressionStatement(SyntaxFactory.AwaitExpression(SyntaxFactory.DefaultExpression(type)));
            model.TryGetSpeculativeSemanticModel(position, statement, out var speculative);
            SemanticModel?[] bindings = [speculative];
            foreach (var binding in bindings.OfType<SemanticModel>())
            {
                VisitAwait(binding.GetAwaitExpressionInfo((AwaitExpressionSyntax)statement.Expression), substitutions);
            }
        }
    }

    private void VisitAwait(AwaitExpressionInfo awaiter, TypeSubstitutions substitutions)
    {
        VisitMethods(substitutions, awaiter.GetAwaiterMethod, awaiter.IsCompletedProperty?.GetMethod, awaiter.GetResultMethod);
        ITypeSymbol?[] returnTypes = [awaiter.GetAwaiterMethod?.ReturnType];
        foreach (var declaredType in returnTypes.OfType<ITypeSymbol>())
        {
            if (Substitute(declaredType, substitutions) is INamedTypeSymbol closedType)
            {
                VisitAwaitContinuation(declaredType, closedType, substitutions);
            }
        }
    }

    private void VisitAwaitContinuation(ITypeSymbol declaredType, INamedTypeSymbol awaiter, TypeSubstitutions substitutions)
    {
        INamedTypeSymbol?[] contracts =
        [
            declaredType.AllInterfaces.Any(type => SymbolEqualityComparer.Default.Equals(type, criticalNotifyCompletion))
                ? criticalNotifyCompletion : notifyCompletion,
        ];
        var methods = contracts.OfType<INamedTypeSymbol>().SelectMany(type => type.GetMembers()).OfType<IMethodSymbol>();
        foreach (var method in methods)
        {
            if (awaiter.TypeKind == TypeKind.Interface)
            {
                TrackDispatch(awaiter.OriginalDefinition, method);
            }
            VisitMethods(substitutions, awaiter.FindImplementationForInterfaceMember(method) as IMethodSymbol);
        }
    }

    private void VisitDeconstruction(DeconstructionInfo deconstruction, TypeSubstitutions substitutions)
    {
        VisitMethods(substitutions, deconstruction.Method, deconstruction.Conversion?.MethodSymbol);
        foreach (var nested in deconstruction.Nested.AsSpan())
        {
            VisitDeconstruction(nested, substitutions);
        }
    }

    private void VisitPattern(SemanticModel model, PatternSyntax pattern, TypeSubstitutions substitutions)
    {
        while (pattern is ParenthesizedPatternSyntax parenthesized)
        {
            pattern = parenthesized.Pattern;
        }
        IOperation?[] roots = [model.GetOperation(pattern, token)];
        var operations = roots.OfType<IOperation>().SelectMany(operation => operation.DescendantsAndSelf());
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case IRecursivePatternOperation recursive:
                    VisitPatternMember(recursive.DeconstructSymbol, substitutions);
                    break;
                case IPropertySubpatternOperation subpattern:
                    foreach (var member in subpattern.Member.DescendantsAndSelf().OfType<IMemberReferenceOperation>())
                    {
                        VisitPatternMember(member.Member, substitutions);
                    }
                    break;
                case IListPatternOperation list:
                    VisitPatternMember(list.LengthSymbol, substitutions);
                    VisitPatternMember(list.IndexerSymbol, substitutions);
                    break;
                case ISlicePatternOperation slice:
                    VisitPatternMember(slice.SliceSymbol, substitutions);
                    break;
            }
        }
    }

    private void VisitPatternMember(ISymbol? symbol, TypeSubstitutions substitutions)
    {
        switch (symbol)
        {
            case IMethodSymbol method:
                VisitMethod(method, substitutions);
                break;
            case IPropertySymbol property:
                VisitMethods(substitutions, property.GetMethod);
                break;
            case IFieldSymbol { ContainingType.IsGenericType: true } field:
                VisitGenericField(field, substitutions);
                break;
        }
    }

    private void VisitGenericField(IFieldSymbol field, TypeSubstitutions substitutions)
    {
        var containing = field.ContainingType!;
        var arguments = ContainingArguments(containing, substitutions);
        VisitDefinition(field.OriginalDefinition, arguments);
        VisitStaticInitialization(containing, arguments);
    }

    private void VisitDisposals(SemanticModel model, SyntaxNode node, TypeSubstitutions substitutions)
    {
        foreach (var operation in new[] { model.GetOperation(node, token) }.OfType<IOperation>())
        {
            var root = operation;
            while (root.Parent is { } parent)
            {
                root = parent;
            }
            var syntax = root.Syntax;
            var key = (syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length);
            if (!disposalCalls.TryGetValue(key, out var calls))
            {
                ControlFlowGraph?[] graphs = [ControlFlowGraph.Create(syntax, model, token)];
                calls = [.. graphs.OfType<ControlFlowGraph>().SelectMany(GetDisposalCalls)];
                disposalCalls.Add(key, calls);
            }
            var methods = calls.Where(call => node.Span.Contains(call.Syntax.Span)).Select(call => call.TargetMethod);
            foreach (var method in methods)
            {
                VisitMethod(method, substitutions);
                if (method.Name == "DisposeAsync")
                {
                    VisitImplicitAwaits(model, node.SpanStart, substitutions, method);
                }
            }
        }
    }

    private IEnumerable<IInvocationOperation> GetDisposalCalls(ControlFlowGraph graph)
    {
        var operations = graph.Blocks.SelectMany(block => block.Operations.Concat(new[] { block.BranchValue }.OfType<IOperation>()))
            .SelectMany(operation => operation.DescendantsAndSelf()).ToArray();
        foreach (var call in operations.OfType<IInvocationOperation>()
            .Where(call => call.IsImplicit && call.TargetMethod.Name is "Dispose" or "DisposeAsync"))
        {
            yield return call;
        }
        var nested = graph.LocalFunctions.Select(method => graph.GetLocalFunctionControlFlowGraph(method, token))
            .Concat(operations.OfType<IFlowAnonymousFunctionOperation>()
                .Select(function => graph.GetAnonymousFunctionControlFlowGraph(function, token)));
        foreach (var inner in nested.SelectMany(GetDisposalCalls))
        {
            yield return inner;
        }
    }

    private void VisitCollectionInitializer(SemanticModel model, InitializerExpressionSyntax initializer, TypeSubstitutions substitutions)
    {
        var additions = initializer.Expressions.Select(expression => model.GetCollectionInitializerSymbolInfo(expression, token).Symbol);
        foreach (var method in additions.OfType<IMethodSymbol>())
        {
            VisitMethod(method, substitutions);
        }
    }

    private void VisitOperators(SemanticModel model, ExpressionSyntax expression, TypeSubstitutions substitutions)
    {
        if (model.GetConversion(expression, token).MethodSymbol is { } conversion)
        {
            VisitMethod(conversion, substitutions);
        }
        if (expression is CastExpressionSyntax or BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or
            AssignmentExpressionSyntax && FindOperator(model.GetOperation(expression, token)) is { } method)
        {
            VisitMethod(method, substitutions);
        }
    }

    private static IMethodSymbol? FindOperator(IOperation? operation)
        => operation switch
        {
            IConversionOperation conversion => conversion.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            IBinaryOperation binary => binary.OperatorMethod,
            IIncrementOrDecrementOperation increment => increment.OperatorMethod,
            ICompoundAssignmentOperation assignment => assignment.OperatorMethod,
            _ => null,
        };

    private void VisitMethods(TypeSubstitutions substitutions, params IMethodSymbol?[] methods)
    {
        foreach (var method in methods.OfType<IMethodSymbol>())
        {
            VisitMethod(method, substitutions);
        }
    }

    private void VisitMethod(IMethodSymbol method, Dictionary<ITypeParameterSymbol, ITypeSymbol> inherited)
    {
        var containing = method.ContainingType!;
        var substitutions = ContainingArguments(containing, inherited);
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        var implementation = definition.PartialImplementationPart ?? definition;
        var methodArguments = method.TypeArguments.Select(argument => Substitute(argument, inherited)).ToArray();
        for (var index = 0; index < method.TypeArguments.Length; index++)
        {
            substitutions[definition.TypeParameters[index]] = methodArguments[index];
            substitutions[implementation.TypeParameters[index]] = methodArguments[index];
        }
        if (!activeMethods.TryEnter(definition, substitutions, out var entered))
        {
            return;
        }

        foreach (var argument in methodArguments)
        {
            AddType(argument);
        }
        AddType(Substitute(method.ReturnType, substitutions));
        VisitDefinition(DispatchDefinition(definition), substitutions);
        if (method.IsGenericMethod || containing.IsGenericType)
        {
            foreach (var template in templates.Read(definition))
            {
                AddType(Substitute(template, substitutions));
            }
        }
        if (method.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or
            MethodKind.UserDefinedOperator or MethodKind.Conversion &&
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
        activeMethods.Exit(definition, entered);
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
        if (!definition.DeclaringSyntaxReferences.Any(reference => compilation.ContainsSyntaxTree(reference.SyntaxTree)) ||
            !collectTemplates && substitutions.Values.Any(ContainsTypeParameter) ||
            !activeDefinitions.TryEnter(definition, substitutions, out var entered))
        {
            return;
        }

        var key = definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + "|" + instanceInitializers + "|" + string.Join(";", substitutions
            .Select(pair => pair.Key.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + "." + pair.Key.Name + "=" + pair.Value.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .OrderBy(value => value, StringComparer.Ordinal));
        if (!visited.Add(key))
        {
            activeDefinitions.Exit(definition, entered);
            return;
        }

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
        activeDefinitions.Exit(definition, entered);
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
        if (type is not INamedTypeSymbol { IsAbstract: false, IsUnboundGenericType: false } named)
        {
            return;
        }
        if (ContainsTypeParameter(named))
        {
            if (templates.IsOption(named))
            {
                optionTemplates.Add(named);
            }
            return;
        }
        if (!types.Add(named) ||
            named.DeclaringSyntaxReferences.Length == 0 && !templates.HasExports(named.ContainingAssembly!))
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
        TrackDispatch(contract.TypeKind == TypeKind.Interface ? contract.OriginalDefinition : contract, call);
    }

    private void TrackDispatch(INamedTypeSymbol contract, IMethodSymbol call)
    {
        var dispatch = GetDispatch(contract);
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
        foreach (var implementation in FindImplementations(type, call))
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

    private sealed class RecursionGuard
    {
        private readonly Dictionary<ISymbol, ActiveVisit> active = new(SymbolEqualityComparer.Default);

        internal bool TryEnter(ISymbol definition, TypeSubstitutions substitutions, out bool entered)
        {
            if (!active.TryGetValue(definition, out var previous))
            {
                entered = true;
                active.Add(definition, new ActiveVisit(substitutions));
                return true;
            }

            entered = false;
            var arguments = previous.Substitutions.Where(pair => substitutions.ContainsKey(pair.Key)).ToArray();
            var initialRemaining = GenericTypeBudget.MaxRecursiveTypeNodes;
            foreach (var pair in arguments)
            {
                GenericTypeBudget.TryConsume(pair.Value, ref initialRemaining);
            }
            var initialSize = GenericTypeBudget.MaxRecursiveTypeNodes - initialRemaining;
            var allowedSize = Math.Min(GenericTypeBudget.MaxRecursiveTypeNodes, initialSize + previous.RemainingGrowth);
            var budget = allowedSize;
            if (!arguments.All(pair => GenericTypeBudget.TryConsume(substitutions[pair.Key], ref budget)))
            {
                return false;
            }

            previous.RemainingGrowth -= Math.Max(0, allowedSize - budget - initialSize);
            return true;
        }

        internal void Exit(ISymbol definition, bool entered)
        {
            if (entered)
            {
                active.Remove(definition);
            }
        }

        private sealed class ActiveVisit(TypeSubstitutions substitutions)
        {
            internal TypeSubstitutions Substitutions { get; } = substitutions;
            internal int RemainingGrowth { get; set; } = GenericTypeBudget.MaxRecursiveTypeNodes;
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
