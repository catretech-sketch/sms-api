using System.Data;
using Dapper;
using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class TeamRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, Name, Email, Role, Status, LastLogin, Joined, EmployeeId, PhotoUrl, Phone";

    private sealed record TeamMemberRow(
        Guid Id, string Name, string Email, string Role, string Status, DateTime? LastLogin, DateTime Joined,
        string? EmployeeId, string? PhotoUrl, string? Phone);

    private sealed record DocContentRow(
        Guid Id, Guid TeamMemberId, string Label, string FileName, string ContentType, int SizeBytes, DateTime Created, string Content);

    public async Task<TeamMemberResponse?> InviteAsync(InviteTeamRequest r, CancellationToken ct = default)
    {
        var row = await QuerySingleProcAsync<TeamMemberRow>("dbo.Team_Invite",
            new { r.Name, r.Email, r.Role, r.EmployeeId, r.PhotoUrl, r.Phone }, ct);
        return row is null ? null : await GetComposedAsync(row.Id, ct);
    }

    public async Task<IReadOnlyList<TeamMemberResponse>> ListAsync(CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<TeamMemberRow>($"SELECT {Cols} FROM dbo.TeamMembers ORDER BY Joined DESC", null, ct);
        var docs = await QueryInlineAsync<TeamDocumentMeta>(
            "SELECT Id, TeamMemberId, Label, FileName, ContentType, SizeBytes, CreatedAt AS Created " +
            "FROM dbo.TeamDocuments ORDER BY CreatedAt DESC", null, ct);
        var byMember = docs.GroupBy(d => d.TeamMemberId).ToDictionary(g => g.Key, g => (IReadOnlyList<TeamDocumentMeta>)g.ToList());
        return rows.Select(r => Map(r, byMember.TryGetValue(r.Id, out var list) ? list : [])).ToList();
    }

    public async Task<TeamMemberResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        await GetComposedAsync(id, ct);

    public async Task<TeamMemberResponse?> UpdateAsync(Guid id, UpdateTeamRequest r, CancellationToken ct = default)
    {
        var row = await QuerySingleProcAsync<TeamMemberRow>("dbo.Team_Update",
            new { Id = id, r.Role, r.Status, r.Name, r.EmployeeId, r.PhotoUrl, r.Phone }, ct);
        return row is null ? null : await GetComposedAsync(id, ct);
    }

    public Task<TeamDocumentMeta?> AddDocumentAsync(Guid memberId, TeamDocumentInput doc, int sizeBytes, CancellationToken ct = default) =>
        QuerySingleProcAsync<TeamDocumentMeta>("dbo.TeamDocument_Add", new
        {
            TeamMemberId = memberId,
            doc.Label,
            doc.FileName,
            doc.ContentType,
            SizeBytes = sizeBytes,
            doc.Content,
        }, ct);

    public async Task<bool> DeleteDocumentAsync(Guid memberId, Guid docId, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.TeamDocuments WHERE Id = @docId AND TeamMemberId = @memberId",
            new { docId, memberId }, cancellationToken: ct));
        return n > 0;
    }

    public async Task<TeamDocumentDetail?> GetDocumentAsync(Guid memberId, Guid docId, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<DocContentRow>(
            "SELECT Id, TeamMemberId, Label, FileName, ContentType, SizeBytes, CreatedAt AS Created, Content " +
            "FROM dbo.TeamDocuments WHERE Id = @docId AND TeamMemberId = @memberId",
            new { docId, memberId }, ct);
        var r = rows.FirstOrDefault();
        return r is null ? null : new TeamDocumentDetail(r.Id, r.TeamMemberId, r.Label, r.FileName, r.ContentType, r.SizeBytes, r.Created, r.Content);
    }

    private async Task<TeamMemberResponse?> GetComposedAsync(Guid id, CancellationToken ct)
    {
        var rows = await QueryInlineAsync<TeamMemberRow>($"SELECT {Cols} FROM dbo.TeamMembers WHERE Id = @id", new { id }, ct);
        var row = rows.FirstOrDefault();
        if (row is null) return null;
        var docs = await QueryInlineAsync<TeamDocumentMeta>(
            "SELECT Id, TeamMemberId, Label, FileName, ContentType, SizeBytes, CreatedAt AS Created " +
            "FROM dbo.TeamDocuments WHERE TeamMemberId = @id ORDER BY CreatedAt DESC", new { id }, ct);
        return Map(row, docs);
    }

    private static TeamMemberResponse Map(TeamMemberRow r, IReadOnlyList<TeamDocumentMeta> docs) =>
        new(r.Id, r.Name, r.Email, r.Role, r.Status, r.LastLogin, r.Joined, r.EmployeeId, r.PhotoUrl, r.Phone, docs);
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

    public Task<AuditEntry?> InsertAsync(
        Guid? actorId, string? actorName, string? role, string action,
        string? target, string? kind, Guid? tenantId, CancellationToken ct = default) =>
        QuerySingleProcAsync<AuditEntry>("dbo.Audit_Insert", new
        {
            ActorId = actorId,
            ActorName = actorName,
            Role = role,
            Action = action,
            Target = target,
            Kind = kind,
            TenantId = tenantId,
        }, ct);
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
