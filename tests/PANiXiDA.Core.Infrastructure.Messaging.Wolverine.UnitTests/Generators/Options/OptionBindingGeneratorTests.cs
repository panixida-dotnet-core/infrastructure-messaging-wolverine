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

    [Theory(DisplayName = "Option generator discovers closed registrations through separately compiled generic helpers")]
    [InlineData(false, "public static class Setup { public static void Register<T>(Kafka kafka) => kafka.AddKafkaBroker<ExportedSettings<T>>(); }", "Setup.Register<int>(kafka);")]
    [InlineData(true, "public static class Setup { public static void Register<T>(Kafka kafka) => kafka.AddKafkaBroker<ExportedSettings<T>>(); }", "Setup.Register<int>(kafka);")]
    [InlineData(false, "public static class Setup<T> { public static void Register(Kafka kafka) => Forward<T>(kafka); private static void Forward<U>(Kafka kafka) => kafka.AddKafkaBroker<ExportedSettings<U>>(); }", "Setup<int>.Register(kafka);")]
    [InlineData(false, "public interface IRegistration { void Configure(Kafka kafka); } public sealed class Registration<T> : IRegistration { public void Configure(Kafka kafka) => kafka.AddKafkaBroker<ExportedSettings<T>>(); }", "IRegistration registration = new Registration<int>(); registration.Configure(kafka);")]
    [InlineData(false, "public interface IRegistration { void Configure(Kafka kafka); } public sealed class Registration<T> : IRegistration { void IRegistration.Configure(Kafka kafka) => kafka.AddKafkaBroker<ExportedSettings<T>>(); }", "IRegistration registration = new Registration<int>(); registration.Configure(kafka);")]
    [InlineData(false, "public abstract class Registration { public abstract void Configure(Kafka kafka); } public sealed class Concrete<T> : Registration { public override void Configure(Kafka kafka) => kafka.AddKafkaBroker<ExportedSettings<T>>(); }", "Registration registration = new Concrete<int>(); registration.Configure(kafka);")]
    public void GeneratorShouldDiscoverReferencedGenericRegistration(bool sourceReference, string helper, string call)
    {
        const string kafka = "PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations.WolverineKafkaConfiguration";
        var referenced = CreateCompilation($$"""
            using Kafka = {{kafka}};
            [assembly: System.Reflection.AssemblyMetadata("PANiXiDA.Wolverine.GenericOptionTemplate.v1:ignored", null)]
            [assembly: System.Reflection.AssemblyMetadata(null, "ignored")]
            public sealed class ExportedSettings<T> : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            file static class FileLocalRegistration
            {
                public static void Register<T>(Kafka kafka) => kafka.AddKafkaBroker<ExportedSettings<T>>();
            }
            {{helper}}
            """, "ReferencedGenericRegistrations");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(referenced, out var generated, out var diagnostics, TestContext.Current.CancellationToken);
        diagnostics.ShouldBeEmpty();
        driver.GetRunResult().GeneratedTrees.Single().ToString().ShouldNotContain("FileLocalRegistration");
        using var stream = new MemoryStream();
        generated.Emit(stream, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(includePrivateMembers: false),
            cancellationToken: TestContext.Current.CancellationToken).Success.ShouldBeTrue();
        MetadataReference reference = sourceReference ? generated.ToMetadataReference() : MetadataReference.CreateFromImage(stream.ToArray());
        var host = CreateCompilation($$"""
            public static class Host { public static void Register({{kafka}} kafka) { {{call}} } }
            """).AddReferences(reference);

        var (source, hostDiagnostics) = Generate(host);

        hostDiagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::ExportedSettings<int>>");
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
    [InlineData("System.Collections.Frozen.FrozenSet<int>")]
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

    [Theory(DisplayName = "Option generator diagnoses derived collection interfaces instead of emitting invalid assignments")]
    [InlineData("IDictionary<string, int>")]
    [InlineData("IDictionary<System.Guid, int>")]
    [InlineData("IReadOnlyDictionary<string, int>")]
    [InlineData("IList<int>")]
    [InlineData("ICollection<int>")]
    [InlineData("ISet<string>")]
    [InlineData("IReadOnlySet<string>")]
    [InlineData("IEnumerable<int>")]
    public void GeneratorShouldDiagnoseDerivedCollectionInterfaces(string collection)
    {
        var compilation = CreateCompilation($$"""
            using System.Collections.Generic;
            public interface ICustomCollection : {{collection}};
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public ICustomCollection Value { get; init; }
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002");
        source.ShouldNotContain(".Register<global::Settings>");
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
    [InlineData("Point[][]")]
    [InlineData("System.Collections.Generic.IReadOnlyList<char>")]
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

    [Fact(DisplayName = "Option generator emits precise decimal defaults in a consuming compilation")]
    public void GeneratorShouldEmitDecimalDefaults()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings(decimal rate = 0.1234567890123456789012345678m)
                : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption
            {
                public decimal Rate { get; } = rate;
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain("0.1234567890123456789012345678m");
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
    [InlineData("OptionBindingScalarChildrenTests.DictionaryOptions")]
    [InlineData("OptionBindingLeafTests.StaticOptions")]
    [InlineData("OptionBindingLeafTests.NullSetterOptions")]
    [InlineData("OptionBindingEdgeTests.NullConversionOptions")]
    [InlineData("OptionBindingEdgeTests.RequiredOptions")]
    [InlineData("OptionBindingEdgeTests.OptionalOptions")]
    [InlineData("OptionBindingEdgeTests.ValueOptions<OptionBindingEdgeTests.NullNumber[]>")]
    [InlineData("OptionBindingEdgeTests.ThrowingGetterOptions<int[]>")]
    [InlineData("OptionBindingEdgeTests.ThrowingGetterOptions<byte[]>")]
    [InlineData("OptionBindingEdgeTests.ThrowingGetterOptions<System.Collections.Generic.IReadOnlyList<int>>")]
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
    [InlineData("\"GenericConverter`1[[System.String, System.Private.CoreLib]], GeneratedOptionsTest\"")]
    [InlineData("\"GenericConverter`1[System.String], GeneratedOptionsTest\"")]
    [InlineData("\"GenericConverter`1[[System.String, System.Runtime]], GeneratedOptionsTest\"")]
    [InlineData("\"GenericConverter`1[[System.Int32[], mscorlib]], GeneratedOptionsTest\"")]
    [InlineData("\"GenericConverter`1[System.Int32[,]], GeneratedOptionsTest\"")]
    [InlineData("\"GenericConverter`1[System.Collections.Generic.List`1[System.String]], GeneratedOptionsTest\"")]
    [InlineData("\"Outer`1+Converter`1[System.String,System.Int32], GeneratedOptionsTest\"")]
    [InlineData("\"Outer`1+Converter[System.String], GeneratedOptionsTest\"")]
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
            public class Outer<T>
            {
                public class Converter<U> : System.ComponentModel.TypeConverter;
                public class Converter : System.ComponentModel.TypeConverter;
            }
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
    [InlineData("\"[\"", "")]
    [InlineData("\"ValueConverter, MissingAssembly\"", "public class ValueConverter : System.ComponentModel.TypeConverter;")]
    [InlineData("\"MissingConverter, GeneratedOptionsTest\"", "")]
    [InlineData("\"ValueConverter[]\"", "public class ValueConverter : System.ComponentModel.TypeConverter;")]
    [InlineData("\"ValueConverter*\"", "public class ValueConverter : System.ComponentModel.TypeConverter;")]
    [InlineData("\"ValueConverter&\"", "public class ValueConverter : System.ComponentModel.TypeConverter;")]
    [InlineData("\"ValueConverter`1\"", "public class ValueConverter<T> : System.ComponentModel.TypeConverter;")]
    [InlineData("\"ValueConverter`1[Missing]\"", "public class ValueConverter<T> : System.ComponentModel.TypeConverter;")]
    [InlineData("\"ValueConverter`1[Missing[]]\"", "public class ValueConverter<T> : System.ComponentModel.TypeConverter;")]
    [InlineData("\"ValueConverter`1[System.Int32[*]]\"", "public class ValueConverter<T> : System.ComponentModel.TypeConverter;")]
    [InlineData("\"ValueConverter`1[System.String,System.Int32]\"", "public class ValueConverter<T> : System.ComponentModel.TypeConverter;")]
    [InlineData("\"ValueConverter`2[System.String]\"", "public class ValueConverter<T, U> : System.ComponentModel.TypeConverter;")]
    [InlineData("\"Outer`2+Converter`1[System.String]\"", "public class Outer<T, U> { public class Converter<V> : System.ComponentModel.TypeConverter; }")]
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

    [Theory(DisplayName = "Option generator discovers closed options through source generic helpers")]
    [InlineData("Register<int>(kafka);", "static void Register<T>(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>();", "int")]
    [InlineData("Register<dynamic>(kafka);", "static void Register<T>(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>();", "dynamic")]
    [InlineData("Register(kafka, 42);", "static void Register<T>(Kafka kafka, T value) => kafka.AddKafkaBroker<Settings<T>>();", "int")]
    [InlineData("Register<int>(kafka);", "static void Register<T>(Kafka kafka) => Other<T[]>(kafka); static void Other<U>(Kafka kafka) => kafka.AddKafkaBroker<Settings<U>>();", "int[]")]
    [InlineData("Registration<int>.Register(kafka);", "static class Registration<T> { public static void Register(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = Create<int>();", "static Settings<T> Create<T>() => new();", "int")]
    [InlineData("Register<int>(kafka);", "static void Register<T>(Kafka kafka) { kafka.AddKafkaBroker<Settings<T>>(); Register<T>(kafka); }", "int")]
    [InlineData("kafka.Register<int>();", "public static void Register<T>(this Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>();", "int")]
    [InlineData("42.Register(kafka);", "public static void Register<T>(this T value, Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>();", "int")]
    [InlineData("Register<int>(kafka);", "static void Register<T>(Kafka kafka) { void Inner() => kafka.AddKafkaBroker<Settings<T>>(); Inner(); }", "int")]
    [InlineData("_ = new Registration<int>(kafka);", "sealed class Registration<T> { public Registration(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int> registration = new(kafka);", "sealed class Registration<T> { public Registration(Kafka kafka) : this(kafka, 0) {} private Registration(Kafka kafka, int _) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration<int>(kafka);", "sealed class Registration<T>(Kafka kafka) : RegistrationBase<T>(kafka); class RegistrationBase<T> { protected RegistrationBase(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int>.Configure(kafka);", "static class Registration<T> { public static System.Action<Kafka> Configure => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int>.Configure(kafka);", "static class Registration<T> { public static System.Action<Kafka> Configure { get; } = kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int>.Configure(kafka);", "static class Registration<T> { public static readonly System.Action<Kafka> Configure = kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int>? registration = new(); registration?.Configure(kafka);", "sealed class Registration<T> { public System.Action<Kafka> Configure => Forward; private System.Action<Kafka> Forward => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int>.Configure(kafka);", "static class Registration<T> { public static readonly System.Action<Kafka> Configure; static Registration() => Configure = kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int>.Configure();", "static class Registration<T> { static Registration() => Register(null!); public static void Configure() {} private static void Register(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration<int>();", "sealed class Registration<T> { static Registration() => Register(null!); private static void Register(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration<int>();", "sealed class Registration<T> { public readonly Kafka Configuration = Register(null!); private static Kafka Register(Kafka kafka) { kafka.AddKafkaBroker<Settings<T>>(); return kafka; } }", "int")]
    [InlineData("_ = new Registration<int>();", "sealed class Registration<T> { public Kafka Configuration { get; } = Register(null!); private static Kafka Register(Kafka kafka) { kafka.AddKafkaBroker<Settings<T>>(); return kafka; } }", "int")]
    [InlineData("Registration<int>.Configure();", "static class Registration<T> { private static readonly Kafka Configuration = Register(null!); public static void Configure() {} private static Kafka Register(Kafka kafka) { kafka.AddKafkaBroker<Settings<T>>(); return kafka; } }", "int")]
    [InlineData("Registration<int>.Configure();", "static class Registration<T> { public static Kafka Configuration { get; } = Register(null!); public static void Configure() {} private static Kafka Register(Kafka kafka) { kafka.AddKafkaBroker<Settings<T>>(); return kafka; } }", "int")]
    [InlineData("_ = new Registration<int>();", "sealed class Registration<T> : RegistrationBase<T>; class RegistrationBase<T> { protected RegistrationBase() => Register(null!); private static void Register(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration<int>();", "sealed class Registration<T> : RegistrationBase<T> { public Registration() {} } class RegistrationBase<T> { protected RegistrationBase() => Register(null!); private static void Register(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration();", "sealed class Registration : RegistrationBase<int>; class RegistrationBase<T> { private RegistrationBase() {} protected RegistrationBase(string required) {} protected RegistrationBase(params long[] values) {} protected RegistrationBase(int value = 42) => Register(null!); private static void Register(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration<int>();", "sealed class Registration<T> : RegistrationBase<T>; class RegistrationBase<T> { protected RegistrationBase(params int[] values) => Register(null!); private static void Register(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration<int>(kafka);", "sealed class Registration<T> : RegistrationBase<T> { public Registration(Kafka kafka) : base(kafka) {} } class RegistrationBase<T> { protected RegistrationBase(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("IRegistration registration = new Registration<int>(); registration.Configure(kafka);", "interface IRegistration { void Configure(Kafka kafka); } sealed class Registration<T> : IRegistration { public void Configure(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Configure(new Registration<int>(), kafka);", "static void Configure(IRegistration registration, Kafka kafka) => registration.Configure(kafka); interface IRegistration { void Configure(Kafka kafka); } sealed class Registration<T> : IRegistration { void IRegistration.Configure(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Configure<int, byte>(kafka);", "static void Configure<T, U>(Kafka kafka) { IRegistration<T> registration = new Registration<T>(); registration.Configure<U>(kafka); } interface IRegistration<S> { void Configure<U>(Kafka kafka); } sealed class Registration<T> : IRegistration<T> { void IRegistration<T>.Configure<U>(Kafka kafka) => kafka.AddKafkaBroker<Settings<U>>(); }", "byte")]
    [InlineData("IRegistration registration = new Registration(); registration.Configure(kafka);", "interface IRegistration { void Configure(Kafka kafka); } class RegistrationBase<T> : IRegistration { void IRegistration.Configure(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); } sealed class Registration : RegistrationBase<int>;", "int")]
    [InlineData("IRegistration<int> registration = new Registration<int>(); registration.Configure(kafka);", "interface IRegistration<T> { void Configure(Kafka kafka) => Register(kafka); static void Register(Kafka kafka) { Local(); void Local() => kafka.AddKafkaBroker<Settings<T>>(); } } sealed class Registration<T> : IRegistration<T>;", "int")]
    [InlineData("IRegistration registration = new Registration<int>(); registration.Configure(kafka);", "interface IRegistration { void Configure(Kafka kafka); } sealed class Registration<T> : IRegistration { public void Configure(Kafka kafka) { kafka.AddKafkaBroker<Settings<T>>(); IRegistration next = new Registration<T[]>(); next.Configure(kafka); } }", "int")]
    [InlineData("BaseRegistration registration = new Registration<int>(); registration.Configure(kafka); _ = new OtherRegistration();", "class BaseRegistration { public virtual void Configure(Kafka kafka) {} } class MiddleRegistration : BaseRegistration { public override void Configure(Kafka kafka) {} } sealed class Registration<T> : MiddleRegistration { public override void Configure(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); } sealed class OtherRegistration : BaseRegistration;", "int")]
    [InlineData("Configure(new Registration(), kafka);", "static void Configure(BaseRegistration<int> registration, Kafka kafka) => registration.Configure(kafka); abstract class BaseRegistration<T> { public abstract void Configure(Kafka kafka); } class MiddleRegistration<T> : BaseRegistration<T> { public override void Configure(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); } sealed class Registration : MiddleRegistration<int> { public new void Configure(Kafka kafka) {} }", "int")]
    [InlineData("Register<byte>(kafka);", "static void Register<T>(Kafka kafka) { MiddleRegistration registration = new Registration<int>(); registration.Configure<T>(kafka); } abstract class BaseRegistration { public abstract void Configure<T>(Kafka kafka); } class MiddleRegistration : BaseRegistration { public override void Configure<T>(Kafka kafka) {} } sealed class Registration<U> : MiddleRegistration { public override void Configure<T>(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "byte")]
    [InlineData("IRegistration<object> registration = new Registration<string>(); registration.Configure(kafka); _ = new Registration<int>();", "interface IRegistration<out T> { void Configure(Kafka kafka); } interface IOther; sealed class Registration<T> : IRegistration<T>, IOther { public void Configure(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "string")]
    [InlineData("IRegistration registration = new Registration<int>(); registration.Configure(kafka);", "interface IRegistration { System.Action<Kafka> Configure { get; } } sealed class Registration<T> : IRegistration { public System.Action<Kafka> Configure => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("IRegistration registration = new Registration<int>(); registration.Configure(kafka);", "interface IRegistration { System.Action<Kafka> Configure { get; } } sealed class Registration<T> : IRegistration { System.Action<Kafka> IRegistration.Configure { get => kafka => kafka.AddKafkaBroker<Settings<T>>(); } }", "int")]
    [InlineData("BaseRegistration registration = new Registration<int>(); registration.Configure(kafka);", "abstract class BaseRegistration { public abstract System.Action<Kafka> Configure { get; } } sealed class Registration<T> : BaseRegistration { public override System.Action<Kafka> Configure => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("IRegistration registration = new Registration<int>(); registration.Kafka = kafka;", "interface IRegistration { Kafka Kafka { set; } } sealed class Registration<T> : IRegistration { public Kafka Kafka { set => value.AddKafkaBroker<Settings<T>>(); } }", "int")]
    [InlineData("IRegistration registration = new Registration<int>(); _ = registration[kafka];", "interface IRegistration { int this[Kafka kafka] { get; } } sealed class Registration<T> : IRegistration { public int this[Kafka kafka] { get { kafka.AddKafkaBroker<Settings<T>>(); return 0; } } }", "int")]
    [InlineData("Registration<int>.Configure(kafka);", "static partial class Registration<T> { public static partial void Configure(Kafka kafka); } static partial class Registration<T> { public static partial void Configure(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration.Configure<int>(kafka);", "static partial class Registration { public static partial void Configure<T>(Kafka kafka); } static partial class Registration { public static partial void Configure<T>(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int>.Configure(kafka);", "static partial class Registration<T> { public static partial System.Action<Kafka> Configure { get; } } static partial class Registration<T> { public static partial System.Action<Kafka> Configure => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration<int>(kafka);", "sealed partial class Registration<T> { public partial Registration(Kafka kafka); } sealed partial class Registration<T> { public partial Registration(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Registration<int>.Configure += _ => {};", "static partial class Registration<T> { public static partial event System.Action<Kafka> Configure; } static partial class Registration<T> { public static partial event System.Action<Kafka> Configure { add => ((Kafka)null!).AddKafkaBroker<Settings<T>>(); remove {} } }", "int")]
    [InlineData("IRegistration registration = new Registration<int>(); registration.Configure += _ => {};", "interface IRegistration { event System.Action<Kafka> Configure; } sealed class Registration<T> : IRegistration { public event System.Action<Kafka> Configure { add => ((Kafka)null!).AddKafkaBroker<Settings<T>>(); remove {} } }", "int")]
    [InlineData("Register<int, string>(kafka, false);", "static void Register<TFirst, TSecond>(Kafka kafka, bool swapped) { kafka.AddKafkaBroker<Settings<TFirst>>(); if (!swapped) Register<TSecond, TFirst>(kafka, true); }", "string")]
    [InlineData("Register<int>(kafka, false);", "static void Register<T>(Kafka kafka, bool swapped) { kafka.AddKafkaBroker<Settings<T>>(); if (!swapped) Other<T>(kafka); } static void Other<U>(Kafka kafka) => Register<string>(kafka, true);", "string")]
    [InlineData("IRegistration registration = new Registration<int, string>(); registration.Configure(kafka, false);", "interface IRegistration { void Configure(Kafka kafka, bool swapped); } sealed class Registration<TFirst, TSecond> : IRegistration { public void Configure(Kafka kafka, bool swapped) { kafka.AddKafkaBroker<Settings<TFirst>>(); if (!swapped) { IRegistration next = new Registration<TSecond, TFirst>(); next.Configure(kafka, true); } } }", "string")]
    [InlineData("Register<int>(kafka, false);", "static void Register<T>(Kafka kafka, bool wrapped) { kafka.AddKafkaBroker<Settings<T>>(); if (!wrapped) Register<System.Collections.Generic.List<T>>(kafka, true); }", "global::System.Collections.Generic.List<int>")]
    [InlineData("System.Action<Kafka> configure = new Registration<int>(); configure(kafka);", "sealed class Registration<T> { public static implicit operator System.Action<Kafka>(Registration<T> _) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Run(new Registration<int>(), kafka);", "static void Run(System.Action<Kafka> configure, Kafka kafka) => configure(kafka); sealed class Registration<T> { public static implicit operator System.Action<Kafka>(Registration<T> _) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("var configure = (System.Action<Kafka>)new Registration<int>(); configure(kafka);", "sealed class Registration<T> { public static explicit operator System.Action<Kafka>(Registration<T> _) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("Create<int>()(kafka);", "static System.Action<Kafka> Create<T>() => new Registration<T>(); sealed class Registration<T> { public static implicit operator System.Action<Kafka>(Registration<T> _) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("var configure = +new Registration<int>(); configure(kafka);", "sealed class Registration<T> { public static System.Action<Kafka> operator +(Registration<T> _) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("var configure = new Registration<int>() + kafka; configure(kafka);", "sealed class Registration<T> { public static System.Action<Kafka> operator +(Registration<T> _, Kafka kafka) => target => target.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("var registration = new Registration<int>(kafka); registration++;", "sealed class Registration<T>(Kafka kafka) { public static Registration<T> operator ++(Registration<T> value) { value.Configure(); return value; } private void Configure() => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("var registration = new Registration<int>(); registration += kafka;", "sealed class Registration<T> { public static Registration<T> operator +(Registration<T> value, Kafka kafka) { kafka.AddKafkaBroker<Settings<T>>(); return value; } }", "int")]
    [InlineData("Run(new Registration<int>(), kafka);", "static void Run<T>(T registration, Kafka kafka) where T : IRegistration<T> => (+registration)(kafka); interface IRegistration<T> where T : IRegistration<T> { static abstract System.Action<Kafka> operator +(T registration); } sealed class Registration<T> : IRegistration<Registration<T>> { public static System.Action<Kafka> operator +(Registration<T> _) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = new Registration<int> { kafka };", "sealed class Registration<T> : System.Collections.IEnumerable { public void Add(Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>(); public System.Collections.IEnumerator GetEnumerator() => throw new System.NotSupportedException(); }", "int")]
    [InlineData("_ = new Registration<int> { { kafka, 1 } };", "sealed class Registration<T> : System.Collections.IEnumerable { public void Add(Kafka kafka, int _) => kafka.AddKafkaBroker<Settings<T>>(); public System.Collections.IEnumerator GetEnumerator() => throw new System.NotSupportedException(); }", "int")]
    [InlineData("_ = new Registration<int> { kafka };", "public sealed class Registration<T> : System.Collections.IEnumerable { public System.Collections.IEnumerator GetEnumerator() => throw new System.NotSupportedException(); } public static void Add<T>(this Registration<T> registration, Kafka kafka) => kafka.AddKafkaBroker<Settings<T>>();", "int")]
    [InlineData("foreach (var configure in new Registration<int>()) configure(kafka);", "sealed class Registration<T> { public System.Collections.Generic.IEnumerator<System.Action<Kafka>> GetEnumerator() { yield return kafka => kafka.AddKafkaBroker<Settings<T>>(); } }", "int")]
    [InlineData("System.Collections.Generic.IEnumerable<System.Action<Kafka>> registration = new Registration<int>(); foreach (var configure in registration) configure(kafka);", "sealed class Registration<T> : System.Collections.Generic.IEnumerable<System.Action<Kafka>> { System.Collections.Generic.IEnumerator<System.Action<Kafka>> System.Collections.Generic.IEnumerable<System.Action<Kafka>>.GetEnumerator() { yield return kafka => kafka.AddKafkaBroker<Settings<T>>(); } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotSupportedException(); }", "int")]
    [InlineData("foreach (var configure in new Registration<int>()) configure(kafka);", "sealed class Registration<T> { public Enumerator GetEnumerator() => new(); public sealed class Enumerator { public bool MoveNext() => false; public System.Action<Kafka> Current => kafka => kafka.AddKafkaBroker<Settings<T>>(); } }", "int")]
    [InlineData("foreach (System.Action<Kafka> configure in new[] { new Registration<int>() }) configure(kafka);", "sealed class Registration<T> { public static implicit operator System.Action<Kafka>(Registration<T> _) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = Run<int>(kafka);", "static async System.Threading.Tasks.Task Run<T>(Kafka kafka) { var configure = await new Registration<T>(); configure(kafka); } static async System.Threading.Tasks.Task Ignore(dynamic source) { foreach (var _ in source) {} await source; } sealed class Registration<T> { public Awaiter GetAwaiter() => new(); public sealed class Awaiter : System.Runtime.CompilerServices.INotifyCompletion { public bool IsCompleted => true; public void OnCompleted(System.Action continuation) => continuation(); public System.Action<Kafka> GetResult() => kafka => kafka.AddKafkaBroker<Settings<T>>(); } }", "int")]
    [InlineData("using (new Registration<int>(kafka)) { _ = new System.Collections.Generic.List<int> { 1 }; }", "sealed class Registration<T> : System.IDisposable { private readonly Kafka kafka; public Registration(Kafka kafka) => this.kafka = kafka; public void Dispose() => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("using System.IDisposable registration = new Registration<int>(kafka);", "sealed class Registration<T> : System.IDisposable { private readonly Kafka kafka; public Registration(Kafka kafka) => this.kafka = kafka; void System.IDisposable.Dispose() => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("using var registration = new Registration<int>(kafka);", "ref struct Registration<T> { private readonly Kafka kafka; public Registration(Kafka kafka) => this.kafka = kafka; public void Dispose() => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("_ = Run<int>(kafka);", "static async System.Threading.Tasks.Task Run<T>(Kafka kafka) { await using var registration = new Registration<T>(kafka); } sealed class Registration<T> { private readonly Kafka kafka; public Registration(Kafka kafka) => this.kafka = kafka; public System.Threading.Tasks.ValueTask DisposeAsync(int optional = 0) { kafka.AddKafkaBroker<Settings<T>>(); return default; } }", "int")]
    [InlineData("var (configure, _) = new Registration<int>(); configure(kafka);", "sealed class Registration<T> { public void Deconstruct(out System.Action<Kafka> configure, out int id) { configure = kafka => kafka.AddKafkaBroker<Settings<T>>(); id = 0; } }", "int")]
    [InlineData("System.Action<Kafka> configure; (configure, _) = new Registration<int>(); configure(kafka);", "public sealed class Registration<T>; public static void Deconstruct<T>(this Registration<T> registration, out System.Action<Kafka> configure, out int id) { configure = kafka => kafka.AddKafkaBroker<Settings<T>>(); id = 0; }", "int")]
    [InlineData("var ((configure, _), _) = new Group<int>(); configure(kafka);", "sealed class Group<T> { public void Deconstruct(out Registration<T> registration, out int id) { registration = new(); id = 0; } } sealed class Registration<T> { public void Deconstruct(out System.Action<Kafka> configure, out int id) { configure = kafka => kafka.AddKafkaBroker<Settings<T>>(); id = 0; } }", "int")]
    [InlineData("foreach (var (configure, _) in new[] { new Registration<int>() }) configure(kafka);", "sealed class Registration<T> { public void Deconstruct(out System.Action<Kafka> configure, out int id) { configure = kafka => kafka.AddKafkaBroker<Settings<T>>(); id = 0; } }", "int")]
    [InlineData("if (new Registration<int>() is (var configure, _)) configure(kafka);", "sealed class Registration<T> { public void Deconstruct(out System.Action<Kafka> configure, out int id) { configure = kafka => kafka.AddKafkaBroker<Settings<T>>(); id = 0; } }", "int")]
    public void GeneratorShouldDiscoverGenericHelpers(string call, string helper, string argument)
        => AssertDiscoversGenericHelper(call, helper, argument);

    [Theory(DisplayName = "Option generator discovers closed options through pattern members")]
    [InlineData("if (new Registration<int>() is { Configure: var configure }) configure(kafka);", "sealed class Registration<T> { public System.Action<Kafka> Configure => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("if (new Registration<int>() is ({ Configure: var configure })) configure(kafka);", "sealed class Registration<T> { public System.Action<Kafka> Configure => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("IRegistration registration = new Registration<int>(); if (registration is { Configure: var configure }) configure(kafka);", "interface IRegistration { System.Action<Kafka> Configure { get; } } sealed class Registration<T> : IRegistration { System.Action<Kafka> IRegistration.Configure => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("if (new Wrapper() is { Group.Registration.Configure: var configure }) configure(kafka);", "sealed class Wrapper { public readonly Group<int> Group = new(); } sealed class Group<T> { public Registration<T> Registration => new(); } sealed class Registration<T> { public System.Action<Kafka> Configure => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("if (new Registration<int>() is { Configure: var configure }) configure(kafka);", "sealed class Registration<T> { public readonly System.Action<Kafka> Configure = kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("if (new Registration<int>() is [var configure]) configure(kafka);", "sealed class Registration<T> { public int Length => 1; public System.Action<Kafka> this[int index] => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("if (new Registration<int>() is [1, .. var configure]) configure(kafka);", "sealed class Registration<T> { public int Length => 2; public int this[int index] => 1; public System.Action<Kafka> Slice(int start, int length) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    public void GeneratorShouldDiscoverPatternMembers(string call, string helper, string argument)
        => AssertDiscoversGenericHelper(call, helper, argument);

    [Theory(DisplayName = "Option generator discovers closed options through awaiter continuation methods")]
    [InlineData("System.Runtime.CompilerServices.INotifyCompletion", "void System.Runtime.CompilerServices.INotifyCompletion.OnCompleted(System.Action continuation) { kafka.AddKafkaBroker<Settings<T>>(); continuation(); }")]
    [InlineData("System.Runtime.CompilerServices.ICriticalNotifyCompletion", "void System.Runtime.CompilerServices.INotifyCompletion.OnCompleted(System.Action continuation) => throw new System.NotSupportedException(); void System.Runtime.CompilerServices.ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation) { kafka.AddKafkaBroker<Settings<T>>(); continuation(); }")]
    public void GeneratorShouldDiscoverAwaiterContinuation(string contract, string continuation)
        => AssertDiscoversGenericHelper(
            "_ = Run(kafka);",
            $$"""
            static async System.Threading.Tasks.Task Run(Kafka kafka) => await new Registration<int>(kafka);
            sealed class Registration<T>
            {
                private readonly Kafka kafka;
                public Registration(Kafka kafka) => this.kafka = kafka;
                public Awaiter GetAwaiter() => new(kafka);
                public sealed class Awaiter : {{contract}}
                {
                    private readonly Kafka kafka;
                    public Awaiter(Kafka kafka) => this.kafka = kafka;
                    public bool IsCompleted => false;
                    public void GetResult() { }
                    {{continuation}}
                }
            }
            """,
            "int");

    [Fact(DisplayName = "Option generator discovers a continuation behind an awaiter interface")]
    public void GeneratorShouldDiscoverInterfaceAwaiterContinuation()
        => AssertDiscoversGenericHelper(
            "_ = Run(kafka);",
            """
            static async System.Threading.Tasks.Task Run(Kafka kafka) => await new Registration<int>(kafka);
            sealed class Registration<T>
            {
                private readonly Kafka kafka;
                public Registration(Kafka kafka) => this.kafka = kafka;
                public IAwaiter GetAwaiter() => new Awaiter<T>(kafka);
            }
            interface IAwaiter : System.Runtime.CompilerServices.INotifyCompletion
            {
                bool IsCompleted { get; }
                void GetResult();
            }
            sealed class Awaiter<T> : IAwaiter
            {
                private readonly Kafka kafka;
                public Awaiter(Kafka kafka) => this.kafka = kafka;
                public bool IsCompleted => false;
                public void GetResult() { }
                void System.Runtime.CompilerServices.INotifyCompletion.OnCompleted(System.Action continuation)
                {
                    kafka.AddKafkaBroker<Settings<T>>();
                    continuation();
                }
            }
            """,
            "int");

    [Fact(DisplayName = "Option generator selects awaiter continuation from the declared generic constraint")]
    public void GeneratorShouldDiscoverConstrainedAwaiterContinuation()
        => AssertDiscoversGenericHelper(
            "_ = Run(new Awaiter<int>(kafka));",
            """
            static async System.Threading.Tasks.Task Run<TAwaiter>(TAwaiter awaiter) where TAwaiter : IAwaiter
                => await new Registration<TAwaiter>(awaiter);
            sealed class Registration<TAwaiter>(TAwaiter awaiter)
            {
                public TAwaiter GetAwaiter() => awaiter;
            }
            interface IAwaiter : System.Runtime.CompilerServices.INotifyCompletion
            {
                bool IsCompleted { get; }
                void GetResult();
            }
            sealed class Awaiter<T> : IAwaiter, System.Runtime.CompilerServices.ICriticalNotifyCompletion
            {
                private readonly Kafka kafka;
                public Awaiter(Kafka kafka) => this.kafka = kafka;
                public bool IsCompleted => false;
                public void GetResult() { }
                void System.Runtime.CompilerServices.INotifyCompletion.OnCompleted(System.Action continuation)
                {
                    kafka.AddKafkaBroker<Settings<T>>();
                    continuation();
                }
                void System.Runtime.CompilerServices.ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation)
                    => throw new System.NotSupportedException();
            }
            """,
            "int");

    [Theory(DisplayName = "Option generator discovers closed options through translated LINQ queries")]
    [InlineData("from item in new Registration<int>() select item + 1", "sealed class Registration<T> { public System.Action<Kafka> Select(System.Func<int, int> selector) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("from item in new Registration<int>() where item > 0 select item", "sealed class Registration<T> { public System.Action<Kafka> Where(System.Func<int, bool> predicate) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }", "int")]
    [InlineData("from item in new Registration<int>() select item.ToString()", "public sealed class Registration<T>; public static System.Action<Kafka> Select<T, TResult>(this Registration<T> value, System.Func<T, TResult> selector) => kafka => kafka.AddKafkaBroker<Settings<TResult>>();", "string")]
    public void GeneratorShouldDiscoverQueryMethods(string query, string helper, string argument)
        => AssertDiscoversGenericHelper("var configure = " + query + "; configure(kafka);", helper, argument);

    [Theory(DisplayName = "Option generator discovers closed options through the await of asynchronous disposal")]
    [InlineData("true", "public void GetResult() => kafka.AddKafkaBroker<Settings<T>>(); public void OnCompleted(System.Action continuation) => continuation();")]
    [InlineData("false", "public void GetResult() { } public void OnCompleted(System.Action continuation) { kafka.AddKafkaBroker<Settings<T>>(); continuation(); }")]
    public void GeneratorShouldDiscoverAsyncDisposalAwaiter(string completed, string methods)
        => AssertDiscoversGenericHelper(
            "_ = Run(kafka);",
            $$"""
            static async System.Threading.Tasks.Task Run(Kafka kafka)
            {
                await using (new Registration<int>(kafka)) { }
            }
            sealed class Registration<T>
            {
                private readonly Kafka kafka;
                public Registration(Kafka kafka) => this.kafka = kafka;
                public Disposal DisposeAsync() => new(kafka);
                public sealed class Disposal
                {
                    private readonly Kafka kafka;
                    public Disposal(Kafka kafka) => this.kafka = kafka;
                    public Awaiter GetAwaiter() => new(kafka);
                }
                public sealed class Awaiter : System.Runtime.CompilerServices.INotifyCompletion
                {
                    private readonly Kafka kafka;
                    public Awaiter(Kafka kafka) => this.kafka = kafka;
                    public bool IsCompleted => {{completed}};
                    {{methods}}
                }
            }
            """,
            "int");

    [Theory(DisplayName = "Option generator discovers closed options through await foreach")]
    [InlineData("public Result MoveNextAsync() => new(kafka);", "")]
    [InlineData("public System.Threading.Tasks.ValueTask<bool> MoveNextAsync() => new(false);", "public Result DisposeAsync() => new(kafka);")]
    public void GeneratorShouldDiscoverAsyncEnumerationAwaiter(string moveNext, string dispose)
        => AssertDiscoversGenericHelper(
            "_ = Run(kafka);",
            $$"""
            static async System.Threading.Tasks.Task Run(Kafka kafka)
            {
                await foreach (var item in new Registration<int>(kafka)) { }
            }
            sealed class Registration<T>
            {
                private readonly Kafka kafka;
                public Registration(Kafka kafka) => this.kafka = kafka;
                public Enumerator GetAsyncEnumerator() => new(kafka);
                public sealed class Enumerator
                {
                    private readonly Kafka kafka;
                    public Enumerator(Kafka kafka) => this.kafka = kafka;
                    public int Current => 0;
                    {{moveNext}}
                    {{dispose}}
                }
                public sealed class Result
                {
                    private readonly Kafka kafka;
                    public Result(Kafka kafka) => this.kafka = kafka;
                    public Awaiter GetAwaiter() => new(kafka);
                }
                public sealed class Awaiter : System.Runtime.CompilerServices.INotifyCompletion
                {
                    private readonly Kafka kafka;
                    public Awaiter(Kafka kafka) => this.kafka = kafka;
                    public bool IsCompleted => true;
                    public bool GetResult() { kafka.AddKafkaBroker<Settings<T>>(); return false; }
                    public void OnCompleted(System.Action continuation) => continuation();
                }
            }
            """,
            "int");

    [Theory(DisplayName = "Option generator discovers closed options through collection expression spreads")]
    [InlineData("System.Action<Kafka>[] callbacks = [..new Registration<int>()]; callbacks[0](kafka);", "sealed class Registration<T> : System.Collections.Generic.IEnumerable<System.Action<Kafka>> { public System.Collections.Generic.IEnumerator<System.Action<Kafka>> GetEnumerator() { yield return kafka => kafka.AddKafkaBroker<Settings<T>>(); } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }")]
    [InlineData("System.Action<Kafka>[] callbacks = [..new Registration<int>()]; callbacks[0](kafka);", "sealed class Registration<T> : System.Collections.Generic.IEnumerable<System.Action<Kafka>> { System.Collections.Generic.IEnumerator<System.Action<Kafka>> System.Collections.Generic.IEnumerable<System.Action<Kafka>>.GetEnumerator() { yield return kafka => kafka.AddKafkaBroker<Settings<T>>(); } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotSupportedException(); }")]
    [InlineData("System.Action<Kafka>[] callbacks = [..new Registration<int>()]; callbacks[0](kafka);", "sealed class Registration<T> { public Enumerator GetEnumerator() => new(); public sealed class Enumerator { public bool MoveNext() => false; public System.Action<Kafka> Current => kafka => kafka.AddKafkaBroker<Settings<T>>(); } }")]
    [InlineData("System.Action<Kafka>[] callbacks = [..new Registration<int>()]; callbacks[0](kafka);", "public sealed class Registration<T>; public static System.Collections.Generic.IEnumerator<System.Action<Kafka>> GetEnumerator<T>(this Registration<T> registration) { yield return kafka => kafka.AddKafkaBroker<Settings<T>>(); }")]
    [InlineData("System.Action<Kafka>[] callbacks = [..new[] { new Registration<int>() }]; callbacks[0](kafka);", "sealed class Registration<T> { public static implicit operator System.Action<Kafka>(Registration<T> _) => kafka => kafka.AddKafkaBroker<Settings<T>>(); }")]
    [InlineData("Callbacks[0](kafka);", "static readonly System.Action<Kafka>[] Callbacks = [..new Registration<int>()]; sealed class Registration<T> { public System.Collections.Generic.IEnumerator<System.Action<Kafka>> GetEnumerator() { yield return kafka => kafka.AddKafkaBroker<Settings<T>>(); } }")]
    public void GeneratorShouldDiscoverCollectionSpread(string call, string helper)
        => AssertDiscoversGenericHelper(call, helper, "int");

    [Fact(DisplayName = "Option generator discovers closed options through a collection expression builder")]
    public void GeneratorShouldDiscoverCollectionBuilder()
        => AssertDiscoversGenericHelper(
            "Registration<int> callbacks = []; foreach (var callback in callbacks) callback(kafka);",
            "[System.Runtime.CompilerServices.CollectionBuilder(typeof(Builder), nameof(Builder.Create))] public sealed class Registration<T>(System.Action<Kafka> callback) { public System.Collections.Generic.IEnumerator<System.Action<Kafka>> GetEnumerator() { yield return callback; } } public static class Builder { public static Registration<T> Create<T>(System.ReadOnlySpan<System.Action<Kafka>> items) => new(kafka => kafka.AddKafkaBroker<Settings<T>>()); }",
            "int");

    [Fact(DisplayName = "Option generator discovers disposal inside a generic local function and lambda")]
    public void GeneratorShouldDiscoverUsingInsideGenericLambda()
        => AssertDiscoversGenericHelper(
            "Run<int>(); void Run<T>() { System.Action action = () => { using var registration = new Registration<T>(kafka); }; action(); }",
            "sealed class Registration<T>(Kafka kafka) : System.IDisposable { public void Dispose() => kafka.AddKafkaBroker<Settings<T>>(); }",
            "int");

    private static void AssertDiscoversGenericHelper(string call, string helper, string argument)
    {
        var compilation = CreateCompilation($$"""
            using Kafka = PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations.WolverineKafkaConfiguration;
            public sealed class Settings<T> : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            public static class Setup
            {
                public static void Configure(Kafka kafka) { {{call}} }
                {{helper}}
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::Settings<" + argument + ">>");
    }

    [Fact(DisplayName = "Option generator still registers options when an enumeration is incomplete")]
    public void GeneratorShouldContinueAfterUnboundEnumeration()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            public sealed class ValuesAttribute(int[] values) : System.Attribute;
            [Values([..new[] { 1 }])]
            public static class Setup
            {
                public static void Configure()
                {
                    foreach (var item in new object()) { }
                    var untyped = [..new[] { 1 }];
                }
            }
            """);

        var driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator()).RunGenerators(compilation, TestContext.Current.CancellationToken);
        var result = driver.GetRunResult();

        result.Diagnostics.ShouldBeEmpty();
        result.GeneratedTrees.ShouldContain(tree => tree.ToString().Contains(".Register<global::Settings>", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Option generator bounds expanding recursion in static field initializers")]
    public void GeneratorShouldBoundGrowingFieldInitializer()
    {
        var compilation = CreateCompilation("""
            using Kafka = PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations.WolverineKafkaConfiguration;
            public sealed class Settings<T> : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            public static class Setup
            {
                public static void Configure(Kafka kafka) => Registration<int>.Configure(kafka);
            }
            public static class Registration<T>
            {
                public static readonly System.Action<Kafka> Configure = kafka =>
                {
                    kafka.AddKafkaBroker<Settings<T>>();
                    _ = Registration<T[]>.Configure;
                };
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::Settings<int>>");
        source.Split(".Register<global::Settings<int", StringSplitOptions.None).Length.ShouldBeLessThanOrEqualTo(65);
    }

    [Theory(DisplayName = "Option generator reports recursively expanding generic model shapes")]
    [InlineData("Node<(int, dynamic)>", "public sealed class Node<T> { public Node<System.Collections.Generic.List<T>>? Next { get; set; } public Node<T[]>? Alternate { get; set; } }")]
    [InlineData("Container<int>.Node", "public sealed class Container<T> { public sealed class Node { public Container<T[]>.Node? Next { get; set; } } }")]
    public void GeneratorShouldDiagnoseExpandingGenericModels(string type, string declaration)
    {
        var compilation = CreateCompilation($$"""
            #nullable enable
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public {{type}}? Value { get; init; }
            }
            {{declaration}}
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldContain(diagnostic => diagnostic.Id == "PANWOLVSG002" &&
            diagnostic.GetMessage().Contains("recursive generic model"));
        source.ShouldNotContain(".Register<global::Settings>");
    }

    [Fact(DisplayName = "Option generator binds finite recursive generic models")]
    public void GeneratorShouldBindFiniteGenericModels()
    {
        var compilation = CreateCompilation("""
            #nullable enable
            public sealed class Settings : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaConsumerOption
            {
                public Node<Wrapper<Wrapper<int>>>? Value { get; init; }
            }
            public sealed class Node<T>
            {
                public Node<T>? Next { get; set; }
                public Node<string>? Fixed { get; set; }
                public T Value { get; set; } = default!;
            }
            public sealed class Wrapper<T>
            {
                public T Value { get; set; } = default!;
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::Settings>");
    }

    [Fact(DisplayName = "Option generator substitutes nested containing types in generic helpers")]
    public void GeneratorShouldDiscoverNestedGenericHelpers()
    {
        var compilation = CreateCompilation("""
            using Kafka = PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations.WolverineKafkaConfiguration;
            public class Outer<T>
            {
                public sealed class Settings<U> : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
                public sealed class Nested : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            }
            public static class Setup
            {
                public static void Configure(Kafka kafka) { Register<int>(kafka); Register<string>(kafka); }
                static void Register<T>(Kafka kafka)
                {
                    kafka.AddKafkaBroker<Outer<T>.Settings<T[]>>();
                    kafka.AddKafkaBroker<Outer<T>.Nested>();
                    _ = typeof(Outer<>);
                }
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldContain(".Register<global::Outer<int>.Settings<int[]>>");
        source.ShouldContain(".Register<global::Outer<string>.Settings<string[]>>");
        source.ShouldContain(".Register<global::Outer<int>.Nested>");
        source.ShouldContain(".Register<global::Outer<string>.Nested>");
    }

    [Fact(DisplayName = "Option generator does not emit open array arguments from uncalled generic helpers")]
    public void GeneratorShouldSkipOpenArrayArguments()
    {
        var compilation = CreateCompilation("""
            public sealed class Settings<T> : PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options.KafkaBrokerOption;
            public static class Setup
            {
                public static void Register<T>(PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations.WolverineKafkaConfiguration kafka)
                    => kafka.AddKafkaBroker<Settings<T[]>>();
            }
            """);

        var (source, diagnostics) = Generate(compilation);

        diagnostics.ShouldBeEmpty();
        source.ShouldNotContain(".Register<global::Settings<");
    }

    private static (string Source, Diagnostic[] Diagnostics) Generate(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptionBindingGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        return (string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString())), diagnostics.ToArray());
    }
}
