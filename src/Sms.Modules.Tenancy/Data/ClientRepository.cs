using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class ClientRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    /// <summary>
    /// Live SIS counts — denormalized Tenants.StudentsCount/StaffCount stay out of date
    /// unless procs refresh them; portfolio/Catre lists must read live.
    /// </summary>
    private const string SelectLive =
        "SELECT t.Id, t.Name, t.Slug, t.Country, t.Status, t.PlanId, t.PlanName, t.Tier, t.Mrr, " +
        "CAST((SELECT COUNT_BIG(*) FROM dbo.Students s WHERE s.TenantId = t.Id AND s.Status = N'active') AS int) AS StudentsCount, " +
        "CAST(( " +
        "  (SELECT COUNT_BIG(*) FROM dbo.Teachers te WHERE te.TenantId = t.Id AND te.Status = N'active') + " +
        "  (SELECT COUNT_BIG(*) FROM dbo.Staff st WHERE st.TenantId = t.Id AND st.Status = N'active') " +
        ") AS int) AS StaffCount, " +
        "t.StorageGb, t.LimitsStudents, t.LimitsStaff, t.LimitsStorageGb, t.CreatedAt, t.Csm, t.HealthScore, " +
        "t.ContactName, t.ContactEmail, t.ContactPhone, t.Address " +
        "FROM dbo.Tenants t";

    public Task<ClientRow?> CreateAsync(CreateClientRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<ClientRow>("dbo.Client_Create", new
        {
            r.Name, r.Slug, r.Country,
            ContactName = r.AdminName, ContactEmail = r.AdminEmail, ContactPhone = r.AdminPhone,
            r.Address, r.PlanId, r.Csm
        }, ct);

    public Task<ClientRow?> SetStatusAsync(Guid id, string status, CancellationToken ct = default) =>
        QuerySingleProcAsync<ClientRow>("dbo.Client_SetStatus", new { Id = id, Status = status }, ct);

    public Task<ClientRow?> ChangePlanAsync(Guid id, Guid planId, CancellationToken ct = default) =>
        QuerySingleProcAsync<ClientRow>("dbo.Client_ChangePlan", new { Id = id, PlanId = planId }, ct);

    public async Task<ClientRow?> SetMrrAsync(Guid id, decimal mrr, CancellationToken ct = default) =>
        (await QueryInlineAsync<ClientRow>(
            $"UPDATE dbo.Tenants SET Mrr = @mrr WHERE Id = @id; {SelectLive} WHERE t.Id = @id;",
            new { id, mrr }, ct)).FirstOrDefault();

    public async Task<ClientRow?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<ClientRow>($"{SelectLive} WHERE t.Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<ClientRow>> ListAsync(
        string? status, string? tier, string? q, CancellationToken ct = default) =>
        QueryInlineAsync<ClientRow>(
            $"{SelectLive} WHERE t.PlanId IS NOT NULL " +
            "AND (@status IS NULL OR t.Status = @status) " +
            "AND (@tier IS NULL OR t.Tier = @tier) " +
            "AND (@q IS NULL OR t.Name LIKE '%' + @q + '%' OR t.Slug LIKE '%' + @q + '%') " +
            "ORDER BY t.Mrr DESC",
            new { status, tier, q }, ct);

    public Task<IReadOnlyList<ClientRow>> GetManyAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        ids.Count == 0
            ? Task.FromResult<IReadOnlyList<ClientRow>>([])
            : QueryInlineAsync<ClientRow>(
                $"{SelectLive} WHERE t.Id IN @ids ORDER BY t.Mrr DESC",
                new { ids }, ct);
}
