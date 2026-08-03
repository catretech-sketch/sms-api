using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Hubs;

[Authorize(Policy = Policies.Principal)]
public sealed class TransportFleetHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        if (tenantId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        await base.OnConnectedAsync();
    }

    public static string TenantGroup(string tenantId) => $"transport-fleet:{tenantId}";
}
