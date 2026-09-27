using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using FluentValidation;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Validation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generators.Validation;

public sealed class ValidatorRegistrationGeneratorTests
{
    private const string PeerValidatorSource = "public sealed class PeerValidator : FluentValidation.AbstractValidator<string>;";

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

    [Theory(DisplayName = "Value-type validators preserve scanner descriptors, scoped activation, and validation")]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratorShouldPreserveValueTypeValidators(bool referenced)
    {
        var module = Compile("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FluentValidation;
            using FluentValidation.Results;
            public sealed class Dependency;
            public readonly struct StructValidator : IValidator<string>
            {
                private readonly InlineValidator<string> validator;
                public Dependency Dependency { get; }

                public StructValidator(Dependency dependency)
                {
                    Dependency = dependency;
                    validator = new InlineValidator<string>();
                    validator.RuleFor(value => value).NotEmpty();
                }

                public ValidationResult Validate(string value) => validator.Validate(value);
                public Task<ValidationResult> ValidateAsync(string value, CancellationToken token = default)
                    => validator.ValidateAsync(value, token);
                public ValidationResult Validate(IValidationContext context) => ((IValidator)validator).Validate(context);
                public Task<ValidationResult> ValidateAsync(IValidationContext context, CancellationToken token = default)
                    => ((IValidator)validator).ValidateAsync(context, token);
                public IValidatorDescriptor CreateDescriptor() => validator.CreateDescriptor();
                public bool CanValidateInstancesOfType(Type type) => ((IValidator)validator).CanValidateInstancesOfType(type);
            }
            """, generate: !referenced, includeAdapter: !referenced);
        if (referenced)
        {
            var host = Compile("public static class Host;", references: [module.Reference]);
            RuntimeHelpers.RunModuleConstructor(host.Assembly.ManifestModule.ModuleHandle);
        }

        var dependency = module.Assembly.GetType("Dependency", throwOnError: true)!;
        var validatorType = module.Assembly.GetType("StructValidator", throwOnError: true)!;
        var expected = new ServiceCollection();
        expected.AddValidatorsFromAssembly(module.Assembly);
        expected.Count.ShouldBe(2);
        var actual = new ServiceCollection();

        ValidatorRegistry.AddValidators(actual, [module.Assembly, module.Assembly]);
        ValidatorRegistry.AddValidators(actual, [module.Assembly]);

        actual.Select(item => (item.ServiceType, item.ImplementationType, item.Lifetime))
            .ShouldBe(expected.Select(item => (item.ServiceType, item.ImplementationType, item.Lifetime)));
        foreach (var services in new[] { expected, actual })
        {
            services.AddScoped(dependency);
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var first = provider.CreateScope();
            using var second = provider.CreateScope();
            var validator = first.ServiceProvider.GetRequiredService<IValidator<string>>();
            var self = first.ServiceProvider.GetRequiredService(validatorType);

            validator.GetType().ShouldBe(validatorType);
            validator.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IValidator<string>>());
            validator.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IValidator<string>>());
            self.ShouldBeSameAs(first.ServiceProvider.GetRequiredService(validatorType));
            self.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService(validatorType));
            validator.ShouldNotBeSameAs(self);
            validatorType.GetProperty("Dependency")!.GetValue(validator)
                .ShouldBeSameAs(first.ServiceProvider.GetRequiredService(dependency));
            validator.Validate("").IsValid.ShouldBeFalse();
            validator.Validate("valid").IsValid.ShouldBeTrue();
        }
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

    [Theory(DisplayName = "Validator registrations belong to the selected assembly load context")]
    [InlineData(true)]
    [InlineData(false)]
    public void GeneratorShouldIsolateAssemblyLoadContexts(bool generateInDeclaringAssembly)
    {
        var module = Compile(PeerValidatorSource, generate: generateInDeclaringAssembly);
        var host = Compile("public static class Host;", references: [module.Reference]);
        var firstContext = new AssemblyLoadContext("First", isCollectible: true);
        var secondContext = new AssemblyLoadContext("Second", isCollectible: true);
        using var firstImage = new MemoryStream(module.Image);
        using var secondImage = new MemoryStream(module.Image);
        var firstAssembly = firstContext.LoadFromStream(firstImage);
        var secondAssembly = secondContext.LoadFromStream(secondImage);
        using var firstHostImage = new MemoryStream(host.Image);
        using var secondHostImage = new MemoryStream(host.Image);
        var firstHost = firstContext.LoadFromStream(firstHostImage);
        var secondHost = secondContext.LoadFromStream(secondHostImage);
        var first = new ServiceCollection();
        var second = new ServiceCollection();

        try
        {
            RuntimeHelpers.RunModuleConstructor(firstHost.ManifestModule.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(secondHost.ManifestModule.ModuleHandle);
            ValidatorRegistry.AddValidators(first, [firstAssembly]);
            ValidatorRegistry.AddValidators(second, [secondAssembly]);

            first.Count.ShouldBe(2);
            second.Count.ShouldBe(2);
            first.ShouldAllBe(descriptor => descriptor.ImplementationType!.Assembly == firstAssembly);
            second.ShouldAllBe(descriptor => descriptor.ImplementationType!.Assembly == secondAssembly);
        }
        finally
        {
            firstContext.Unload();
            secondContext.Unload();
        }
    }

    [Theory(DisplayName = "Validator registrations do not prevent their assembly load context from unloading")]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratorShouldAllowAssemblyLoadContextUnloading(bool referenced)
    {
        var module = Compile(PeerValidatorSource, generate: !referenced);
        var host = referenced ? Compile("public static class Host;", references: [module.Reference]) : module;
        var context = RegisterAndUnload(host.Image, referenced ? module.Assembly : null);

        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        context.IsAlive.ShouldBeFalse();
    }

    [Theory(DisplayName = "An unrelated host with an unresolvable reference does not prevent validator registration")]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratorShouldIgnoreUnresolvableHostReferences(bool invalidFile)
    {
        var module = Compile(PeerValidatorSource);
        var host = Compile("public static class Host;", references: [module.Reference]);
        var context = new RejectingReferenceLoadContext(module.Assembly.GetName().Name!, invalidFile);
        using var image = new MemoryStream(host.Image);
        var hostAssembly = context.LoadFromStream(image);
        RuntimeHelpers.RunModuleConstructor(hostAssembly.ManifestModule.ModuleHandle);
        var services = new ServiceCollection();

        try
        {
            ValidatorRegistry.AddValidators(services, [module.Assembly]);

            context.Attempts.ShouldBeGreaterThan(0);
            services.Count.ShouldBe(2);
            services.ShouldAllBe(descriptor => descriptor.ImplementationType!.Assembly == module.Assembly);
        }
        finally
        {
            context.Unload();
        }
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

    [Theory(DisplayName = "Only unregistered concrete validators emitted by peer generators produce a diagnostic")]
    [InlineData("public static class Host;", PeerValidatorSource, true)]
    [InlineData(PeerValidatorSource, "public static class Generated;", false)]
    [InlineData("public partial class PeerValidator;", "public partial class PeerValidator : FluentValidation.AbstractValidator<string>;", true)]
    [InlineData("public static class Host;", "public abstract class BaseValidator : FluentValidation.AbstractValidator<string>;", false)]
    [InlineData("public static class Host;", "public class GenericValidator<T> : FluentValidation.AbstractValidator<T>;", false)]
    public async Task GeneratorShouldRejectUnregisteredPeerValidators(
        string source,
        string peerSource,
        bool missing)
    {
        var compilation = CreateCompilation(source, "Peer_" + Guid.NewGuid().ToString("N"));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ValidatorRegistrationGenerator().AsSourceGenerator(),
            new PeerValidatorGenerator(peerSource).AsSourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, TestContext.Current.CancellationToken);

        var diagnostics = await output.WithAnalyzers([new ValidatorRegistrationAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldBe(missing ? ["PANWOLVSG001"] : []);
        diagnostics.ShouldAllBe(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "A host registers peer-generated validators from a compiled Application assembly")]
    public async Task GeneratorShouldRegisterReferencedPeerValidators()
    {
        var compilation = CreateCompilation(
            "public static class Application;",
            "Application_" + Guid.NewGuid().ToString("N"),
            includeAdapter: false);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PeerValidatorGenerator(PeerValidatorSource));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var applicationCompilation, out _, TestContext.Current.CancellationToken);
        var application = Emit(applicationCompilation, string.Empty);
        var hostCompilation = CreateCompilation(
            "public static class Host;",
            "Host_" + Guid.NewGuid().ToString("N"),
            references: [application.Reference]);
        var result = Generate(hostCompilation);
        var diagnostics = await result.Compilation.WithAnalyzers([new ValidatorRegistrationAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        var host = Emit(result.Compilation, result.Source);
        RuntimeHelpers.RunModuleConstructor(host.Assembly.ManifestModule.ModuleHandle);
        var services = new ServiceCollection();

        ValidatorRegistry.AddValidators(services, [application.Assembly]);

        diagnostics.ShouldBeEmpty();
        services.Count.ShouldBe(2);
        services.ShouldAllBe(descriptor => descriptor.ImplementationType!.Assembly == application.Assembly);
    }

    [Theory(DisplayName = "The validator analyzer is inactive without generated registrations or the required contracts")]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AnalyzerShouldIgnoreCompilationsWithoutRegistrations(
        bool includeAdapter,
        bool includeFluentValidation)
    {
        var compilation = CreateCompilation("public static class Host;", "WithoutRegistrations", includeAdapter);
        if (!includeFluentValidation)
        {
            compilation = compilation.RemoveReferences(compilation.References.Single(reference =>
                Path.GetFileName(reference.Display) == "FluentValidation.dll"));
        }

        var diagnostics = await compilation.WithAnalyzers([new ValidatorRegistrationAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        diagnostics.ShouldBeEmpty();
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
        ValidatorRegistry.Register(module.Assembly, module.Assembly.FullName!, "Validator", null);
        ValidatorRegistry.Register(module.Assembly, module.Assembly.FullName!, "Validator", collection =>
            collection.AddSingleton("registered"));
        ValidatorRegistry.Register(module.Assembly, module.Assembly.FullName!, "Validator", null);

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
        return Emit(result.Compilation, result.Source);
    }

    private static Module Emit(
        Compilation compilation,
        string generated)
    {
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        var image = stream.ToArray();
        var reference = MetadataReference.CreateFromImage(image);
        stream.Position = 0;
        return new Module(AssemblyLoadContext.Default.LoadFromStream(stream), reference, generated, image);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterAndUnload(
        byte[] image,
        Assembly? discoveryAssembly)
    {
        var context = new AssemblyLoadContext("Unloadable", isCollectible: true);
        using var stream = new MemoryStream(image);
        var assembly = context.LoadFromStream(stream);
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        ValidatorRegistry.AddValidators(new ServiceCollection(), [discoveryAssembly ?? assembly]);
        var reference = new WeakReference(context);
        context.Unload();
        return reference;
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

    private sealed record Module(
        Assembly Assembly,
        MetadataReference Reference,
        string Generated,
        byte[] Image);

    private sealed class PeerValidatorGenerator(string source) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(context.CompilationProvider,
                (output, _) => output.AddSource("PeerValidators.g.cs", source));
        }
    }

    private sealed class RejectingReferenceLoadContext(
        string rejectedName,
        bool invalidFile) : AssemblyLoadContext("Rejecting", isCollectible: true)
    {
        public int Attempts { get; private set; }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name != rejectedName)
            {
                return null;
            }

            Attempts++;
            if (invalidFile)
            {
                throw new FileLoadException("Reference cannot be loaded.");
            }

            throw new FileNotFoundException("Reference was not found.");
        }
    }
}
