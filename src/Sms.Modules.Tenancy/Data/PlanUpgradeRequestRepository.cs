using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class PlanUpgradeRequestRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<PlanUpgradeRequestResponse?> CreateAsync(
        Guid tenantId, Guid? fromPlanId, Guid toPlanId, decimal amount, string currency,
        string mode, string status, Guid? requestedByUserId, CancellationToken ct = default) =>
        QuerySingleProcAsync<PlanUpgradeRequestResponse>("dbo.PlanUpgradeRequest_Create", new
        {
            TenantId = tenantId,
            FromPlanId = fromPlanId,
            ToPlanId = toPlanId,
            Amount = amount,
            Currency = currency,
            Mode = mode,
            Status = status,
            RequestedByUserId = requestedByUserId,
        }, ct);

    public Task<PlanUpgradeRequestResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        QuerySingleProcAsync<PlanUpgradeRequestResponse>("dbo.PlanUpgradeRequest_Get", new { Id = id }, ct);

    public Task<PlanUpgradeRequestResponse?> GetByOrderAsync(string razorpayOrderId, CancellationToken ct = default) =>
        QuerySingleProcAsync<PlanUpgradeRequestResponse>("dbo.PlanUpgradeRequest_GetByOrder",
            new { RazorpayOrderId = razorpayOrderId }, ct);

    public Task<IReadOnlyList<PlanUpgradeRequestResponse>> ListAsync(string? status, CancellationToken ct = default) =>
        QueryProcAsync<PlanUpgradeRequestResponse>("dbo.PlanUpgradeRequest_List", new { Status = status }, ct);

    public Task<IReadOnlyList<PlanUpgradeRequestResponse>> ListByTenantsAsync(
        IReadOnlyList<Guid> tenantIds, CancellationToken ct = default)
    {
        if (tenantIds.Count == 0)
            return Task.FromResult<IReadOnlyList<PlanUpgradeRequestResponse>>([]);
        var csv = string.Join(',', tenantIds);
        return QueryProcAsync<PlanUpgradeRequestResponse>("dbo.PlanUpgradeRequest_ListByTenants",
            new { TenantIds = csv }, ct);
    }

    public Task<PlanUpgradeRequestResponse?> SetStatusAsync(
        Guid id, string status, Guid? reviewedByUserId, string? notes, CancellationToken ct = default) =>
        QuerySingleProcAsync<PlanUpgradeRequestResponse>("dbo.PlanUpgradeRequest_SetStatus", new
        {
            Id = id,
            Status = status,
            ReviewedByUserId = reviewedByUserId,
            Notes = notes,
        }, ct);

    public Task<PlanUpgradeRequestResponse?> AttachRazorpayAsync(
        Guid id, string? orderId, string? paymentId, string? status, CancellationToken ct = default) =>
        QuerySingleProcAsync<PlanUpgradeRequestResponse>("dbo.PlanUpgradeRequest_AttachRazorpay", new
        {
            Id = id,
            RazorpayOrderId = orderId,
            RazorpayPaymentId = paymentId,
            Status = status,
        }, ct);

    public Task<PlanUpgradeRequestResponse?> AttachInvoiceAsync(Guid id, Guid invoiceId, CancellationToken ct = default) =>
        QuerySingleProcAsync<PlanUpgradeRequestResponse>("dbo.PlanUpgradeRequest_AttachInvoice",
            new { Id = id, InvoiceId = invoiceId }, ct);
}
