using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

using TypeSubstitutions = System.Collections.Generic.Dictionary<Microsoft.CodeAnalysis.ITypeParameterSymbol, Microsoft.CodeAnalysis.ITypeSymbol>;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal sealed class OptionTypeDiscovery(Compilation compilation, GenericOptionTemplates templates, bool collectTemplates, CancellationToken token)
{
    private static readonly Func<ITypeSymbol, bool> IsOpenArgument = new(ContainsTypeParameter);
    private readonly HashSet<INamedTypeSymbol> types = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> asyncBuilders = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> optionTemplates = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> visited = new(StringComparer.Ordinal);
    private readonly RecursionGuard active = new();
    private readonly RecursionGuard activeMethods = new();
    private readonly RecursionGuard activeInstanceInitializers = new();
    private readonly Dictionary<INamedTypeSymbol, MethodDispatch> methodDispatches = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(SyntaxTree Tree, int Start, int Length), (SyntaxNode Syntax, IMethodSymbol Method, bool Disposal)[]> implicitFlowCalls = [];
    private static readonly string[] AsyncBuilderMemberNames =
    [
        "Create", "Task", "Start", "SetStateMachine", "SetException", "SetResult", "AwaitOnCompleted", "AwaitUnsafeOnCompleted",
    ];
    private static readonly string[] CollectionContractNames =
    [
        "System.Collections.ICollection", "System.Collections.IList", "System.Collections.IDictionary",
        "System.Collections.Generic.ICollection`1", "System.Collections.Generic.IList`1",
        "System.Collections.Generic.IReadOnlyCollection`1", "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.IDictionary`2", "System.Collections.Generic.IReadOnlyDictionary`2",
    ];
    private readonly INamedTypeSymbol[] collectionDefinitions =
        [.. CollectionContractNames.Select(compilation.GetTypeByMetadataName).OfType<INamedTypeSymbol>()];
    private readonly INamedTypeSymbol? keyValuePair = compilation.GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2");
    private readonly INamedTypeSymbol? enumerable = compilation.GetTypeByMetadataName("System.Collections.IEnumerable");
    private readonly INamedTypeSymbol? genericEnumerable = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
    private readonly INamedTypeSymbol? asyncEnumerable = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
    private readonly INamedTypeSymbol? asyncBuilderAttribute = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.AsyncMethodBuilderAttribute");
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

    internal IEnumerable<INamedTypeSymbol> DiscoverTemplates(ISymbol member)
    {
        var substitutions = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        if (member is IMethodSymbol method)
        {
            VisitMethod(method, substitutions);
        }
        else
        {
            VisitGenericField((IFieldSymbol)member, substitutions);
        }
        return optionTemplates;
    }

    private void Visit(SyntaxNode root, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        var model = templates.GetModel(root.SyntaxTree);
        foreach (var node in root.DescendantNodes())
        {
            token.ThrowIfCancellationRequested();
            VisitImplicitCalls(model, node, substitutions);
            if (!IsSymbolReference(node))
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

    private static bool IsSymbolReference(SyntaxNode node)
        => node is SimpleNameSyntax or InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax or
            PrimaryConstructorBaseTypeSyntax or MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or ElementAccessExpressionSyntax or
            ElementBindingExpressionSyntax;

    private void VisitImplicitCalls(SemanticModel model, SyntaxNode node, TypeSubstitutions substitutions)
    {
        if (node is ExpressionSyntax expression)
        {
            VisitOperators(model, expression, substitutions);
        }

        switch (node)
        {
            case MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax:
                VisitDeclaredAsyncBuilder(model, node, substitutions);
                break;
            case InvocationExpressionSyntax or ConstructorInitializerSyntax:
                VisitExternalArguments(model.GetOperation(node, token), substitutions);
                break;
            case BaseObjectCreationExpressionSyntax:
                var creation = model.GetOperation(node, token);
                if (creation is ITypeParameterObjectCreationOperation parameterCreation)
                {
                    VisitTypeParameterCreation(parameterCreation, substitutions);
                }
                else
                {
                    VisitExternalArguments(creation, substitutions);
                }
                break;
            case FixedStatementSyntax fixedStatement:
                VisitFixed(model, fixedStatement, substitutions);
                break;
            case WithExpressionSyntax:
                if (model.GetOperation(node, token) is IWithOperation operation)
                {
                    VisitMethods(substitutions, operation.CloneMethod);
                }
                break;
            case ElementAccessExpressionSyntax or ElementBindingExpressionSyntax:
                if (model.GetOperation(node, token) is IImplicitIndexerReferenceOperation indexer)
                {
                    VisitPatternMember(indexer.LengthSymbol, substitutions);
                    VisitPatternMember(indexer.IndexerSymbol, substitutions);
                }
                break;
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
            case InterpolatedStringExpressionSyntax interpolation:
                VisitInterpolation(model, interpolation, substitutions);
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
            case IfStatementSyntax or ConditionalExpressionSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                CatchClauseSyntax or CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax:
                VisitTruthCondition(model, node, substitutions);
                break;
            case UsingStatementSyntax:
            case LocalDeclarationStatementSyntax declaration when declaration.UsingKeyword.RawKind != 0:
                VisitImplicitFlowCalls(model, node, substitutions, disposal: true);
                break;
        }
    }

    private void VisitFixed(SemanticModel model, FixedStatementSyntax statement, TypeSubstitutions substitutions)
    {
        var expressions = statement.Declaration.Variables.Select(variable => variable.Initializer).OfType<EqualsValueClauseSyntax>()
            .Select(initializer => initializer.Value);
        foreach (var expression in expressions)
        {
            var type = model.GetTypeInfo(expression, token).Type;
            if (type is IArrayTypeSymbol || SymbolEqualityComparer.Default.Equals(type, compilation.GetSpecialType(SpecialType.System_String)))
            {
                continue;
            }
            // Fixed initializers omit the selected pinnable call from both the operation tree and flow graph.
            var call = SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia()), SyntaxFactory.IdentifierName("GetPinnableReference")));
            VisitMethods(substitutions, model.GetSpeculativeSymbolInfo(expression.SpanStart, call, SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol);
        }
    }

    private void VisitTypeParameterCreation(ITypeParameterObjectCreationOperation creation, TypeSubstitutions substitutions)
    {
        if (Substitute(creation.Type!, substitutions) is INamedTypeSymbol type)
        {
            VisitMethods(substitutions, type.InstanceConstructors.FirstOrDefault(constructor => constructor.Parameters.IsEmpty));
        }
    }

    private void VisitExternalArguments(IOperation? operation, TypeSubstitutions substitutions)
    {
        var method = operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IObjectCreationOperation creation => creation.Constructor,
            _ => null,
        };
        if (method is null || HasSource(method.OriginalDefinition))
        {
            return;
        }
        var arguments = operation!.ChildOperations.OfType<IArgumentOperation>().ToArray();
        foreach (var argument in arguments)
        {
            VisitExternalInterfaceArgument(argument, substitutions);
        }
        var enumerables = arguments.Where(argument => EnumerableContracts(Substitute(argument.Parameter!.Type, substitutions)).Any());
        foreach (var argument in enumerables)
        {
            foreach (var contract in EnumerableContracts(Substitute(argument.Parameter!.Type, substitutions)))
            {
                VisitExternalEnumerableContract(contract, null, substitutions);
            }
            var value = argument.Value;
            while (value is IConversionOperation conversion)
            {
                value = conversion.Operand;
            }
            if (value.Type is not null && Substitute(value.Type, substitutions) is INamedTypeSymbol source)
            {
                foreach (var contract in EnumerableContracts(source))
                {
                    VisitExternalEnumerableContract(contract, source, substitutions);
                }
            }
        }
    }

    private void VisitExternalInterfaceArgument(IArgumentOperation argument, TypeSubstitutions substitutions)
    {
        var contracts = ArgumentContracts(argument, substitutions).ToArray();
        if (contracts.Length == 0)
        {
            return;
        }
        var value = argument.Value;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }
        var source = value.Type is null ? null : Substitute(value.Type, substitutions) as INamedTypeSymbol;
        foreach (var member in contracts.SelectMany(contract => contract.GetMembers()))
        {
            VisitPatternMember(member, substitutions);
            if (source is not null)
            {
                VisitPatternMember(source.FindImplementationForInterfaceMember(member), substitutions);
            }
        }
    }

    private IEnumerable<INamedTypeSymbol> ArgumentContracts(IArgumentOperation argument, TypeSubstitutions substitutions)
    {
        var parameter = Substitute(argument.Parameter!.Type, substitutions);
        if (parameter is INamedTypeSymbol { TypeKind: TypeKind.Interface } contract)
        {
            return contract.AllInterfaces.Concat([contract]);
        }
        if (parameter is INamedTypeSymbol named && argument.Parameter.OriginalDefinition.Type is ITypeParameterSymbol original)
        {
            var definitions = original.ConstraintTypes.OfType<INamedTypeSymbol>().Concat(original.AllInterfaces).Select(type => type.OriginalDefinition);
            return named.AllInterfaces.Where(type => definitions.Contains(type.OriginalDefinition, SymbolEqualityComparer.Default));
        }
        return [];
    }

    private void VisitExternalEnumerableContract(INamedTypeSymbol contract, INamedTypeSymbol? source, TypeSubstitutions substitutions)
    {
        var asynchronous = SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, asyncEnumerable);
        var name = asynchronous ? "GetAsyncEnumerator" : "GetEnumerator";
        var enumerator = contract.GetMembers(name).OfType<IMethodSymbol>().Single();
        VisitExternalEnumerator(source is null ? enumerator : source.FindImplementationForInterfaceMember(enumerator) as IMethodSymbol, substitutions);
        if (!asynchronous)
        {
            VisitExternalCollection(contract, source, substitutions);
        }
    }

    private void VisitExternalCollection(INamedTypeSymbol enumerableContract, INamedTypeSymbol? source, TypeSubstitutions substitutions)
    {
        foreach (var contract in CollectionContracts(enumerableContract))
        {
            foreach (var member in contract.GetMembers())
            {
                VisitPatternMember(source is null ? member : source.FindImplementationForInterfaceMember(member), substitutions);
            }
        }
    }

    private IEnumerable<INamedTypeSymbol> CollectionContracts(INamedTypeSymbol enumerableContract)
    {
        var entries = enumerableContract.TypeArguments.OfType<INamedTypeSymbol>()
            .Where(element => SymbolEqualityComparer.Default.Equals(element.OriginalDefinition, keyValuePair));
        foreach (var entry in entries)
        {
            foreach (var definition in collectionDefinitions.Where(definition => definition.Arity == 2))
            {
                yield return definition.Construct([.. entry.TypeArguments]);
            }
        }
        var collections = collectionDefinitions.Where(definition => definition.Arity <= 1 &&
            (definition.Arity == 0 || enumerableContract.Arity != 0));
        foreach (var definition in collections)
        {
            yield return definition.Arity == 0 ? definition : definition.Construct([.. enumerableContract.TypeArguments]);
        }
    }

    private void VisitExternalEnumerator(IMethodSymbol? getEnumerator, TypeSubstitutions substitutions)
    {
        if (getEnumerator is null)
        {
            return;
        }
        VisitMethod(getEnumerator, substitutions);
        // Enumeration contracts and their implementations return named enumerator types.
        var enumerator = (INamedTypeSymbol)Substitute(getEnumerator.ReturnType, substitutions);
        var members = enumerator.AllInterfaces.Concat([enumerator]).SelectMany(type => type.GetMembers()).ToArray();
        foreach (var method in members.OfType<IMethodSymbol>().Where(method => method.Name is "MoveNext" or "MoveNextAsync" or "Dispose" or "DisposeAsync"))
        {
            VisitMethod(method, substitutions);
        }
        foreach (var property in members.OfType<IPropertySymbol>())
        {
            VisitMethods(substitutions, property.GetMethod);
        }
    }

    private IEnumerable<INamedTypeSymbol> EnumerableContracts(ITypeSymbol? type)
        => type is INamedTypeSymbol named
            ? named.AllInterfaces.Concat([named]).Where(contract =>
                SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, enumerable) ||
                SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, genericEnumerable) ||
                SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, asyncEnumerable))
            : [];

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
            VisitExternalArguments(call, substitutions);
        }
    }

    private void VisitInterpolation(SemanticModel model, InterpolatedStringExpressionSyntax interpolation, TypeSubstitutions substitutions)
    {
        IOperation?[] operations = [model.GetOperation(interpolation, token)];
        foreach (var content in operations.OfType<IInterpolatedStringOperation>())
        {
            var parent = content.Parent;
            while (parent is IInterpolatedStringAdditionOperation)
            {
                parent = parent.Parent;
            }
            if (parent is not IInterpolatedStringHandlerCreationOperation handler)
            {
                continue;
            }

            foreach (var creation in handler.ChildOperations.OfType<IObjectCreationOperation>())
            {
                VisitMethods(substitutions, creation.Constructor);
            }
            var appends = content.Parts.OfType<IInterpolatedStringAppendOperation>().Select(append => append.AppendCall).OfType<IInvocationOperation>();
            foreach (var append in appends)
            {
                VisitMethod(append.TargetMethod, substitutions);
            }
        }
    }

    private void VisitCollectionExpression(SemanticModel model, CollectionExpressionSyntax collection, TypeSubstitutions substitutions)
    {
        IOperation?[] operations = [model.GetOperation(collection, token)];
        foreach (var operation in operations.OfType<ICollectionExpressionOperation>())
        {
            VisitMethods(substitutions, operation.ConstructMethod);
            VisitCollectionAdds(model, collection, operation, substitutions);
            foreach (var spread in operation.Elements.OfType<ISpreadOperation>())
            {
                VisitMethods(substitutions, spread.ElementConversion.MethodSymbol);
                VisitIteration(BindSpreadIteration(model, (SpreadElementSyntax)spread.Syntax), substitutions);
            }
        }
    }

    private void VisitCollectionAdds(SemanticModel model, CollectionExpressionSyntax collection, ICollectionExpressionOperation operation,
        TypeSubstitutions substitutions)
    {
        if (operation.ConstructMethod is not { MethodKind: MethodKind.Constructor } constructor)
        {
            return;
        }

        var receiver = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
            DefaultExpression(constructor.ContainingType), SyntaxFactory.IdentifierName("Add"));
        var elements = collection.Elements.OfType<ExpressionElementSyntax>()
            .Select(element => (element.Expression.WithoutTrivia(), element.Expression.SpanStart));
        var spreads = operation.Elements.OfType<ISpreadOperation>().Where(spread => spread.ElementType is not null)
            .Select(spread => ((ExpressionSyntax)DefaultExpression(spread.ElementType!), spread.Syntax.SpanStart));
        foreach (var (element, position) in elements.Concat(spreads))
        {
            var call = SyntaxFactory.InvocationExpression(receiver,
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(element))));
            var symbol = model.GetSpeculativeSymbolInfo(position, call, SpeculativeBindingOption.BindAsExpression).Symbol;
            VisitMethods(substitutions, symbol as IMethodSymbol);
        }
    }

    private static DefaultExpressionSyntax DefaultExpression(ITypeSymbol type)
        => SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

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
            var statement = SyntaxFactory.ExpressionStatement(SyntaxFactory.AwaitExpression(DefaultExpression(method.ReturnType)));
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
        VisitMemberTemplates(field.OriginalDefinition, arguments);
        VisitStaticInitialization(containing, arguments);
    }

    private void VisitMemberTemplates(ISymbol member, TypeSubstitutions substitutions)
    {
        foreach (var template in templates.Read(member))
        {
            AddType(Substitute(template, substitutions));
        }
    }

    private void VisitImplicitFlowCalls(SemanticModel model, SyntaxNode node, TypeSubstitutions substitutions, bool disposal)
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
            if (!implicitFlowCalls.TryGetValue(key, out var calls))
            {
                ControlFlowGraph?[] graphs = [ControlFlowGraph.Create(syntax, model, token)];
                calls = [.. graphs.OfType<ControlFlowGraph>().SelectMany(GetImplicitFlowCalls)];
                implicitFlowCalls.Add(key, calls);
            }
            var methods = calls.Where(call => call.Disposal == disposal && node.Span.Contains(call.Syntax.Span)).Select(call => call.Method);
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

    private IEnumerable<(SyntaxNode Syntax, IMethodSymbol Method, bool Disposal)> GetImplicitFlowCalls(ControlFlowGraph graph)
    {
        var operations = graph.Blocks.SelectMany(block => block.Operations.Concat(new[] { block.BranchValue }.OfType<IOperation>()))
            .SelectMany(operation => operation.DescendantsAndSelf()).ToArray();
        foreach (var call in operations.OfType<IInvocationOperation>()
            .Where(call => call.IsImplicit && call.TargetMethod.Name is "Dispose" or "DisposeAsync"))
        {
            yield return (call.Syntax, call.TargetMethod, true);
        }
        var truthCalls = FindTruthTests(operations)
            .SelectMany(unary => new[] { unary.OperatorMethod }.OfType<IMethodSymbol>().Select(method => (unary.Syntax, method, false)));
        foreach (var call in truthCalls)
        {
            yield return call;
        }
        var nested = graph.LocalFunctions.Select(method => graph.GetLocalFunctionControlFlowGraph(method, token))
            .Concat(operations.OfType<IFlowAnonymousFunctionOperation>()
                .Select(function => graph.GetAnonymousFunctionControlFlowGraph(function, token)));
        foreach (var inner in nested.SelectMany(GetImplicitFlowCalls))
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
        if (expression is CastExpressionSyntax or BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or AssignmentExpressionSyntax)
        {
            var operation = model.GetOperation(expression, token);
            VisitMethods(substitutions, FindOperator(operation));
            if (operation is ITupleBinaryOperation)
            {
                var comparison = (BinaryExpressionSyntax)expression;
                VisitTupleComparison(model, comparison.Kind(), comparison.SpanStart,
                    (comparison.Left, model.GetTypeInfo(comparison.Left, token).Type),
                    (comparison.Right, model.GetTypeInfo(comparison.Right, token).Type), substitutions);
            }
            if (operation is IBinaryOperation { OperatorMethod: not null, OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr })
            {
                // The semantic binary operation exposes & or |; the flow graph exposes its short-circuit operator.
                VisitImplicitFlowCalls(model, expression, substitutions, disposal: false);
            }
        }
    }

    private void VisitTupleComparison(SemanticModel model, SyntaxKind kind, int position,
        (ExpressionSyntax Syntax, ITypeSymbol? Type) left, (ExpressionSyntax Syntax, ITypeSymbol? Type) right, TypeSubstitutions substitutions)
    {
        var leftParts = TupleParts(model, left.Syntax, left.Type);
        var rightParts = TupleParts(model, right.Syntax, right.Type);
        if (leftParts.Length != 0 && leftParts.Length == rightParts.Length)
        {
            for (var index = 0; index < leftParts.Length; index++)
            {
                VisitTupleComparison(model, kind, position, leftParts[index], rightParts[index], substitutions);
            }
            return;
        }
        // Tuple comparisons omit element operators from both IOperation and CFG. Let Roslyn bind each
        // element expression at the original location, preserving literal constants and overload selection.
        var comparison = SyntaxFactory.BinaryExpression(kind, SyntaxFactory.ParenthesizedExpression(left.Syntax.WithoutTrivia()),
            SyntaxFactory.ParenthesizedExpression(right.Syntax.WithoutTrivia()));
        IMethodSymbol?[] methods = [model.GetSpeculativeSymbolInfo(position, comparison, SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol];
        foreach (var method in methods.OfType<IMethodSymbol>())
        {
            VisitMethod(method, substitutions);
            VisitTupleTruth(method, kind, substitutions);
        }
    }

    private void VisitTupleTruth(IMethodSymbol method, SyntaxKind kind, TypeSubstitutions substitutions)
    {
        var result = Substitute(method.ReturnType, ContainingArguments(method.ContainingType!, substitutions));
        var conversion = compilation.ClassifyConversion(result, compilation.GetSpecialType(SpecialType.System_Boolean));
        if (conversion.IsImplicit)
        {
            VisitMethods(substitutions, conversion.MethodSymbol);
            return;
        }
        var truth = kind == SyntaxKind.EqualsExpression ? "op_False" : "op_True";
        INamedTypeSymbol?[] types = [result as INamedTypeSymbol];
        var hierarchy = types.OfType<INamedTypeSymbol>().SelectMany(type => DispatchContracts(type)
            .Where(contract => contract.TypeKind != TypeKind.Interface).Prepend(type));
        VisitMethods(substitutions, hierarchy.SelectMany(type => type.GetMembers(truth)).OfType<IMethodSymbol>().FirstOrDefault());
    }

    private (ExpressionSyntax Syntax, ITypeSymbol? Type)[] TupleParts(SemanticModel model, ExpressionSyntax expression, ITypeSymbol? type)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }
        if (expression is TupleExpressionSyntax literal)
        {
            return [.. literal.Arguments.Select(argument => (argument.Expression, model.GetTypeInfo(argument.Expression, token).Type))];
        }
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            type = nullable.TypeArguments[0];
            expression = TupleMember(expression, "Value");
        }
        return type is INamedTypeSymbol { IsTupleType: true } tuple
            ? [.. tuple.TupleElements.Select((field, index) => ((ExpressionSyntax)TupleMember(expression,
                "Item" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), (ITypeSymbol?)field.Type))]
            : [];
    }

    private static MemberAccessExpressionSyntax TupleMember(ExpressionSyntax expression, string member)
        => SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia()), SyntaxFactory.IdentifierName(member));

    private void VisitTruthCondition(SemanticModel model, SyntaxNode node, TypeSubstitutions substitutions)
    {
        IOperation?[] operations = [model.GetOperation(node, token)];
        var children = operations.OfType<IOperation>().SelectMany(operation => operation.ChildOperations);
        foreach (var condition in FindTruthTests(children))
        {
            VisitMethods(substitutions, condition.OperatorMethod);
        }
    }

    private static IEnumerable<IUnaryOperation> FindTruthTests(IEnumerable<IOperation> operations)
        => operations.OfType<IUnaryOperation>().Where(unary => unary.OperatorKind is UnaryOperatorKind.True or UnaryOperatorKind.False);

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
        VisitAsyncBuilder(method, substitutions);
        VisitDefinition(DispatchDefinition(definition), substitutions);
        if (method.IsGenericMethod || containing.IsGenericType)
        {
            VisitMemberTemplates(definition, substitutions);
        }
        if (method.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or
            MethodKind.UserDefinedOperator or MethodKind.Conversion &&
            (containing.TypeKind == TypeKind.Interface || method.IsVirtual || method.IsAbstract || method.IsOverride))
        {
            VisitDispatchCall(method, substitutions);
        }
        if (containing.IsRecord && method is { IsImplicitlyDeclared: true, Name: "<Clone>$" })
        {
            VisitMethods(substitutions, containing.InstanceConstructors.FirstOrDefault(IsRecordCopyConstructor));
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

    private void VisitDeclaredAsyncBuilder(SemanticModel model, SyntaxNode node, TypeSubstitutions substitutions)
    {
        if (node is AnonymousFunctionExpressionSyntax)
        {
            IOperation?[] operations = [model.GetOperation(node, token)];
            foreach (var operation in operations.OfType<IAnonymousFunctionOperation>())
            {
                VisitAsyncBuilder(operation.Symbol, substitutions);
            }
            return;
        }
        IMethodSymbol?[] methods = [model.GetDeclaredSymbol(node, token) as IMethodSymbol];
        foreach (var method in methods.OfType<IMethodSymbol>())
        {
            VisitAsyncBuilder(method, substitutions);
        }
    }

    private void VisitAsyncBuilder(IMethodSymbol method, TypeSubstitutions substitutions)
    {
        if (!method.IsAsync)
        {
            return;
        }
        var attributes = method.GetAttributes().Concat(method.ReturnType.GetAttributes());
        if (attributes.FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, asyncBuilderAttribute))?
            .ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol builder)
        {
            return;
        }
        if (builder.IsUnboundGenericType)
        {
            if (Substitute(method.ReturnType, substitutions) is not INamedTypeSymbol result || result.TypeArguments.Length != builder.Arity)
            {
                return;
            }
            builder = builder.OriginalDefinition.Construct([.. result.TypeArguments]);
        }
        if (!HasSource(builder) && !templates.HasExports(builder.ContainingAssembly!))
        {
            return;
        }
        if (!asyncBuilders.Add(builder))
        {
            return;
        }
        var bases = DispatchContracts(builder).Where(type => type.TypeKind != TypeKind.Interface).Concat([builder]);
        var members = bases.SelectMany(type => type.GetMembers()).Where(member => AsyncBuilderMemberNames.Contains(member.Name, StringComparer.Ordinal));
        foreach (var member in members)
        {
            VisitAsyncBuilderMember(member, substitutions);
        }
    }

    private void VisitAsyncBuilderMember(ISymbol member, TypeSubstitutions substitutions)
    {
        VisitPatternMember(member, substitutions);
        if (collectTemplates || member is not IMethodSymbol { IsGenericMethod: true } method || !HasSource(method))
        {
            return;
        }
        // The compiler supplies state-machine type arguments. Collect templates with those arguments open,
        // then substitute the known builder arguments without inventing a source type for the state machine.
        var arguments = ContainingArguments(method.ContainingType!, substitutions);
        var discovery = new OptionTypeDiscovery(compilation, templates, collectTemplates: true, token);
        foreach (var template in discovery.DiscoverTemplates(method.OriginalDefinition))
        {
            AddType(Substitute(template, arguments));
        }
    }

    private void VisitImplicitBaseConstructor(IMethodSymbol method, INamedTypeSymbol containing,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> substitutions)
    {
        var baseType = containing.BaseType;
        if (baseType is null || !HasSource(containing) ||
            !HasSource(baseType) && !templates.HasExports(baseType.ContainingAssembly!) ||
            method.OriginalDefinition.DeclaringSyntaxReferences.SelectMany(reference => reference.GetSyntax(token).ChildNodes())
                .Any(IsExplicitConstructorInitializer))
        {
            return;
        }

        var copyConstructor = method.IsImplicitlyDeclared && IsRecordCopyConstructor(method);
        var candidates = baseType.InstanceConstructors
            .Where(constructor => (copyConstructor ? IsRecordCopyConstructor(constructor) :
                constructor.Parameters.All(parameter => parameter.IsOptional || parameter.IsParams)) &&
                compilation.IsSymbolAccessibleWithin(constructor, containing))
            .OrderBy(constructor => constructor.Parameters.Any(parameter => parameter.IsParams))
            .ThenBy(constructor => constructor.Parameters.Length);
        foreach (var constructor in candidates.Take(1))
        {
            VisitMethod(constructor, substitutions);
        }
    }

    private static bool IsRecordCopyConstructor(IMethodSymbol method)
        => method.ContainingType is { IsRecord: true, IsValueType: false } containing &&
            method.MethodKind == MethodKind.Constructor && method.Parameters.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, containing);

    private static bool IsExplicitConstructorInitializer(SyntaxNode node)
        => node is ConstructorInitializerSyntax ||
           node is BaseListSyntax baseList && baseList.Types.OfType<PrimaryConstructorBaseTypeSyntax>().Any();

    private bool HasSource(ISymbol definition)
        => definition.DeclaringSyntaxReferences.Any(reference => compilation.ContainsSyntaxTree(reference.SyntaxTree));

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
        if (!HasSource(definition))
        {
            return;
        }
        if (!collectTemplates && substitutions.Values.Any(IsOpenArgument))
        {
            return;
        }
        var activeDefinitions = instanceInitializers ? activeInstanceInitializers : active;
        if (!activeDefinitions.TryEnter(definition, substitutions, out var entered))
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
            .Where(member => member is IFieldSymbol or IPropertySymbol or IEventSymbol && member.IsStatic != instanceInitializers)
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
            if (!collectTemplates)
            {
                return;
            }
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
        VisitCallbacks(named);
    }

    private void VisitCallbacks(INamedTypeSymbol type)
    {
        // Metadata-only callees may construct a closed source type or invoke its virtual/interface contracts.
        Func<ISymbol, IMethodSymbol?[]> callbacks = CallbackMethods;
        var implementations = type.AllInterfaces.SelectMany(contract => contract.GetMembers())
            .Select(type.FindImplementationForInterfaceMember).OfType<ISymbol>()
            .SelectMany(callbacks).OfType<IMethodSymbol>();
        var bases = DispatchContracts(type).Where(contract => contract.TypeKind != TypeKind.Interface).Concat([type]);
        var virtuals = bases.SelectMany(contract => contract.GetMembers()).SelectMany(callbacks).OfType<IMethodSymbol>()
            .Where(method => method.IsVirtual || method.IsOverride);
        var substitutions = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var method in implementations.Concat(virtuals).Concat(type.InstanceConstructors).Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            VisitMethod(method, substitutions);
        }
    }

    private static IMethodSymbol?[] CallbackMethods(ISymbol member)
        => member switch
        {
            IMethodSymbol method => [method],
            IPropertySymbol property => [property.GetMethod, property.SetMethod],
            IEventSymbol @event => [@event.AddMethod, @event.RemoveMethod],
            _ => [],
        };

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
