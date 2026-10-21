using System.Text;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Behaviors;

internal static class RequestBehaviorMetadataSourceBuilder
{
    internal static string Build(IEnumerable<string> registrations)
    {
        var source = new StringBuilder("""
            #nullable enable
            #pragma warning disable CA2255

            [global::System.CodeDom.Compiler.GeneratedCode("PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators", "1.0.0")]
            file static class GeneratedWolverineRequestBehaviors
            {
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Initialize()
                {

            """);

        foreach (var registration in registrations)
        {
            source
                .Append("        global::PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation.")
                .Append("RequestBehaviorMetadata.")
                .Append(registration)
                .Append('\n');
        }

        return source
            .Append("    }\n}\n")
            .ToString();
    }
}
