using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SecureController : ControllerBase
{
    // 🔐 Any authenticated user
    [Authorize]
    [HttpGet("profile")]
    public IActionResult GetProfile()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;

        return Ok(new
        {
            Message = "Authenticated successfully",
            UserId = userId,
            Email = email,
            Role = role
        });
    }

    // 👑 Admin Only
    [Authorize(Policy = "AdminAccess")]
    [HttpGet("admin-only")]
    public IActionResult AdminOnly()
    {
        return Ok(new
        {
            Message = "Welcome Admin 👑",
            AccessLevel = "Admin"
        });
    }

    // 🧑‍💼 Manager OR Admin
    [Authorize(Policy = "ManagementAccess")]
    [HttpGet("management-area")]
    public IActionResult ManagementArea()
    {
        return Ok(new
        {
            Message = "Welcome to Management Area 🏢",
            AllowedRoles = "Manager, Admin"
        });
    }
}
