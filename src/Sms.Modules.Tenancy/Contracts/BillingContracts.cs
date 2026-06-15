namespace Sms.Modules.Tenancy.Contracts;

// ---- Invoice ---- (Dapper maps columns straight onto this record)
public sealed record InvoiceResponse(
    Guid Id, Guid TenantId, string? TenantName, string? PlanName, decimal Amount,
    string Status, DateTime Issued, DateTime Due, DateTime? PaidOn);

// ---- Subscription ----
public sealed record SubscriptionResponse(
    Guid Id, Guid TenantId, Guid PlanId, string Status, DateTime StartedAt, DateTime? RenewsAt, int Seats);

public sealed record CreateSubscriptionRequest(Guid TenantId, Guid PlanId, int Seats);

// ---- Dashboard overview ----
public sealed record DashCounts(int Total, int Active, int Trial, int Suspended, int Cancelled);
public sealed record PlanMixItem(string Label, int Value, string? Color);
public sealed record SystemHealthItem(string Name, string Status, string Latency, string Uptime);

public sealed record DashboardOverview(
    DashCounts Counts, decimal Mrr, int TrialsEnding, decimal ChurnPct,
    IReadOnlyList<string> Months, IReadOnlyList<decimal> MrrSeries, IReadOnlyList<int> SignupSeries,
    IReadOnlyList<PlanMixItem> PlanMix, IReadOnlyList<object> UsageAlerts,
    IReadOnlyList<SystemHealthItem> SystemHealth, IReadOnlyList<object> RecentActivity);
