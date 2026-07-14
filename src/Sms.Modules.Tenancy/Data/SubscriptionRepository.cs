using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class SubscriptionRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    // Alias StartedAt/RenewsAt/Mrr so Dapper fills the frontend-facing SubscriptionResponse fields.
    private const string EnrichedSelect = """
        SELECT s.Id,
               s.TenantId,
               t.Name AS TenantName,
               s.PlanId,
               COALESCE(t.PlanName, p.Name) AS PlanName,
               COALESCE(t.Tier, p.Tier) AS Tier,
               s.Status,
               s.StartedAt AS CurrentPeriodStart,
               s.RenewsAt AS CurrentPeriodEnd,
               CASE
                 WHEN LOWER(ISNULL(p.Pricing, '')) = 'per_student' THEN
                   ISNULL(p.PerStudent, 0) *
                   CASE
                     WHEN ISNULL(t.StudentsCount, 0) >= ISNULL(s.Seats, 0)
                          AND ISNULL(t.StudentsCount, 0) >= ISNULL(p.MinStudents, 0)
                          AND ISNULL(t.StudentsCount, 0) > 0
                       THEN t.StudentsCount
                     WHEN ISNULL(s.Seats, 0) >= ISNULL(p.MinStudents, 0) AND ISNULL(s.Seats, 0) > 0
                       THEN s.Seats
                     WHEN ISNULL(p.MinStudents, 0) > 0 THEN p.MinStudents
                     ELSE 1
                   END
                 ELSE COALESCE(NULLIF(t.Mrr, 0), p.Price, 0)
               END AS NextCharge,
               s.Seats
        FROM dbo.Subscriptions s
        LEFT JOIN dbo.Tenants t ON t.Id = s.TenantId
        LEFT JOIN dbo.Plans p ON p.Id = s.PlanId
        """;

    public async Task<SubscriptionResponse?> CreateAsync(CreateSubscriptionRequest r, CancellationToken ct = default)
    {
        var created = await QuerySingleProcAsync<SubscriptionCreated>("dbo.Subscription_Create",
            new { r.TenantId, r.PlanId, r.Seats }, ct);
        return created is null ? null : await GetAsync(created.Id, ct);
    }

    public async Task<SubscriptionResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<SubscriptionResponse>(
            EnrichedSelect + " WHERE s.Id = @id", new { id }, ct)).FirstOrDefault();

    public Task<IReadOnlyList<SubscriptionResponse>> ListAsync(
        Guid? tenantId, string? status, CancellationToken ct = default) =>
        QueryInlineAsync<SubscriptionResponse>(
            EnrichedSelect +
            " WHERE (@tenantId IS NULL OR s.TenantId = @tenantId)" +
            " AND (@status IS NULL OR s.Status = @status)" +
            " ORDER BY s.StartedAt DESC",
            new { tenantId, status }, ct);
}
