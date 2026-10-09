using PANiXiDA.Core.Application.Authentication.Abstractions;

using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;

public sealed class IntegrationCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; }
    public Guid? UserId => null;
    public string? UserName => null;
    public IReadOnlyCollection<string> Roles => [];
    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

    public bool HasPermission(string permission)
    {
        return IsAuthenticated && Permissions.Contains(permission);
    }

    public bool TryGetClaimValue<T>(string claimType, [MaybeNullWhen(false)] out T value)
        where T : IParsable<T>
    {
        value = default;
        return false;
    }
}
