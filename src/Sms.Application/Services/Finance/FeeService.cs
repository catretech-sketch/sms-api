using Sms.Application.Common;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Finance;

public interface IFeeService
{
    Task<ApiResult<IReadOnlyList<FeePaymentResponse>>> ListPaymentsAsync(Guid? studentId, CancellationToken ct = default);
    Task<ApiResult<FeePaymentResponse>> CreatePaymentAsync(CreateFeePaymentRequest req, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<FeeInvoiceResponse>>> ListInvoicesAsync(Guid? studentId, CancellationToken ct = default);
    Task<ApiResult<FeeInvoiceResponse>> CreateInvoiceAsync(CreateFeeInvoiceRequest req, CancellationToken ct = default);
    Task<ApiResult<FeeInvoiceResponse>> PayInvoiceAsync(Guid id, CancellationToken ct = default);
}

public sealed class FeeService(
    FeeRepository payments,
    FeeInvoiceRepository invoices,
    IPaymentGateway gateway,
    ITenantContext tenant) : IFeeService
{
    public async Task<ApiResult<IReadOnlyList<FeePaymentResponse>>> ListPaymentsAsync(Guid? studentId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<FeePaymentResponse>>.Ok(await payments.ListAsync(studentId, ct));

    public async Task<ApiResult<FeePaymentResponse>> CreatePaymentAsync(CreateFeePaymentRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeePaymentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<FeePaymentResponse>.Ok((await payments.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<FeeInvoiceResponse>>> ListInvoicesAsync(Guid? studentId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<FeeInvoiceResponse>>.Ok(await invoices.ListAsync(studentId, ct));

    public async Task<ApiResult<FeeInvoiceResponse>> CreateInvoiceAsync(CreateFeeInvoiceRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<FeeInvoiceResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<FeeInvoiceResponse>.Ok((await invoices.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<FeeInvoiceResponse>> PayInvoiceAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await invoices.GetAsync(id, ct);
        if (inv is null)
            return ApiResult<FeeInvoiceResponse>.Fail(new Error("not_found", "resource not found"), 404);
        if (inv.Status == "paid")
            return ApiResult<FeeInvoiceResponse>.Fail(new Error("conflict", "invoice already paid"), 409);
        var result = await gateway.ChargeAsync(inv.Amount, "INR");
        if (!result.Success)
            return ApiResult<FeeInvoiceResponse>.Fail(new Error("conflict", "payment failed"), 409);
        return ApiResult<FeeInvoiceResponse>.Ok((await invoices.MarkPaidAsync(id, result.Method, ct))!);
    }
}
