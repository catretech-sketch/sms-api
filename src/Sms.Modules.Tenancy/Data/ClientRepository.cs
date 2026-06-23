using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class ClientRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols =
        "Id, Name, Slug, Country, Status, PlanId, PlanName, Tier, Mrr, StudentsCount, StaffCount, StorageGb, " +
        "LimitsStudents, LimitsStaff, LimitsStorageGb, CreatedAt, Csm, HealthScore, " +
        "ContactName, ContactEmail, ContactPhone, Address";

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

    public async Task<ClientRow?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<ClientRow>($"SELECT {Cols} FROM dbo.Tenants WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<ClientRow>> ListAsync(
        string? status, string? tier, string? q, CancellationToken ct = default) =>
        QueryInlineAsync<ClientRow>(
            $"SELECT {Cols} FROM dbo.Tenants WHERE PlanId IS NOT NULL " +
            "AND (@status IS NULL OR Status = @status) " +
            "AND (@tier IS NULL OR Tier = @tier) " +
            "AND (@q IS NULL OR Name LIKE '%' + @q + '%' OR Slug LIKE '%' + @q + '%') " +
            "ORDER BY Mrr DESC",
            new { status, tier, q }, ct);
}
