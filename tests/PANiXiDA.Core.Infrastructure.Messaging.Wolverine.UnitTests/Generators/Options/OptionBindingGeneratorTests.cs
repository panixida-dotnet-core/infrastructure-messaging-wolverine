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
                public int[,] Extra { get; set; } = new int[1, 1];
            }
            """);

        var (_, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002" && diagnostic.GetMessage().Contains("one-dimensional"));
    }

    [Theory(DisplayName = "Option generator reports inaccessible types")]
    [InlineData("public class Container { private sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption {} }")]
    [InlineData("file sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;")]
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
            "System.TimeSpan", "System.Guid", "System.Uri", "int?", "System.Globalization.CultureInfo",
            "System.Version", "System.Half", "System.Int128", "System.UInt128", "System.Half?", "System.Int128?", "System.UInt128?"];
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

    [Theory(DisplayName = "Option generator compiles complex and init-only properties without changing consumer models")]
    [InlineData("object")]
    [InlineData("dynamic")]
    [InlineData("string[]")]
    [InlineData("byte[]")]
    [InlineData("System.Collections.Generic.List<Payload>")]
    [InlineData("System.Collections.Generic.IReadOnlySet<string>")]
    [InlineData("System.Collections.Generic.IReadOnlyDictionary<int, Payload>")]
    [InlineData("Microsoft.Extensions.Configuration.IConfigurationSection")]
    [InlineData("Payload")]
    public void GeneratorShouldCompileComplexProperties(string type)
    {
        var compilation = CreateCompilation($$"""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public {{type}} Value { get; init; }
            }
            public sealed record Payload(string Name, int Count = 2);
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain("UnsafeAccessor");
        source.ShouldNotContain("ConfigurationBinder.Get");
        source.Replace("global::System.Reflection.TargetInvocationException", string.Empty).ShouldNotContain("System.Reflection");
    }

    [Theory(DisplayName = "Option generator compiles scalar and complex constructor parameter checks")]
    [InlineData("int")]
    [InlineData("int?")]
    [InlineData("Point")]
    [InlineData("Point?")]
    [InlineData("Point[]")]
    public void GeneratorShouldCompileConstructorParameters(string type)
    {
        var compilation = CreateCompilation($$"""
            public sealed class Settings({{type}} value) : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public {{type}} Value { get; } = value;
            }
            public readonly record struct Point(int X);
            """);

        var (_, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Option generator preserves constraints and nested generic parameter positions in init accessors")]
    public void GeneratorShouldCompileGenericInitAccessors()
    {
        var compilation = CreateCompilation("""
            public class Container<T> where T : unmanaged
            {
                public class Payload<U, V, W> where U : struct where V : notnull where W : class, System.ICloneable, new()
                {
                    public T First { get; init; }
                    public U Second { get; init; }
                    public V Third { get; init; }
                    public W Fourth { get; init; }
                }
            }
            public sealed class Cloneable : System.ICloneable { public object Clone() => new Cloneable(); }
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public Container<int>.Payload<int, string, Cloneable> Value { get; init; }
            }
            """);

        var (_, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Option generator supports complex models declared in a separate consumer assembly")]
    public void GeneratorShouldCompileReferencedComplexModels()
    {
        var compilation = CreateCompilation(string.Empty)
            .AddReferences(MetadataReference.CreateFromFile(typeof(OptionBindingGeneratorTests).Assembly.Location));

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain("ComplexOptionBindingTests.ComplexOptions");
        source.ShouldContain("ComplexOptionBindingTests.GenericOptions");
    }

    [Theory(DisplayName = "Option generator diagnoses unsupported property shapes without emitting invalid binders")]
    [InlineData("System.Span<int>")]
    [InlineData("int*")]
    [InlineData("System.Linq.IQueryable<int>")]
    public void GeneratorShouldDiagnoseUnsupportedPropertyShapes(string type)
    {
        var compilation = CreateCompilation($$"""
            public unsafe sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public {{type}} Value { get => default; set { } }
            }
            """).WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var (_, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002");
    }

    [Fact(DisplayName = "Option generator handles inaccessible member types in incomplete consumer code")]
    public void GeneratorShouldDiagnoseInaccessiblePropertyTypes()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                private sealed class Hidden;
                public Hidden Value { get; init; }
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator());

        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002");
        result.Results.ShouldAllBe(item => item.Exception == null);
    }

    [Fact(DisplayName = "Option generator preserves constructor failures, optional nulls and blank configuration aliases")]
    public void GeneratorShouldCompileConstructionBoundaries()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public AbstractPayload Abstract { get; init; }
                public MissingProperty Missing { get; init; }
                public RefParameter Reference { get; init; }
                public OptionalPayload Optional { get; init; }
                public ReadOnlyValue ReadOnly { get; }
                public InitializedValue Initialized { get; init; }
                [Microsoft.Extensions.Configuration.ConfigurationKeyName(" ")]
                public string Name { get; init; }
                public System.Collections.Generic.Dictionary<string, Microsoft.Extensions.Configuration.IConfigurationSection> Sections { get; init; }
                public System.Collections.Generic.List<object> Values { get; init; }
                public System.Collections.Generic.List<dynamic> DynamicValues { get; init; }
                public System.Collections.Generic.List<byte[]> Binary { get; init; }
            }
            public abstract class AbstractPayload { public string Name { get; set; } }
            public class MissingProperty(int missing);
            public class RefParameter { public RefParameter(ref int value) {} public int Value { get; set; } }
            public record OptionalPayload(string Name = null, System.DayOfWeek Day = System.DayOfWeek.Friday);
            public struct ReadOnlyValue { public string Name { get; set; } }
            public struct InitializedValue { public InitializedValue() { Name = "initial"; } public string Name { get; init; } }
            """);

        var (_, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
    }

    [Theory(DisplayName = "Option generator resolves named and closed generic converters without reflection")]
    [InlineData("\"ValueConverter\"")]
    [InlineData("\"ValueConverter, GeneratedOptionsTest\"")]
    [InlineData("typeof(ValueConverter)")]
    [InlineData("typeof(GenericConverter<string>)")]
    public void GeneratorShouldResolveCustomConverters(string converter)
    {
        var compilation = CreateCompilation($$"""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public Payload Value { get; init; }
            }
            [System.ComponentModel.TypeConverter({{converter}})]
            public class Payload { public string Name { get; set; } }
            public class ValueConverter : System.ComponentModel.TypeConverter;
            public class GenericConverter<T> : System.ComponentModel.TypeConverter;
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".CanConvertFrom(typeof(string))");
        source.ShouldNotContain("TypeDescriptor");
    }

    [Theory(DisplayName = "Option generator ignores empty TypeConverter attributes")]
    [InlineData("System.ComponentModel.TypeConverter")]
    [InlineData("System.ComponentModel.TypeConverter(\"\")")]
    public void GeneratorShouldIgnoreEmptyConverterAttributes(string attribute)
    {
        var compilation = CreateCompilation($$"""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public Payload Value { get; init; }
            }
            [{{attribute}}]
            public class Payload { public string Name { get; set; } }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldNotContain(".CanConvertFrom");
    }

    [Theory(DisplayName = "Option generator diagnoses converters that cannot be constructed statically")]
    [InlineData("\"MissingConverter\"", "")]
    [InlineData("typeof(ValueConverter)", "public class ValueConverter;")]
    [InlineData("typeof(ValueConverter)", "public abstract class ValueConverter : System.ComponentModel.TypeConverter;")]
    [InlineData("typeof(ValueConverter<>)", "public class ValueConverter<T> : System.ComponentModel.TypeConverter;")]
    [InlineData("typeof(ValueConverter)", "public class ValueConverter : System.ComponentModel.TypeConverter { private ValueConverter() {} }")]
    [InlineData("typeof(ValueConverter)", "public class ValueConverter(int value) : System.ComponentModel.TypeConverter;")]
    [InlineData("typeof(ValueConverter)", "public class ValueConverter : System.ComponentModel.TypeConverter { public ValueConverter(ref System.Type type) {} }")]
    public void GeneratorShouldDiagnoseUnsupportedConverters(string converter, string declaration)
    {
        var compilation = CreateCompilation($$"""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public Payload Value { get; init; }
            }
            [System.ComponentModel.TypeConverter({{converter}})]
            public class Payload { public string Name { get; set; } }
            {{declaration}}
            """);

        var (_, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002" && diagnostic.GetMessage().Contains("TypeConverter"));
    }

    [Fact(DisplayName = "Option generator diagnoses private converter types")]
    public void GeneratorShouldDiagnosePrivateConverters()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public Payload Value { get; init; }
            }
            [System.ComponentModel.TypeConverter(typeof(Payload.Converter))]
            public class Payload
            {
                public string Name { get; set; }
                private class Converter : System.ComponentModel.TypeConverter;
            }
            """);

        var (_, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002" && diagnostic.GetMessage().Contains("TypeConverter"));
    }

    private static CSharpCompilation CreateCompilation(string source, string name = "GeneratedOptionsTest")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => path != typeof(OptionBindingGeneratorTests).Assembly.Location)
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
