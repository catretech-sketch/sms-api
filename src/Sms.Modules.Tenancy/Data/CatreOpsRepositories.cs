using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class TeamRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, Name, Email, Role, Status, LastLogin, Joined";

    public Task<TeamMemberResponse?> InviteAsync(InviteTeamRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TeamMemberResponse>("dbo.Team_Invite", new { r.Name, r.Email, r.Role }, ct);

    public Task<IReadOnlyList<TeamMemberResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<TeamMemberResponse>($"SELECT {Cols} FROM dbo.TeamMembers ORDER BY Joined DESC", null, ct);

    public Task<TeamMemberResponse?> UpdateAsync(Guid id, string? role, string? status, CancellationToken ct = default) =>
        QuerySingleProcAsync<TeamMemberResponse>("dbo.Team_Update", new { Id = id, Role = role, Status = status }, ct);
}

public sealed class AuditRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<IReadOnlyList<AuditEntry>> ListAsync(
        string? kind, Guid? actorId, Guid? tenantId, CancellationToken ct = default) =>
        QueryInlineAsync<AuditEntry>(
            "SELECT Id, ActorId, ActorName, Role, Action, Target, Kind, At AS [Time] FROM dbo.AuditLog " +
            "WHERE (@kind IS NULL OR Kind = @kind) AND (@actorId IS NULL OR ActorId = @actorId) " +
            "AND (@tenantId IS NULL OR TenantId = @tenantId) ORDER BY At DESC",
            new { kind, actorId, tenantId }, ct);
}

public sealed class ReportRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record Headline(decimal TotalMrr, int ActiveCount);
    private sealed record PlanAgg(string PlanName, int Clients, decimal Mrr);

    public async Task<RevenueReport> RevenueAsync(CancellationToken ct = default)
    {
        var h = (await QueryInlineAsync<Headline>(
            "SELECT ISNULL(SUM(CASE WHEN Status='active' THEN Mrr ELSE 0 END),0) AS TotalMrr, " +
            "SUM(CASE WHEN Status='active' THEN 1 ELSE 0 END) AS ActiveCount FROM dbo.Tenants", null, ct)).First();

        var perPlan = await QueryInlineAsync<PlanAgg>(
            "SELECT PlanName, COUNT(*) AS Clients, ISNULL(SUM(Mrr),0) AS Mrr FROM dbo.Tenants " +
            "WHERE PlanName IS NOT NULL GROUP BY PlanName ORDER BY Mrr DESC", null, ct);

        var totalMrr = h.TotalMrr;
        var arpa = h.ActiveCount > 0 ? Math.Round(totalMrr / h.ActiveCount, 2) : 0m;
        var perf = perPlan.Select(p => new PlanPerf(p.PlanName, p.Clients, p.Mrr,
            totalMrr > 0 ? Math.Round(p.Mrr / totalMrr * 100, 1) : 0m)).ToList();
        var byPlan = perPlan.Select(p => new PlanMixItem(p.PlanName, p.Clients, null)).ToList();

        return new RevenueReport(totalMrr * 12, 0, 0m, arpa, [], [], byPlan, perf);
    }
}
