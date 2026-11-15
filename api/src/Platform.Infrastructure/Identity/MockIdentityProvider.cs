using System.Security.Claims;
using Platform.Core.External;

namespace Platform.Infrastructure.Identity;

/// <summary>
/// Local development only (ENVIRONMENT=local); never registered in Azure. Maps the claims of a locally issued test
/// principal the same way as Entra, also accepting the standard <c>sub</c> claim as the subject.
/// </summary>
public sealed class MockIdentityProvider : IIdentityProvider
{
    private static readonly string[] SubjectClaimTypes =
    [
        "oid",
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        "sub",
        ClaimTypes.NameIdentifier,
    ];

    public PlatformIdentity? FromClaims(ClaimsPrincipal principal) => ClaimMapping.Map(principal, SubjectClaimTypes);
}
