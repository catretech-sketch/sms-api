using System.Data;
using Dapper;
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
    private sealed record Headline(decimal TotalMrr, int ActiveCount, int NetGrowth, decimal GrossChurnPct);
    private sealed record PlanAgg(string PlanName, int Clients, decimal Mrr);
    private sealed record RevMonth(string Label, decimal Revenue);

    public async Task<RevenueReport> RevenueAsync(CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "dbo.Report_Revenue", commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var h = await multi.ReadSingleAsync<Headline>();
        var perPlan = (await multi.ReadAsync<PlanAgg>()).ToList();
        var series = (await multi.ReadAsync<RevMonth>()).ToList();

        var arpa = h.ActiveCount > 0 ? Math.Round(h.TotalMrr / h.ActiveCount, 2) : 0m;
        var perf = perPlan.Select(p => new PlanPerf(p.PlanName, p.Clients, p.Mrr,
            h.TotalMrr > 0 ? Math.Round(p.Mrr / h.TotalMrr * 100, 1) : 0m)).ToList();
        var byPlan = perPlan.Select(p => new PlanMixItem(p.PlanName, p.Clients, null)).ToList();

        return new RevenueReport(
            Arr: h.TotalMrr * 12,
            NetGrowth: h.NetGrowth,
            GrossChurnPct: h.GrossChurnPct,
            Arpa: arpa,
            Months: series.Select(s => s.Label).ToList(),
            RevenueSeries: series.Select(s => s.Revenue).ToList(),
            RevenueByPlan: byPlan,
            PlanPerformance: perf);
    }
}
