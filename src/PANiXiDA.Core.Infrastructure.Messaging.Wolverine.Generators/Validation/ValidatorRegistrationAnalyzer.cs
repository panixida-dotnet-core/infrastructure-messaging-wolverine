using System.Collections.Concurrent;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Validation;

/// <summary>
/// Detects validators added after the registration generator inspected the compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValidatorRegistrationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor MissingRegistration = new(
        "PANWOLVSG001",
        "Generated validator requires a referenced project",
        "Validator '{0}' was added after validator discovery; " +
        "generate it in a referenced project so the host can register it",
        "ValidatorRegistration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingRegistration];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(StartAnalysis);
    }

    private static void StartAnalysis(CompilationStartAnalysisContext context)
    {
        var validatorContract = context.Compilation.GetTypeByMetadataName("FluentValidation.IValidator`1");
        var registry = context.Compilation.GetTypeByMetadataName(
            "PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation.ValidatorRegistry");
        if (validatorContract is null || registry is null ||
            !context.Compilation.SyntaxTrees.Any(IsRegistrationTree))
        {
            return;
        }

        var registered = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var assemblyName = context.Compilation.Assembly.Identity.ToString();
        context.RegisterSemanticModelAction(model => CollectRegistrations(model, registry, assemblyName, registered));
        context.RegisterCompilationEndAction(end => ReportMissingRegistrations(end, validatorContract, registered));
    }

    private static void CollectRegistrations(
        SemanticModelAnalysisContext context,
        INamedTypeSymbol registry,
        string assemblyName,
        ConcurrentDictionary<string, byte> registered)
    {
        var model = context.SemanticModel;
        if (!IsRegistrationTree(model.SyntaxTree))
        {
            return;
        }

        foreach (var call in model.SyntaxTree.GetRoot(context.CancellationToken)
                     .DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(call, context.CancellationToken).Symbol is not IMethodSymbol method ||
                method.Name != "Register" || !SymbolEqualityComparer.Default.Equals(method.ContainingType, registry))
            {
                continue;
            }

            var name = model.GetConstantValue(call.ArgumentList.Arguments[1].Expression, context.CancellationToken);
            var validatorName = model.GetConstantValue(call.ArgumentList.Arguments[2].Expression, context.CancellationToken);
            if (name.Value is string value && value == assemblyName && validatorName.Value is string typeName)
            {
                registered.TryAdd(typeName, 0);
            }
        }
    }

    private static void ReportMissingRegistrations(
        CompilationAnalysisContext context,
        INamedTypeSymbol validatorContract,
        ConcurrentDictionary<string, byte> registered)
    {
        foreach (var type in ValidatorRegistrationGenerator.EnumerateTypes(context.Compilation.Assembly.GlobalNamespace))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (type.IsAbstract || ValidatorRegistrationGenerator.HasTypeParameters(type) ||
                !type.AllInterfaces.Any(contract =>
                    SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, validatorContract)) ||
                registered.ContainsKey(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(MissingRegistration, type.Locations.FirstOrDefault(), type.Name));
        }
    }

    private static bool IsRegistrationTree(SyntaxTree tree)
    {
        return Path.GetFileName(tree.FilePath) == "ValidatorRegistrations.g.cs";
    }
}
