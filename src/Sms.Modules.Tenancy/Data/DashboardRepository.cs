using System.Data;
using Dapper;
using Sms.Modules.Tenancy.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tenancy.Data;

public sealed class DashboardRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record CountsRow(int Total, int Active, int Trial, int Suspended, int Cancelled,
        decimal Mrr, int TrialsEnding, decimal ChurnPct);
    private sealed record PlanMixRow(string Label, int Value);
    private sealed record MonthRow(string Label, decimal Mrr, int Signups);

    /// One round-trip: counts+churn, plan mix, recent activity, usage alerts, monthly series.
    public async Task<DashboardOverview> OverviewAsync(CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "dbo.Dashboard_CatreOverview", commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var c = await multi.ReadSingleAsync<CountsRow>();
        var mix = (await multi.ReadAsync<PlanMixRow>()).ToList();
        var activity = (await multi.ReadAsync<RecentActivityItem>()).ToList();
        var alerts = (await multi.ReadAsync<UsageAlertItem>()).ToList();
        var months = (await multi.ReadAsync<MonthRow>()).ToList();

        return new DashboardOverview(
            new DashCounts(c.Total, c.Active, c.Trial, c.Suspended, c.Cancelled),
            c.Mrr, c.TrialsEnding, c.ChurnPct,
            Months: months.Select(m => m.Label).ToList(),
            MrrSeries: months.Select(m => m.Mrr).ToList(),
            SignupSeries: months.Select(m => m.Signups).ToList(),
            PlanMix: mix.Select(m => new PlanMixItem(m.Label, m.Value, null)).ToList(),
            UsageAlerts: alerts,
            SystemHealth: [new SystemHealthItem("Database", "operational", "-", "-")],
            RecentActivity: activity);
    }
}
