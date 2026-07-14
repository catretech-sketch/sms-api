using Sms.Application.Common;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Finance;

public interface IPayslipService
{
    Task<ApiResult<IReadOnlyList<PayslipResponse>>> ListAsync(Guid? userId, CancellationToken ct = default);
    Task<ApiResult<PayslipResponse>> CreateAsync(CreatePayslipRequest req, CancellationToken ct = default);
}

public sealed class PayslipService(PayslipRepository repo, ITenantContext tenant) : IPayslipService
{
    public async Task<ApiResult<IReadOnlyList<PayslipResponse>>> ListAsync(Guid? userId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<PayslipResponse>>.Ok(await repo.ListAsync(userId ?? tenant.UserId, ct));

    public async Task<ApiResult<PayslipResponse>> CreateAsync(CreatePayslipRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<PayslipResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<PayslipResponse>.Ok((await repo.CreateAsync(tid, req, ct))!, 201);
    }
}
