using Sms.Modules.Transport;

namespace Sms.Application.Services.Transport;

public interface ITransportFleetBroadcaster
{
    Task BroadcastFleetAsync(Guid tenantId, CancellationToken ct = default);
}
