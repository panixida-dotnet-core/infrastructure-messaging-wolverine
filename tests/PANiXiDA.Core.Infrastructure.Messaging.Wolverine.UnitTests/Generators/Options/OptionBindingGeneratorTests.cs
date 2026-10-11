using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generators.Options;

public sealed class OptionBindingGeneratorTests
{
    [Fact(DisplayName = "Option generator emits direct constructor and property calls for derived options")]
    public void GeneratorShouldBindInheritedAndCustomProperties()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                [Microsoft.Extensions.Configuration.ConfigurationKeyName("retry-count")]
                public int RetryCount { get; set; }
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::Settings>");
        source.ShouldContain("new global::Settings()");
        source.ShouldContain("children.TryGetValue(\"retry-count\"");
        source.ShouldContain("option.@RetryCount =");
        source.ShouldContain("option.@GroupProtocol =");
        source.ShouldNotContain("ConfigurationBinder");
        source.ShouldNotContain("Activator");
    }

    [Fact(DisplayName = "Option generator discovers option types from referenced assemblies")]
    public void GeneratorShouldDiscoverReferencedOptions()
    {
        var referenced = CreateCompilation("""
            public sealed class ReferencedSettings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            internal sealed class HiddenSettings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            """, "ReferencedOptions");
        using var stream = new MemoryStream();
        referenced.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.ShouldBeTrue();
        var compilation = CreateCompilation(string.Empty).AddReferences(MetadataReference.CreateFromImage(stream.ToArray()));

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::ReferencedSettings>");
        source.ShouldNotContain(".Register<global::HiddenSettings>");
    }

    [Fact(DisplayName = "Option generator binds closed generic options used by existing registration calls")]
    public void GeneratorShouldDiscoverClosedGenericOptions()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings<T> : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            public abstract class AbstractSettings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            public static class Setup
            {
                public static void Configure(PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations.WolverineKafkaConfiguration kafka)
                {
                    kafka.AddKafkaBroker<Settings<int>>();
                    kafka.AddKafkaBroker<Settings<int>>();
                    kafka.AddKafkaBroker<Settings<int[]>>();
                    Ignore<AbstractSettings>();
                }
                private static void Ignore<T>() {}
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::Settings<int>>");
        source.Split(".Register<global::Settings<int>>").Length.ShouldBe(2);
        source.ShouldContain(".Register<global::Settings<int[]>>");
        source.ShouldNotContain(".Register<global::AbstractSettings>");
    }

    [Fact(DisplayName = "Option generator respects hidden properties and ignores properties that cannot be bound")]
    public void GeneratorShouldRespectPropertyVisibility()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption
            {
                [System.ComponentModel.Description("Not a configuration key")]
                public new string BootstrapServers { get; set; } = "default";
                public static string StaticValue { get; set; } = "ignored";
                public string this[int index] { get => "ignored"; set {} }
                public string WriteOnly { set {} }
                public string PrivateGetter { private get; set; } = "ignored";
                public string ReadOnly => "ignored";
                public string PrivateSetter { get; private set; } = "ignored";
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain("children.TryGetValue(\"BootstrapServers\"");
        foreach (var ignored in new[] { "StaticValue", "WriteOnly", "PrivateGetter", "ReadOnly", "PrivateSetter" })
        {
            source.ShouldNotContain("option.@" + ignored);
        }
    }

    [Fact(DisplayName = "Option generator does not emit open containing types from generic registration helpers")]
    public void GeneratorShouldSkipOpenContainingTypes()
    {
        var compilation = CreateCompilation("""
            public class Container<T>
            {
                public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
                public static void Configure(PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations.WolverineKafkaConfiguration kafka)
                    => kafka.AddKafkaBroker<Settings>();
            }
            public static class Setup
            {
                public static void Configure(PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations.WolverineKafkaConfiguration kafka)
                    => kafka.AddKafkaBroker<Container<int>.Settings>();
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::Container<int>.Settings>");
        source.ShouldNotContain(".Register<global::Container<T>.Settings>");
    }

    [Fact(DisplayName = "Option generator reports unsupported members instead of silently dropping configuration")]
    public void GeneratorShouldReportUnsupportedMembers()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption
            {
                public object Extra { get; set; } = new();
            }
            """);

        var (_, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002" && diagnostic.GetMessage().Contains("Extra"));
    }

    [Theory(DisplayName = "Option generator reports inaccessible types and unsupported construction")]
    [InlineData("public class Container { private sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption {} }")]
    [InlineData("public sealed class Settings(int value) : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;")]
    [InlineData("public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption { public int Value { get; init; } }")]
    [InlineData("file sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;")]
    [InlineData("public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption { public object Extra { get; } = new(); }")]
    [InlineData("public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption { public string[] Extra { get; set; } = []; }")]
    public void GeneratorShouldReportUnsupportedOptions(string source)
    {
        var (_, diagnostics) = Generate(CreateCompilation(source));

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002");
    }

    [Fact(DisplayName = "Option generator compiles scalar conversions including nullable values")]
    public void GeneratorShouldCompileScalarConversions()
    {
        string[] types = ["string", "bool", "char", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
            "float", "double", "decimal", "System.DateTime", "System.DateTimeOffset", "System.DateOnly", "System.TimeOnly",
            "System.TimeSpan", "System.Guid", "System.Uri", "int?"];
        var properties = types.Select((type, index) => $"public {type} Value{index} {{ get; set; }}");
        var compilation = CreateCompilation("public sealed class Settings : " +
            "PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption { " + string.Join(" ", properties) + " }");

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain("option.@Value21");
    }

    [Fact(DisplayName = "Option generator skips compilations without the runtime registry")]
    public void GeneratorShouldSkipMissingRuntime()
    {
        var compilation = CSharpCompilation.Create("WithoutRuntime");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator());

        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        result.Diagnostics.ShouldBeEmpty();
        result.GeneratedTrees.ShouldBeEmpty();
    }

    private static CSharpCompilation CreateCompilation(string source, string name = "GeneratedOptionsTest")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        return CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (string Source, Diagnostic[] Diagnostics) Generate(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        return (string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString())), diagnostics.ToArray());
    }
}
