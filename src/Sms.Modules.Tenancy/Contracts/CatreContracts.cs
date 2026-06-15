namespace Sms.Modules.Tenancy.Contracts;

// ---- Client (Tenant) ----
public sealed record ClientLimits(int? Students, int? Staff, int? StorageGb);

public sealed record ClientResponse(
    Guid Id, string Name, string Slug, string? Country, string Status,
    Guid? PlanId, string? PlanName, string? Tier, decimal Mrr,
    int StudentsCount, int StaffCount, decimal StorageGb, ClientLimits Limits,
    DateTime Created, string? Csm, int HealthScore);

public sealed record CreateClientRequest(
    string Name, string Slug, string? Country, string? AdminName, string? AdminEmail,
    string? AdminPhone, Guid PlanId, int TrialDays, string? Csm);

public sealed record SetStatusRequest(string Status, string? Reason);
public sealed record ChangePlanRequest(Guid PlanId);

// Flat DB row (Dapper maps columns by name); composed into ClientResponse.
public sealed record ClientRow(
    Guid Id, string Name, string Slug, string? Country, string Status, Guid? PlanId, string? PlanName,
    string? Tier, decimal Mrr, int StudentsCount, int StaffCount, decimal StorageGb,
    int? LimitsStudents, int? LimitsStaff, int? LimitsStorageGb, DateTime CreatedAt, string? Csm, int HealthScore);

// ---- Plan ----
public sealed record PlanLimits(int Students, int Staff, int StorageGb);
public sealed record PlanOffer(string Label, int Pct);

public sealed record PlanResponse(
    Guid Id, string Name, string Tier, string Pricing, decimal Price, decimal? PerStudent,
    int? MinStudents, string Period, IReadOnlyList<string> Features, PlanLimits Limits,
    string Visibility, string Audience, string? Band, PlanOffer? Offer, string? Color, string? Description);

public sealed record PlanUpsertRequest(
    Guid? Id, string Name, string Tier, string Pricing, decimal Price, decimal? PerStudent, int? MinStudents,
    string Period, IReadOnlyList<string>? Features, PlanLimits Limits, string Visibility, string Audience,
    string? Band, PlanOffer? Offer, string? Color, string? Description);

public sealed record PlanRow(
    Guid Id, string Name, string Tier, string Pricing, decimal Price, decimal? PerStudent, int? MinStudents,
    string Period, string? FeaturesCsv, int LimitsStudents, int LimitsStaff, int LimitsStorageGb,
    string Visibility, string Audience, string? Band, string? OfferLabel, int? OfferPct, string? Color, string? Description);

public static class CatreMappers
{
    public static ClientResponse ToResponse(this ClientRow r) => new(
        r.Id, r.Name, r.Slug, r.Country, r.Status, r.PlanId, r.PlanName, r.Tier, r.Mrr,
        r.StudentsCount, r.StaffCount, r.StorageGb,
        new ClientLimits(r.LimitsStudents, r.LimitsStaff, r.LimitsStorageGb),
        r.CreatedAt, r.Csm, r.HealthScore);

    public static PlanResponse ToResponse(this PlanRow r) => new(
        r.Id, r.Name, r.Tier, r.Pricing, r.Price, r.PerStudent, r.MinStudents, r.Period,
        string.IsNullOrEmpty(r.FeaturesCsv) ? [] : r.FeaturesCsv.Split(','),
        new PlanLimits(r.LimitsStudents, r.LimitsStaff, r.LimitsStorageGb),
        r.Visibility, r.Audience, r.Band,
        r.OfferLabel is null ? null : new PlanOffer(r.OfferLabel, r.OfferPct ?? 0),
        r.Color, r.Description);
}
