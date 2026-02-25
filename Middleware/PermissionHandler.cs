using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using lms_api.Data;

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _context;

    public PermissionHandler(AppDbContext context)
    {
        _context = context;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;

        if (role == null)
            return Task.CompletedTask;

        var hasPermission = _context.RolePermissions
            .Any(rp => rp.RoleName == role && rp.PermissionName == requirement.Permission);

        if (hasPermission)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}