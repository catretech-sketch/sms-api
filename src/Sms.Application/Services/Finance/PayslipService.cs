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

public sealed class PayslipService(
    PayslipRepository repo,
    IPayrollService payroll,
    ITenantContext tenant) : IPayslipService
{
    public async Task<ApiResult<IReadOnlyList<PayslipResponse>>> ListAsync(Guid? userId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<IReadOnlyList<PayslipResponse>>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<PayslipResponse>>.Fail(new Error("forbidden", "no user context"), 403);

        var list = await repo.ListAsync(tid, userId ?? uid, ct);
        if (list.Count == 0)
        {
            // Self-heal: approved payroll rows exist but Payslips were never published (e.g. missing UserId link).
            await payroll.RepublishApprovedPayslipsForUserAsync(ct);
            list = await repo.ListAsync(tid, userId ?? uid, ct);
        }

        return ApiResult<IReadOnlyList<PayslipResponse>>.Ok(list);
    }

    public async Task<ApiResult<PayslipResponse>> CreateAsync(CreatePayslipRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<PayslipResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<PayslipResponse>.Ok((await repo.CreateAsync(tid, req, ct))!, 201);
    }
}
