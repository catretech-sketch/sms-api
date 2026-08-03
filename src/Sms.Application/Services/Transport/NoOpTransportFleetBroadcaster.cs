namespace Sms.Application.Services.Transport;

/// <summary>Default fleet broadcaster when SignalR hub wiring is not configured.</summary>
internal sealed class NoOpTransportFleetBroadcaster : ITransportFleetBroadcaster
{
    public Task BroadcastFleetAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
}
