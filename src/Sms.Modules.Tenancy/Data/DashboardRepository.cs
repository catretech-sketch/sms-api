using System.Data;
using Dapper;
using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class DashboardRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record CountsRow(int Total, int Active, int Trial, int Suspended, int Cancelled, decimal Mrr, int TrialsEnding);
    private sealed record PlanMixRow(string Label, int Value);

    /// One round-trip: counts + MRR, then plan mix (Dapper QueryMultiple over the dashboard proc).
    public async Task<DashboardOverview> OverviewAsync(CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "dbo.Dashboard_CatreOverview", commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var c = await multi.ReadSingleAsync<CountsRow>();
        var mix = (await multi.ReadAsync<PlanMixRow>()).ToList();

        return new DashboardOverview(
            new DashCounts(c.Total, c.Active, c.Trial, c.Suspended, c.Cancelled),
            c.Mrr, c.TrialsEnding, ChurnPct: 0m,
            Months: [], MrrSeries: [], SignupSeries: [],
            PlanMix: mix.Select(m => new PlanMixItem(m.Label, m.Value, null)).ToList(),
            UsageAlerts: [],
            SystemHealth: [new SystemHealthItem("API", "operational", "-", "-")],
            RecentActivity: []);
    }
}
