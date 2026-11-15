using System.Security.Claims;

namespace Platform.Core.External;

/// <summary>The signed-in user as the platform sees it.</summary>
public sealed record PlatformIdentity(string SubjectId, string? Email, string? DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// Maps already-validated claims to a <see cref="PlatformIdentity"/> (Entra External ID in Azure; a mock locally).
/// Token validation is not done here (US-109).
/// </summary>
public interface IIdentityProvider
{
    /// <summary>The identity, or null if the principal has no subject.</summary>
    PlatformIdentity? FromClaims(ClaimsPrincipal principal);
}
