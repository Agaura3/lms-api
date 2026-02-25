using Microsoft.AspNetCore.SignalR;

namespace lms_api.Hubs;

public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}