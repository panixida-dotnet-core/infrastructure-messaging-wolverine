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
        public sealed class Behavior<TRequest, TResult>(Dependency dependency) : IBeforeRequestBehavior<TRequest, TResult>
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

    private static string Generate(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("GeneratedMetadataTest", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RequestBehaviorMetadataGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        return driver.GetRunResult().Results.Single().GeneratedSources.Single().SourceText.ToString();
    }
}
