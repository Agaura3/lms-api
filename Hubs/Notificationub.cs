using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using lms_api.Services;

namespace lms_api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var companyId = Context.User?.FindFirst("CompanyId")?.Value;
        if (Guid.TryParse(companyId, out var companyGuid))
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                DashboardBroadcastService.CompanyGroup(companyGuid));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var companyId = Context.User?.FindFirst("CompanyId")?.Value;
        if (Guid.TryParse(companyId, out var companyGuid))
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                DashboardBroadcastService.CompanyGroup(companyGuid));
        }

        await base.OnDisconnectedAsync(exception);
    }
}
