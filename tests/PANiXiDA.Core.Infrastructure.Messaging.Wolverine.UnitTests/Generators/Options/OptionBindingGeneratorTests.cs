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
        source.ShouldContain("KafkaConsumerOption)option).@GroupProtocol =");
        source.ShouldNotContain("ConfigurationBinder");
        source.ShouldNotContain("Activator");
    }

    [Theory(DisplayName = "Option generator compiles multi-segment configuration aliases")]
    [InlineData("nested:value")]
    [InlineData("nested:deeper:value")]
    public void GeneratorShouldResolveNestedAliases(string alias)
    {
        var compilation = CreateCompilation($$"""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                [Microsoft.Extensions.Configuration.ConfigurationKeyName("{{alias}}")]
                public string Value { get; init; }
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain("section.GetSection(\"" + alias[..alias.LastIndexOf(':')] + "\").GetChildren()");
        source.ShouldContain("child.Key, \"value\", global::System.StringComparison.OrdinalIgnoreCase");
    }

    [Theory(DisplayName = "Option generator discovers option types from referenced assemblies")]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratorShouldDiscoverReferencedOptions(bool sourceReference)
    {
        var referenced = CreateCompilation("""
            public sealed class ReferencedSettings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            internal sealed class HiddenSettings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            """, "ReferencedOptions");
        using var stream = new MemoryStream();
        referenced.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.ShouldBeTrue();
        MetadataReference reference = sourceReference ? referenced.ToMetadataReference() : MetadataReference.CreateFromImage(stream.ToArray());
        var compilation = CreateCompilation(string.Empty).AddReferences(reference);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::ReferencedSettings>");
        source.ShouldNotContain(".Register<global::HiddenSettings>");
    }

    [Theory(DisplayName = "Option generator reuses binders from the options assembly without revalidating internal converters")]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratorShouldReuseReferencedBinders(bool sourceReference)
    {
        var referenced = CreateCompilation("""
            [System.ComponentModel.TypeConverter(typeof(InternalConverter))]
            public sealed class ReferencedSettings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            internal sealed class InternalConverter : System.ComponentModel.TypeConverter;
            public sealed class GenericSettings<T> : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption
            {
                public T Value { get; set; }
            }
            public static class Setup { public static GenericSettings<int> Create() => new(); }
            """, "ReferencedGeneratedOptions");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator());
        driver.RunGeneratorsAndUpdateCompilation(referenced, out var generated, out var ownerDiagnostics, TestContext.Current.CancellationToken);
        ownerDiagnostics.ShouldBeEmpty();
        using var stream = new MemoryStream();
        generated.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.ShouldBeTrue();
        MetadataReference reference = sourceReference ? generated.ToMetadataReference() : MetadataReference.CreateFromImage(stream.ToArray());
        var compilation = CreateCompilation("""
            public static class Consumer
            {
                public static GenericSettings<int> Existing() => new();
                public static GenericSettings<string> New() => new();
            }
            """).AddReferences(reference);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldNotContain(".Register<global::ReferencedSettings>");
        source.ShouldNotContain(".Register<global::GenericSettings<int>>");
        source.ShouldContain(".Register<global::GenericSettings<string>>");
    }

    [Theory(DisplayName = "Option generator diagnoses open model types and polymorphic collection values")]
    [InlineData("object")]
    [InlineData("dynamic")]
    [InlineData("ISettings")]
    [InlineData("BaseSettings")]
    [InlineData("System.Collections.Generic.List<object>")]
    [InlineData("System.Collections.Generic.Dictionary<string, object>")]
    [InlineData("System.Collections.Generic.List<ISettings>")]
    public void GeneratorShouldDiagnosePolymorphicModels(string type)
    {
        var compilation = CreateCompilation($$"""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public {{type}} Value { get; init; }
            }
            public interface ISettings;
            public abstract class BaseSettings;
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002" &&
            diagnostic.GetMessage().Contains("concrete configuration model"));
        source.ShouldNotContain(".Register<global::Settings>");
    }

    [Theory(DisplayName = "Option generator rejects object collections without traversing unrelated dependency models")]
    [InlineData("System.Collections.Generic.List<int>")]
    [InlineData("System.Collections.Generic.Dictionary<string, int>")]
    public void GeneratorShouldDiagnoseObjectCollections(string type)
    {
        var compilation = CreateCompilation($$"""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public object Value { get; } = new {{type}}();
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002");
        source.ShouldNotContain("Microsoft.EntityFrameworkCore");
        source.ShouldNotContain(".Register<global::Settings>");
    }

    [Fact(DisplayName = "Option generator diagnoses unsupported model interfaces in referenced options")]
    public void GeneratorShouldDiagnoseReferencedPolymorphicModels()
    {
        var referenced = CreateCompilation("""
            public interface ISettings;
            public sealed class Implementation : ISettings
            {
                public string Extra { get; init; }
            }
            public sealed class ReferencedOptions : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public ISettings Value { get; } = new Implementation();
            }
            """, "ReferencedRuntimeTypes");
        using var stream = new MemoryStream();
        referenced.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.ShouldBeTrue();
        var compilation = CreateCompilation("file sealed class LocalImplementation : ISettings;")
            .AddReferences(MetadataReference.CreateFromImage(stream.ToArray()));

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002");
        source.ShouldNotContain("typeof(global::Implementation)");
        source.ShouldNotContain("option.@Extra");
        source.ShouldNotContain("typeof(global::LocalImplementation)");
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
                public static string StaticValue { get; set; } = "initial";
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
        source.ShouldContain("global::Settings.@StaticValue =");
        foreach (var ignored in new[] { "WriteOnly", "PrivateGetter" })
        {
            source.ShouldNotContain("option.@" + ignored);
        }
        foreach (var readOnly in new[] { "ReadOnly", "PrivateSetter" })
        {
            source.ShouldContain("children.TryGetValue(\"" + readOnly + "\"");
            source.ShouldNotContain("option.@" + readOnly + " =");
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

    [Fact(DisplayName = "Option generator treats concrete enumerable-only models as ordinary objects")]
    public void GeneratorShouldCompileEnumerableObjects()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public EnumerablePayload Value { get; init; }
            }
            public sealed class EnumerablePayload : System.Collections.Generic.IEnumerable<int>
            {
                public string Name { get; init; }
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain("new global::EnumerablePayload()");
        source.ShouldContain("children.TryGetValue(\"Name\"");
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
        source.Replace("global::System.Reflection.TargetInvocationException", string.Empty)
            .Replace("global::System.Reflection.AssemblyMetadata", string.Empty).ShouldNotContain("System.Reflection");
    }

    [Theory(DisplayName = "Option generator compiles scalar and complex constructor parameter checks")]
    [InlineData("int")]
    [InlineData("int?")]
    [InlineData("Point")]
    [InlineData("Point?")]
    [InlineData("Point[]")]
    [InlineData("Microsoft.Extensions.Configuration.IConfigurationSection")]
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

    [Fact(DisplayName = "Option generator reuses binders for complex models declared in a referenced assembly")]
    public void GeneratorShouldReuseReferencedComplexModels()
    {
        var compilation = CreateCompilation(string.Empty)
            .AddReferences(MetadataReference.CreateFromFile(typeof(OptionBindingGeneratorTests).Assembly.Location));

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldNotContain("ComplexOptionBindingTests.ComplexOptions");
        source.ShouldNotContain("ComplexOptionBindingTests.GenericOptions");
    }

    [Theory(DisplayName = "Option generator composes referenced models inside consumer options")]
    [InlineData("ComplexOptionBindingTests.ComplexOptions")]
    [InlineData("ComplexOptionBindingTests.GenericOptions")]
    [InlineData("ComplexOptionBindingTests.CollectionOptions")]
    [InlineData("ComplexOptionBindingTests.ExistingInstanceOptions")]
    [InlineData("OptionBindingCompatibilityTests.ConverterOptions")]
    [InlineData("OptionBindingCompatibilityTests.StructOptions")]
    [InlineData("OptionBindingFailureTests.RestrictedOptions")]
    [InlineData("OptionBindingFailureTests.IntegerDictionaryOptions")]
    [InlineData("OptionBindingReadOnlyTests.ScalarOptions")]
    [InlineData("OptionBindingReadOnlyTests.DictionaryOptions")]
    [InlineData("OptionBindingLeafTests.StaticOptions")]
    [InlineData("OptionBindingLeafTests.NullSetterOptions")]
    [InlineData("OptionBindingLeafTests.ValueOptions<System.Collections.Generic.List<OptionBindingLeafTests.RequiredStruct>>")]
    [InlineData("OptionBindingLeafTests.ValueOptions<System.Collections.Generic.List<OptionBindingLeafTests.OptionalStruct>>")]
    [InlineData("OptionBindingLeafTests.ValueOptions<System.Collections.Generic.List<OptionBindingLeafTests.DefaultStruct>>")]
    [InlineData("PolymorphicOptionBindingTests.ConcreteOptions")]
    public void GeneratorShouldComposeReferencedModels(string model)
    {
        var compilation = CreateCompilation($$"""
            using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;
            public sealed class ConsumerOptions : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public {{model}} Value { get; init; }
            }
            """).AddReferences(MetadataReference.CreateFromFile(typeof(OptionBindingGeneratorTests).Assembly.Location));

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::ConsumerOptions>");
        source.ShouldContain(model.Split('<')[0]);
    }

    [Fact(DisplayName = "Option generator tolerates incomplete assembly metadata while the compiler reports its error")]
    public void GeneratorShouldHandleIncompleteAssemblyMetadata()
    {
        var compilation = CreateCompilation("""
            [assembly: System.Reflection.AssemblyMetadata("PANiXiDA.Wolverine.GeneratedOptionBinder")]
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption;
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator());

        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        compilation.GetDiagnostics(TestContext.Current.CancellationToken).ShouldContain(diagnostic => diagnostic.Id == "CS7036");
        result.Diagnostics.ShouldBeEmpty();
        result.GeneratedTrees.Single().ToString().ShouldContain(".Register<global::Settings>");
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
                public MissingProperty Missing { get; init; }
                public RefParameter Reference { get; init; }
                public OptionalPayload Optional { get; init; }
                public ReadOnlyValue ReadOnly { get; }
                public InitializedValue Initialized { get; init; }
                [Microsoft.Extensions.Configuration.ConfigurationKeyName(" ")]
                public string Name { get; init; }
                public System.Collections.Generic.Dictionary<string, Microsoft.Extensions.Configuration.IConfigurationSection> Sections { get; init; }
                public System.Collections.Generic.List<byte[]> Binary { get; init; }
            }
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
