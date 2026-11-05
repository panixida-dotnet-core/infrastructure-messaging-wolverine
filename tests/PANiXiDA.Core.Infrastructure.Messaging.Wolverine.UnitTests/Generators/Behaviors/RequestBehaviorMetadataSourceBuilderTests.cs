using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Behaviors;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generators.Behaviors;

public sealed class RequestBehaviorMetadataSourceBuilderTests
{
    [Theory(DisplayName = "Metadata initialization uses bounded methods and preserves every registration in order")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(64913)]
    [InlineData(65536)]
    [InlineData(65537)]
    public void GeneratedInitializerShouldPreserveRegistrationsInBoundedMethods(int registrationCount)
    {
        var registrations = Enumerable.Range(0, registrationCount)
            .Select(index => (index % 3) switch
            {
                0 => $"RegisterBehavior({index});",
                1 => $"RegisterBinding({index});",
                _ => $"RegisterMessage({index});"
            });

        var generated = RequestBehaviorMetadataSourceBuilder.Build(registrations);
        var syntax = CSharpSyntaxTree.ParseText(generated, path: "RequestBehaviorMetadata.g.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var methods = syntax.GetRoot(TestContext.Current.CancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().ToArray();
        var initializer = methods.Single(method => method.Identifier.ValueText == "Initialize");
        var batches = methods.Where(method => method.Identifier.ValueText.StartsWith("InitializeBatch", StringComparison.Ordinal)).ToArray();

        batches.Length.ShouldBe((registrationCount + 255) / 256);
        batches.ShouldAllBe(method => method.Body!.Statements.Count > 0 && method.Body.Statements.Count <= 256);
        methods.ShouldAllBe(method => method.Body!.Statements.Count <= 256);
        initializer.Body!.Statements.Count.ShouldBe(registrationCount <= 65536
            ? batches.Length : (batches.Length + 255) / 256);
        initializer.AttributeLists.SelectMany(list => list.Attributes).ShouldHaveSingleItem()
            .Name.ToString().ShouldBe("global::System.Runtime.CompilerServices.ModuleInitializer");

        const string registry = """
            namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
            public static class RequestBehaviorMetadata
            {
                public static readonly System.Collections.Generic.List<int> Calls = new();
                public static void RegisterBehavior(int value) => Calls.Add(value);
                public static void RegisterBinding(int value) => Calls.Add(value);
                public static void RegisterMessage(int value) => Calls.Add(value);
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => !Path.GetFileName(path).StartsWith("PANiXiDA.", StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("BatchedMetadata_" + Guid.NewGuid().ToString("N"),
            [syntax, CSharpSyntaxTree.ParseText(registry, path: "Registry.cs", cancellationToken: TestContext.Current.CancellationToken)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        emit.Success.ShouldBeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());

        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        var registryType = assembly.GetType("PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation.RequestBehaviorMetadata", throwOnError: true)!;
        var calls = (List<int>)registryType.GetField("Calls")!.GetValue(null)!;
        calls.ShouldBe(Enumerable.Range(0, registrationCount));
    }
}
