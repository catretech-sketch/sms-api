namespace Sms.Modules.Tenancy.Contracts;

// ---- Onboarding ----
public sealed record OnboardingChecklistItem(string Label, bool Done);
public sealed record OnboardingItemResponse(
    Guid Id, Guid? TenantId, string Name, string Slug, string? Owner, decimal Value, string Stage,
    IReadOnlyList<OnboardingChecklistItem> Checklist, int Done, int Age);
public sealed record CreateOnboardingRequest(string Name, string Slug, string? Owner, decimal Value, string? Stage);
public sealed record AdvanceRequest(string Stage);
public sealed record ChecklistRequest(string Label, bool Done);
public sealed record OnboardingItemRow(
    Guid Id, Guid? TenantId, string Name, string Slug, string? Owner, decimal Value, string Stage, int Age);
public sealed record ChecklistRow(Guid OnboardingId, string Label, bool Done);

// ---- Support tickets ----
public sealed record TicketResponse(
    Guid Id, string Subject, Guid? TenantId, string? TenantName, string Status, string Priority,
    string? Assignee, DateTime Created, DateTime Updated, int MessagesCount);
public sealed record TicketMessageResponse(Guid Id, Guid TicketId, string? Who, string Role, string Text, DateTime When);
public sealed record TicketDetailResponse(
    Guid Id, string Subject, Guid? TenantId, string? TenantName, string Status, string Priority,
    string? Assignee, DateTime Created, DateTime Updated, int MessagesCount,
    IReadOnlyList<TicketMessageResponse> Messages);
public sealed record CreateTicketRequest(string Subject, Guid? TenantId, string? TenantName, string? Priority);
public sealed record UpdateTicketRequest(string? Status, string? Assignee);
public sealed record AddMessageRequest(string Text);

// ---- Team ----
public sealed record TeamMemberResponse(
    Guid Id, string Name, string Email, string Role, string Status, DateTime? LastLogin, DateTime Joined);
public sealed record InviteTeamRequest(string Name, string Email, string Role);
public sealed record UpdateTeamRequest(string? Role, string? Status);

// ---- Audit ----
public sealed record AuditEntry(
    Guid Id, Guid? ActorId, string? ActorName, string? Role, string Action, string? Target, string? Kind, DateTime Time);

// ---- Reports ----
public sealed record PlanPerf(string PlanName, int Clients, decimal Mrr, decimal SharePct);
public sealed record RevenueReport(
    decimal Arr, int NetGrowth, decimal GrossChurnPct, decimal Arpa,
    IReadOnlyList<string> Months, IReadOnlyList<decimal> RevenueSeries,
    IReadOnlyList<PlanMixItem> RevenueByPlan, IReadOnlyList<PlanPerf> PlanPerformance);
