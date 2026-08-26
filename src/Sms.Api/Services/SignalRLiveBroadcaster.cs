using Microsoft.AspNetCore.SignalR;
using Sms.Api.Hubs;
using Sms.Application.Services.Realtime;

namespace Sms.Api.Services;

public sealed class SignalRLiveBroadcaster(IHubContext<LiveHub> hub) : ILiveBroadcaster
{
    public async Task PublishAsync(Guid tenantId, string type, object? data = null, CancellationToken ct = default)
    {
        try
        {
            object payload = data is null ? new { type } : new { type, data };
            await hub.Clients.Group(LiveGroups.Tenant(tenantId)).SendAsync("live_event", payload, ct);
        }
        catch
        {
            /* live push is best-effort — never fail the write path */
        }
    }
}
