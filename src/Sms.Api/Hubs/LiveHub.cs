using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sms.Application.Services.Realtime;

namespace Sms.Api.Hubs;

[Authorize]
public sealed class LiveHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst("sub")?.Value;
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(userId, out var uid))
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.User(uid));
        if (Guid.TryParse(tenantId, out var tid))
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Tenant(tid));
        await base.OnConnectedAsync();
    }
}
