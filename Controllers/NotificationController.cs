using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lms_api.Common;
using lms_api.Data;
using lms_api.Extensions;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetMyNotifications()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Message,
                n.IsRead,
                n.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.SuccessResponse(notifications));
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

        if (notification == null)
            return NotFound(ApiResponse<string>.FailResponse("Notification not found"));

        notification.IsRead = true;
        await _context.SaveChangesAsync();

        return Ok(ApiResponse<string>.SuccessResponse("Marked as read"));
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return Ok(ApiResponse<string>.SuccessResponse("All notifications marked as read"));
    }
}
