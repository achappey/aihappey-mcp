using System.Security.Claims;
using Microsoft.Identity.Web;

namespace MCPhappey.Common.Extensions;

public static class HttpExtensions
{
    public static string? GetUserOid(this IEnumerable<Claim> claims) =>
        claims.FirstOrDefault(a => a.Type == ClaimConstants.Oid)?.Value
        ?? claims.FirstOrDefault(a => a.Type == ClaimConstants.ObjectId)?.Value;

    public static string? GetUserUpn(this IEnumerable<Claim> claims) =>
        claims.FirstOrDefault(a => a.Type == ClaimTypes.Upn)?.Value;
}
