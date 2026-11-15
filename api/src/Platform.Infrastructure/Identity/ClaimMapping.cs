using System.Security.Claims;
using Platform.Core.External;

namespace Platform.Infrastructure.Identity;

/// <summary>Shared claim reading for the identity providers. Claims must already be validated (US-109).</summary>
internal static class ClaimMapping
{
    public static PlatformIdentity? Map(ClaimsPrincipal principal, IReadOnlyList<string> subjectClaimTypes)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = First(principal, subjectClaimTypes);
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var email = First(principal, ["email", "preferred_username"]);
        var displayName = First(principal, ["name"]);
        var roles = principal.FindAll("roles").Select(claim => claim.Value).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return new PlatformIdentity(subject, email, displayName, roles);
    }

    private static string? First(ClaimsPrincipal principal, IReadOnlyList<string> claimTypes) =>
        claimTypes.Select(type => principal.FindFirst(type)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
