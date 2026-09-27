using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using FluentValidation;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Validation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generators.Validation;

public sealed class ValidatorRegistrationGeneratorTests
{
    private const string Source = """
        using FluentValidation;
        public sealed class Dependency;
        public abstract class BaseValidator<T> : AbstractValidator<T>;
        public sealed class AlphaValidator(Dependency dependency) : BaseValidator<string>
        {
            public Dependency Dependency { get; } = dependency;
        }
        public sealed class BetaValidator : AbstractValidator<string>
        {
            public BetaValidator() { RuleFor(value => value).NotEmpty(); }
        }
        internal sealed class InternalValidator : AbstractValidator<int>;
        public class GenericValidator<T> : AbstractValidator<T>;
        public class GenericContainer<T> { public sealed class NestedValidator : AbstractValidator<string>; }
        public static class Container { public sealed class NestedValidator : AbstractValidator<long>; }
        """;

    [Fact(DisplayName = "Generated registrations preserve FluentValidation service descriptors")]
    public void GeneratorShouldPreserveScannerDescriptors()
    {
        var module = Compile(Source);
        var expected = new ServiceCollection();
        expected.AddValidatorsFromAssembly(module.Assembly, includeInternalTypes: true);
        var actual = new ServiceCollection();

        ValidatorRegistry.AddValidators(actual, [module.Assembly, module.Assembly]);
        ValidatorRegistry.AddValidators(actual, [module.Assembly]);

        static string describe(ServiceDescriptor descriptor) =>
            $"{descriptor.ServiceType}|{descriptor.ImplementationType}|{descriptor.Lifetime}";
        actual.Select(describe).Order().ShouldBe(expected.Select(describe).Order());
        actual.Count.ShouldBe(8);
    }

    [Fact(DisplayName = "Generated validators preserve scoped activation, dependencies, and validation rules")]
    public void GeneratorShouldPreserveScopedActivationAndValidation()
    {
        var module = Compile(Source);
        var dependency = module.Assembly.GetType("Dependency", throwOnError: true)!;
        var validator = module.Assembly.GetType("AlphaValidator", throwOnError: true)!;
        var services = new ServiceCollection();
        services.AddScoped(dependency);
        ValidatorRegistry.AddValidators(services, [module.Assembly]);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var validators = first.ServiceProvider.GetServices<IValidator<string>>().ToArray();

        validators.Length.ShouldBe(2);
        validators[0].ShouldBeSameAs(first.ServiceProvider.GetServices<IValidator<string>>().First());
        validators[0].ShouldNotBeSameAs(second.ServiceProvider.GetServices<IValidator<string>>().First());
        validators[0].ShouldNotBeSameAs(first.ServiceProvider.GetRequiredService(validator));
        validator.GetProperty("Dependency")!.GetValue(validators[0])
            .ShouldBeSameAs(first.ServiceProvider.GetRequiredService(dependency));
        validators[1].Validate("").IsValid.ShouldBeFalse();
        validators[1].Validate("valid").IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Generated registrations preserve the scanner contract for validators with multiple interfaces")]
    public void GeneratorShouldPreserveFirstValidatorContract()
    {
        var module = Compile("""
            using System.Threading;
            using System.Threading.Tasks;
            using FluentValidation;
            using FluentValidation.Results;
            public sealed class MultipleValidator : AbstractValidator<string>, IValidator<int>
            {
                ValidationResult IValidator<int>.Validate(int value) => new();
                Task<ValidationResult> IValidator<int>.ValidateAsync(int value, CancellationToken token)
                    => Task.FromResult(new ValidationResult());
            }
            """);
        var expected = new ServiceCollection();
        expected.AddValidatorsFromAssembly(module.Assembly);
        var actual = new ServiceCollection();

        ValidatorRegistry.AddValidators(actual, [module.Assembly]);

        actual.Select(item => item.ServiceType).ShouldBe(expected.Select(item => item.ServiceType));
    }

    [Fact(DisplayName = "Generated registrations preserve existing self registrations and validator implementations")]
    public void GeneratorShouldPreserveExplicitRegistrations()
    {
        var module = Compile("using FluentValidation; public sealed class Validator : AbstractValidator<string>;");
        var validator = module.Assembly.GetType("Validator", throwOnError: true)!;
        var services = new ServiceCollection();
        services.AddSingleton(validator);
        services.AddTransient(typeof(IValidator<string>), validator);

        ValidatorRegistry.AddValidators(services, [module.Assembly]);

        services.Count.ShouldBe(2);
        services.Single(item => item.ServiceType == validator).Lifetime.ShouldBe(ServiceLifetime.Singleton);
        services.Single(item => item.ServiceType == typeof(IValidator<string>)).Lifetime.ShouldBe(ServiceLifetime.Transient);
    }

    [Fact(DisplayName = "A host registers validators from a referenced assembly without a Wolverine dependency")]
    public void GeneratorShouldRegisterReferencedApplicationValidators()
    {
        var application = Compile(
            "using FluentValidation; public sealed class Validator : AbstractValidator<string>;",
            generate: false,
            includeAdapter: false);
        var host = Compile("public static class Host;", references: [application.Reference]);
        RuntimeHelpers.RunModuleConstructor(host.Assembly.ManifestModule.ModuleHandle);
        var services = new ServiceCollection();

        ValidatorRegistry.AddValidators(services, [host.Assembly]);
        services.ShouldBeEmpty();
        ValidatorRegistry.AddValidators(services, [application.Assembly]);

        services.Count.ShouldBe(2);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IValidator<string>>().GetType().Assembly.ShouldBe(application.Assembly);
        application.Assembly.GetReferencedAssemblies().ShouldNotContain(reference =>
            reference.Name!.Contains("Wolverine", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Referenced internal validators are supplied by their declaring module generator")]
    public void GeneratorShouldMergeDeclaringAndHostRegistrations()
    {
        var application = Compile(Source);
        var host = Compile("public static class Host;", references: [application.Reference]);
        RuntimeHelpers.RunModuleConstructor(host.Assembly.ManifestModule.ModuleHandle);
        var services = new ServiceCollection();

        ValidatorRegistry.AddValidators(services, [application.Assembly]);

        services.Count.ShouldBe(8);
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IValidator<int>));
    }

    [Theory(DisplayName = "Inaccessible validators fail explicitly when their assembly is selected")]
    [InlineData("internal sealed class HiddenValidator : AbstractValidator<string>;", true)]
    [InlineData("public static class Container { private sealed class HiddenValidator : AbstractValidator<string>; }", false)]
    [InlineData("file sealed class HiddenValidator : AbstractValidator<string>;", false)]
    public void GeneratorShouldRejectInaccessibleValidators(
        string declaration,
        bool referenced)
    {
        var application = Compile("using FluentValidation; " + declaration, generate: !referenced);
        if (referenced)
        {
            var host = Compile("public static class Host;", references: [application.Reference]);
            RuntimeHelpers.RunModuleConstructor(host.Assembly.ManifestModule.ModuleHandle);
            ValidatorRegistry.AddValidators(new ServiceCollection(), [host.Assembly]);
        }

        var exception = Should.Throw<InvalidOperationException>(() =>
            ValidatorRegistry.AddValidators(new ServiceCollection(), [application.Assembly]));

        exception.Message.ShouldContain("No generated validator registration exists");
        exception.Message.ShouldContain("HiddenValidator");
    }

    [Fact(DisplayName = "Empty assemblies are valid while missing generator metadata fails explicitly")]
    public void GeneratorShouldDistinguishEmptyAndMissingMetadata()
    {
        var empty = Compile("public static class Empty;");
        var missing = Compile("public static class Missing;", generate: false);
        var services = new ServiceCollection();

        ValidatorRegistry.AddValidators(services, [empty.Assembly]);
        var exception = Should.Throw<InvalidOperationException>(() =>
            ValidatorRegistry.AddValidators(services, [missing.Assembly]));

        services.ShouldBeEmpty();
        exception.Message.ShouldContain("No generated validator metadata exists");
    }

    [Fact(DisplayName = "Validator generation uses semantic contract matching and a file-local implementation")]
    public void GeneratorShouldIgnoreLookalikeContracts()
    {
        var module = Compile("""
            namespace Other { public interface IValidator<T>; }
            public sealed class LookalikeValidator : Other.IValidator<string>;
            public static class GeneratedWolverineValidators;
            """);
        var services = new ServiceCollection();

        ValidatorRegistry.AddValidators(services, [module.Assembly]);

        services.ShouldBeEmpty();
        module.Generated.ShouldContain("file static class GeneratedWolverineValidators");
        module.Generated.ShouldNotContain("LookalikeValidator");
    }

    [Fact(DisplayName = "Validator generation is disabled when the adapter contract is absent")]
    public void GeneratorShouldRequireAdapterContract()
    {
        var module = Compile("using FluentValidation; public sealed class Validator : AbstractValidator<string>;", includeAdapter: false);

        module.Generated.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Validator registration output is stable when rule bodies change")]
    public void GeneratorShouldKeepOutputStableForRuleChanges()
    {
        var name = "Stable_" + Guid.NewGuid().ToString("N");
        var original = Generate(CreateCompilation(Source, name));
        var changed = Generate(CreateCompilation(Source.Replace(".NotEmpty()", ".MinimumLength(3)"), name));

        changed.Source.ShouldBe(original.Source);
    }

    [Theory(DisplayName = "Obsolete validators do not break compilation of a host")]
    [InlineData("[System.Obsolete(\"Removed\", true)]")]
    [InlineData("[System.Obsolete(\"Deprecated\")]")]
    [InlineData("[System.Obsolete]")]
    [InlineData("[System.Obsolete(\"Deprecated\", DiagnosticId = \"OLD001\")]")]
    public void GeneratorShouldHandleObsoleteValidators(string attribute)
    {
        var application = Compile(
            $$"""
            using FluentValidation;
            {{attribute}}
            public sealed class OldValidator : AbstractValidator<string>;
            """,
            generate: false);
        var host = Compile("public static class Host;", references: [application.Reference]);
        RuntimeHelpers.RunModuleConstructor(host.Assembly.ManifestModule.ModuleHandle);
        var services = new ServiceCollection();
        ValidatorRegistry.AddValidators(services, [host.Assembly]);
        services.ShouldBeEmpty();

        ValidatorRegistry.AddValidators(services, [application.Assembly]);
        services.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Validator generation is disabled when FluentValidation is absent")]
    public void GeneratorShouldRequireFluentValidationContract()
    {
        var compilation = CreateCompilation("public static class Host;", "WithoutFluentValidation");
        var fluentValidation = compilation.References.Single(reference =>
            Path.GetFileName(reference.Display) == "FluentValidation.dll");

        var result = Generate(compilation.RemoveReferences(fluentValidation));

        result.Source.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Validator callbacks can register before assembly manifests and survive inaccessible entries")]
    public void RegistryShouldAcceptCallbacksBeforeAssemblyManifests()
    {
        var module = Compile("public static class Module;", generate: false);
        var services = new ServiceCollection();
        ValidatorRegistry.Register(module.Assembly.FullName!, "Validator", null);
        ValidatorRegistry.Register(module.Assembly.FullName!, "Validator", collection =>
            collection.AddSingleton("registered"));
        ValidatorRegistry.Register(module.Assembly.FullName!, "Validator", null);

        ValidatorRegistry.AddValidators(services, [module.Assembly]);

        services.ShouldHaveSingleItem().ImplementationInstance.ShouldBe("registered");
    }

    private static Module Compile(
        string source,
        bool generate = true,
        bool includeAdapter = true,
        MetadataReference[]? references = null)
    {
        var compilation = CreateCompilation(source, "Validators_" + Guid.NewGuid().ToString("N"), includeAdapter, references);
        var result = generate ? Generate(compilation) : (Compilation: (Compilation)compilation, Source: string.Empty);
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        stream.Position = 0;
        return new Module(AssemblyLoadContext.Default.LoadFromStream(stream), reference, result.Source);
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string name,
        bool includeAdapter = true,
        MetadataReference[]? references = null)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => includeAdapter || !Path.GetFileName(path).Contains("Wolverine", StringComparison.Ordinal));
        return CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            paths.Select(path => MetadataReference.CreateFromFile(path)).Concat(references ?? []),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error,
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
                {
                    ["CS1701"] = ReportDiagnostic.Suppress,
                    ["CS1702"] = ReportDiagnostic.Suppress
                }));
    }

    private static (Compilation Compilation, string Source) Generate(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ValidatorRegistrationGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        return (output, string.Join("\n", driver.GetRunResult().Results.Single().GeneratedSources.Select(item => item.SourceText.ToString())));
    }

    private sealed record Module(Assembly Assembly, MetadataReference Reference, string Generated);
}
