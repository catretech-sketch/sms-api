using Sms.Application.Common;
using Sms.Modules.Hostel.Contracts;

namespace Sms.Application.Services.Hostel;

public interface IHostelService
{
    Task<ApiResult<HostelSummaryResponse>> GetSummaryAsync(CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<HostelBlockResponse>>> ListBlocksAsync(CancellationToken ct = default);
    Task<ApiResult<HostelBlockResponse>> CreateBlockAsync(CreateHostelBlockRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<HostelRoomResponse>>> ListRoomsAsync(CancellationToken ct = default);
    Task<ApiResult<HostelRoomResponse>> CreateRoomAsync(CreateHostelRoomRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<HostelResidentResponse>>> ListResidentsAsync(CancellationToken ct = default);
    Task<ApiResult<HostelResidentResponse>> CreateResidentAsync(CreateHostelResidentRequest req, CancellationToken ct = default);
}
