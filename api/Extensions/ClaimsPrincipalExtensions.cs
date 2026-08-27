using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using api.Services;

namespace api.Extensions;

public static class ClaimsPrincipalExtensions
{
    // The "sub" claim is AES-encrypted (see CryptoHelper) — this is the one place
    // that should ever decrypt it back into the caller's real user id.
    public static int GetUserId(this ClaimsPrincipal principal, CryptoHelper crypto)
    {
        var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("Missing sub claim.");

        return int.Parse(crypto.Decrypt(subClaim));
    }
}
