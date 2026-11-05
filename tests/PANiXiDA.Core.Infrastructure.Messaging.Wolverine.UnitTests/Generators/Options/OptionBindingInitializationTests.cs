using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generators.Options;

public sealed class OptionBindingInitializationTests
{
    public static Action OnUnrelatedInitialization { get; set; } = static () => { };

    public static string Bind<TOption>(IConfiguration configuration)
        where TOption : KafkaBrokerOption
        => OptionBindingRegistry.GetRequired<TOption>(configuration, "Settings").BootstrapServers;

    [Theory(DisplayName = "Early module initialization binds local and referenced options without initializing unrelated modules")]
    [InlineData(false)]
    [InlineData(true)]
    public void BinderShouldSupportReentrantModuleInitialization(bool referenced)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().ToList();
        if (referenced)
        {
            var external = CreateCompilation("""
                public sealed class ExternalSettings<T> : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
                """, references);
            using var externalStream = new MemoryStream();
            external.Emit(externalStream, cancellationToken: TestContext.Current.CancellationToken).Success.ShouldBeTrue();
            references.Add(MetadataReference.CreateFromImage(externalStream.ToArray()));
            externalStream.Position = 0;
            AssemblyLoadContext.Default.LoadFromStream(externalStream);
        }

        var unrelatedCalls = 0;
        OnUnrelatedInitialization = () => unrelatedCalls++;
        var unrelated = GenerateAssembly(CreateCompilation("""
            public sealed class UnrelatedSettings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            public static class UnrelatedStartup
            {
                [System.Runtime.CompilerServices.ModuleInitializer]
                public static void Initialize()
                    => PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generators.Options.OptionBindingInitializationTests.OnUnrelatedInitialization();
            }
            """, references));
        var declaration = referenced ? string.Empty :
            "public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;";
        var optionType = referenced ? "ExternalSettings<int>" : "Settings";
        var assembly = GenerateAssembly(CreateCompilation($$"""
            {{declaration}}
            public static class Startup
            {
                public static string Value;

                [System.Runtime.CompilerServices.ModuleInitializer]
                public static void Initialize()
                {
                    var configuration = new Microsoft.Extensions.Configuration.ConfigurationManager
                    {
                        ["Settings:BootstrapServers"] = "localhost:9092",
                    };
                    Value = PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generators.Options.OptionBindingInitializationTests
                        .Bind<{{optionType}}>(configuration);
                }
            }
            """, references));

        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        assembly.GetType("Startup")!.GetField("Value")!.GetValue(null).ShouldBe("localhost:9092");
        unrelatedCalls.ShouldBe(0);
        RuntimeHelpers.RunModuleConstructor(unrelated.ManifestModule.ModuleHandle);
        unrelatedCalls.ShouldBe(1);
    }

    [Fact(DisplayName = "Generated initializer metadata rejects a missing options type")]
    public void InitializerShouldRejectNullOptionsType()
    {
        Should.Throw<ArgumentNullException>(() => new TestInitializer(null!));
    }

    private sealed class TestInitializer(Type optionType) : OptionBindingInitializerAttribute(optionType)
    {
        public override void RegisterBindings() { }
    }

    private static CSharpCompilation CreateCompilation(string source, IEnumerable<MetadataReference> references)
        => CSharpCompilation.Create("ModuleOptions_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static Assembly GenerateAssembly(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics, TestContext.Current.CancellationToken);
        diagnostics.ShouldBeEmpty();
        using var stream = new MemoryStream();
        var emitted = generated.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }
}
