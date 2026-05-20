using System.Security.Claims;

namespace lms_api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static Guid? GetCompanyId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue("CompanyId");
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static string? GetRole(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role);
}
