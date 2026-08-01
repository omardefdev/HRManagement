using Microsoft.AspNetCore.SignalR;

namespace HRManagement.ServiceDefaults;

public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all");
        await base.OnConnectedAsync();
    }
}

public static class NotificationHubExtensions
{
    public static async Task SendNotificationAsync(this IHubContext<NotificationHub> hub,
        string title, string message, string type = "info")
    {
        await hub.Clients.Group("all").SendAsync("ReceiveNotification", new
        {
            title,
            message,
            type,
            timestamp = DateTime.UtcNow
        });
    }
}
