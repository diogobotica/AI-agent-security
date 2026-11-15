using System.Security.Claims;
using Platform.Core.External;

namespace Platform.Infrastructure.Identity;

/// <summary>
/// Entra External ID (ENVIRONMENT=azure). Maps already-validated token claims to a <see cref="PlatformIdentity"/>:
/// subject from <c>oid</c> (short or long claim type), email from <c>email</c> or <c>preferred_username</c>, display
/// name from <c>name</c>, roles from <c>roles</c>. Token validation itself arrives with US-109.
/// </summary>
public sealed class EntraIdentityProvider : IIdentityProvider
{
    private static readonly string[] SubjectClaimTypes =
    [
        "oid",
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
    ];

    public PlatformIdentity? FromClaims(ClaimsPrincipal principal) => ClaimMapping.Map(principal, SubjectClaimTypes);
}
