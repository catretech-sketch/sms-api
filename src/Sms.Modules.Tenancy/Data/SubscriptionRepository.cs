using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class SubscriptionRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, PlanId, Status, StartedAt, RenewsAt, Seats";

    public Task<SubscriptionResponse?> CreateAsync(CreateSubscriptionRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<SubscriptionResponse>("dbo.Subscription_Create",
            new { r.TenantId, r.PlanId, r.Seats }, ct);

    public async Task<SubscriptionResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<SubscriptionResponse>(
            $"SELECT {Cols} FROM dbo.Subscriptions WHERE Id = @id", new { id }, ct)).FirstOrDefault();

    public Task<IReadOnlyList<SubscriptionResponse>> ListAsync(
        Guid? tenantId, string? status, CancellationToken ct = default) =>
        QueryInlineAsync<SubscriptionResponse>(
            $"SELECT {Cols} FROM dbo.Subscriptions WHERE (@tenantId IS NULL OR TenantId = @tenantId) " +
            "AND (@status IS NULL OR Status = @status) ORDER BY StartedAt DESC",
            new { tenantId, status }, ct);
}
