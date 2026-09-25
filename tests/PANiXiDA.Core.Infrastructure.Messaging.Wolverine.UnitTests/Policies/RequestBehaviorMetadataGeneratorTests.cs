using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class RequestBehaviorMetadataGeneratorTests
{
    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using PANiXiDA.Core.Application.Messaging.Mediator.Contracts;
        using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors.Abstractions;
        using PANiXiDA.Core.ResultPattern;
        public record Request(int Value) : ICommand<Result>;
        public sealed record DerivedRequest(int Value) : Request(Value);
        public sealed class Dependency;
        public class Behavior<TRequest, TResult>(Dependency dependency) : IBeforeRequestBehavior<TRequest, TResult>
            where TRequest : IRequest<TResult> where TResult : Result
        {
            public Task<Result> BeforeAsync(TRequest request, CancellationToken token) => Task.FromResult(Result.Success());
        }
        public sealed class NeedsDefaultConstructor<TRequest, TResult> : IBeforeRequestBehavior<TRequest, TResult>
            where TRequest : IRequest<TResult>, new() where TResult : Result
        {
            public Task<Result> BeforeAsync(TRequest request, CancellationToken token) => Task.FromResult(Result.Success());
        }
        public sealed class BaseBehavior : IBeforeRequestBehavior<Request, Result>
        {
            public Task<Result> BeforeAsync(Request request, CancellationToken token) => Task.FromResult(Result.Success());
        }
        """;

    [Fact(DisplayName = "Metadata generator emits compilable constructors, inherited requests, and constrained bindings")]
    public void GeneratorShouldPreserveConstructorsInheritanceAndConstraints()
    {
        var source = Generate(Source);

        source.ShouldContain("typeof(global::Behavior<global::DerivedRequest, global::PANiXiDA.Core.ResultPattern.Result>)");
        source.ShouldContain("new global::System.Type[] { typeof(global::Dependency) }");
        source.ShouldContain("typeof(global::BaseBehavior), typeof(global::DerivedRequest)");
        var constrainedBindings = source.Split('\n')
            .Where(line => line.Contains("RegisterBinding(typeof(global::NeedsDefaultConstructor<,>)")).ToArray();
        constrainedBindings.ShouldNotBeEmpty();
        constrainedBindings.ShouldAllBe(line => line.EndsWith(", null);", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Metadata generator output does not change when a behavior method body changes")]
    public void GeneratorShouldKeepMetadataStableForMethodBodyChanges()
    {
        var original = Generate(Source);
        var changed = Generate(Source.Replace("Task.FromResult(Result.Success())", "Task.FromResult(Result.Success()).ContinueWith(task => task.Result)"));

        changed.ShouldBe(original);
    }

    [Fact(DisplayName = "Metadata generator discovers closed generic types and generic method registrations")]
    public void GeneratorShouldDiscoverReferencedTypesAndHandlerResults()
    {
        var source = Generate(Source + """
            public sealed class GenericRequest<T> : IRequest<Result>;
            public static class Registration
            {
                public static void Add<T>() {}
                public static void Configure<T>() where T : IRequest<Result>
                {
                    Add<Behavior<Request, Result>>();
                    Add<GenericRequest<int>>();
                    _ = typeof(Behavior<,>);
                    _ = typeof(Behavior<T, Result>);
                    _ = typeof(T);
                    _ = typeof(int[]);
                }
                public static Task<(Result<int>, string[])> Handle(Request request) => null!;
                public static ValueTask<Result> Handle(DerivedRequest request) => default;
                public static Result[] Other(Request request) => [];
            }
            """);

        source.ShouldContain("typeof(global::Behavior<global::Request, global::PANiXiDA.Core.ResultPattern.Result>)");
        source.ShouldContain("typeof(global::GenericRequest<global::System.Int32>)");
        source.ShouldContain("typeof(global::BaseBehavior), typeof(global::Request), typeof(global::PANiXiDA.Core.ResultPattern.Result<global::System.Int32>)");
        source.ShouldNotContain("typeof(T)");
    }

    [Fact(DisplayName = "Metadata generator preserves reference, value, unmanaged, and constructor constraints")]
    public void GeneratorShouldPreserveGenericConstraints()
    {
        var source = Generate(Source + """
            public sealed class DefaultRequest : IRequest<Result>;
            public abstract class AbstractRequest : IRequest<Result>;
            public sealed class PrivateConstructorRequest : IRequest<Result>
            {
                private PrivateConstructorRequest() {}
            }
            public struct ValueRequest : IRequest<Result>;
            public struct ManagedValueRequest : IRequest<Result> { public string Value; }
            public class ReferenceBehavior<TRequest, TResult> : Behavior<TRequest, TResult>
                where TRequest : class, IRequest<TResult> where TResult : Result
            {
                public ReferenceBehavior() : base(new Dependency()) {}
            }
            public class ValueBehavior<TRequest, TResult> : Behavior<TRequest, TResult>
                where TRequest : struct, IRequest<TResult> where TResult : Result
            {
                public ValueBehavior() : base(new Dependency()) {}
            }
            public class UnmanagedBehavior<TRequest, TResult> : Behavior<TRequest, TResult>
                where TRequest : unmanaged, IRequest<TResult> where TResult : Result
            {
                public UnmanagedBehavior() : base(new Dependency()) {}
            }
            public class WrongArityBehavior<TRequest> : Behavior<TRequest, Result> where TRequest : IRequest<Result>
            {
                public WrongArityBehavior() : base(new Dependency()) {}
            }
            """);

        source.ShouldContain("typeof(global::ReferenceBehavior<global::DefaultRequest, global::PANiXiDA.Core.ResultPattern.Result>)");
        source.ShouldNotContain("typeof(global::ReferenceBehavior<global::ValueRequest,");
        source.ShouldContain("typeof(global::UnmanagedBehavior<global::ValueRequest, global::PANiXiDA.Core.ResultPattern.Result>)");
        source.ShouldNotContain("typeof(global::UnmanagedBehavior<global::ManagedValueRequest,");
        source.ShouldContain("typeof(global::NeedsDefaultConstructor<global::DefaultRequest, global::PANiXiDA.Core.ResultPattern.Result>)");
        source.ShouldNotContain("typeof(global::NeedsDefaultConstructor<global::AbstractRequest,");
        source.ShouldNotContain("typeof(global::NeedsDefaultConstructor<global::PrivateConstructorRequest,");
        source.ShouldContain("typeof(global::WrongArityBehavior<>), typeof(global::DefaultRequest)");
    }

    [Fact(DisplayName = "Metadata generator skips inaccessible and unsupported request types")]
    public void GeneratorShouldSkipInaccessibleRequests()
    {
        var source = Generate(Source + """
            file sealed class FileRequest : IRequest<Result>;
            public class Container<T> { public sealed class NestedRequest : IRequest<Result>; }
            public static class Handler
            {
                private sealed class HiddenRequest : IRequest<Result>;
                private sealed class HiddenResult() : Result(true, []);
                public static System.Type Hidden => typeof(HiddenRequest);
                private static Result Handle(HiddenRequest request) => null!;
                private static HiddenResult Handle(Request request) => null!;
            }
            public sealed class UnrelatedRequest : IRequest<string>;
            public sealed class ArrayRequest : IRequest<Result[]>;
            """);

        source.ShouldNotContain("FileRequest");
        source.ShouldNotContain("HiddenRequest");
        source.ShouldNotContain("HiddenResult");
        source.ShouldNotContain("NestedRequest");
        source.ShouldNotContain("RegisterBinding(typeof(global::BaseBehavior), typeof(global::UnrelatedRequest)");
    }

    [Theory(DisplayName = "Metadata generator remains inactive without the adapter or required contracts")]
    [InlineData("")]
    [InlineData("namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation { public class RequestBehaviorMetadata; }")]
    public void GeneratorShouldSkipCompilationsWithoutRequiredReferences(string source)
    {
        Generate(source, includePackages: false).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Metadata generator emits nothing when no behaviors are available")]
    public void GeneratorShouldSkipEmptyRegistrations()
    {
        const string source = """
            namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation { public class RequestBehaviorMetadata; }
            namespace PANiXiDA.Core.Application.Messaging.Mediator.Behaviors.Abstractions
            {
                public interface IBeforeRequestBehavior<TRequest, TResult>;
                public interface IAfterRequestBehavior<TRequest, TResult>;
                public interface IFinallyRequestBehavior<TRequest, TResult>;
            }
            namespace PANiXiDA.Core.Application.Messaging.Mediator.Contracts { public interface IRequest<T>; }
            namespace PANiXiDA.Core.ResultPattern { public class Result; }
            """;

        Generate(source, includePackages: false).ShouldBeEmpty();
        Generate(source.Replace("public interface IRequest<T>;", ""), includePackages: false).ShouldBeEmpty();
        Generate(source.Replace("public class Result;", ""), includePackages: false).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Metadata generator tolerates incomplete generic expressions during editing")]
    public void GeneratorShouldTolerateIncompleteSource()
    {
        var source = Generate(Source + "public class Incomplete { public void Run() { Missing<Unknown>(); } }", requireValidCompilation: false);

        source.ShouldContain("typeof(global::Behavior<,>)");
        source.ShouldNotContain("Missing");
    }

    [Theory(DisplayName = "Metadata generator discovers types in referenced application and adapter helper assemblies")]
    [InlineData("PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation.RequestBehaviorMetadata")]
    [InlineData("PANiXiDA.Core.Application.Messaging.Mediator.Contracts.IRequest<int>")]
    public void GeneratorShouldInspectReferencedAdapterAssemblies(string referencedType)
    {
        var referencedCompilation = CSharpCompilation.Create("AdapterHelpers",
            [CSharpSyntaxTree.ParseText($$"""
                public class AdapterHelper
                {
                    public static System.Type Registry => typeof({{referencedType}});
                }
                """, cancellationToken: TestContext.Current.CancellationToken)], References(true), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        referencedCompilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.ShouldBeTrue();
        var reference = MetadataReference.CreateFromImage(stream.ToArray());

        var source = Generate("public static class Registration { public static System.Type Helper => typeof(AdapterHelper); }", additionalReference: reference);

        source.ShouldContain("RegisterBehavior(typeof(global::AdapterHelper), 1,");
    }

    [Fact(DisplayName = "Request pair equality compares both compiler symbols")]
    public void RequestPairEqualityShouldCompareBothSymbols()
    {
        var compilation = CSharpCompilation.Create("Pairs", [CSharpSyntaxTree.ParseText("class A; class B;", cancellationToken: TestContext.Current.CancellationToken)]);
        var a = compilation.GetTypeByMetadataName("A")!;
        var b = compilation.GetTypeByMetadataName("B")!;
        var comparerType = typeof(RequestBehaviorMetadataGenerator).GetNestedType("RequestPairComparer", System.Reflection.BindingFlags.NonPublic)!;
        var comparer = (IEqualityComparer<(INamedTypeSymbol, INamedTypeSymbol)>)Activator.CreateInstance(comparerType, nonPublic: true)!;

        comparer.Equals((a, b), (a, b)).ShouldBeTrue();
        comparer.Equals((a, b), (a, a)).ShouldBeFalse();
        comparer.Equals((a, b), (b, b)).ShouldBeFalse();
    }

    private static IEnumerable<MetadataReference> References(bool includePackages)
    {
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => includePackages || !Path.GetFileName(path).StartsWith("PANiXiDA.", StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static string Generate(string source, bool requireValidCompilation = true, bool includePackages = true, MetadataReference? additionalReference = null)
    {
        var references = References(includePackages);
        if (additionalReference is not null)
        {
            references = references.Append(additionalReference);
        }

        var compilation = CSharpCompilation.Create("GeneratedMetadataTest", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RequestBehaviorMetadataGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        if (requireValidCompilation)
        {
            output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        }

        return string.Join("\n", driver.GetRunResult().Results.Single().GeneratedSources.Select(item => item.SourceText.ToString()));
    }
}
