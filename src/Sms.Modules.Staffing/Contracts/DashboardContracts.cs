namespace Sms.Modules.Staffing.Contracts;

// Flattened across driver/conductor — Kind discriminates which fields are meaningful, the rest
// stay null. Only driver/conductor are ever populated (real data exists for them); every other
// staff category gets Kind null and DashboardResponse.RoleCard is omitted entirely, per the "no
// fake data" rule this whole feature area follows — guard/gardener/sweeper/peon have no backing
// data model yet.
public sealed record RoleCardResponse(
    string Kind, string? BusNo, string? RouteName,
    int? LicenseExpiresInDays = null, bool? FitnessOk = null,
    int? OnBoard = null, int? Capacity = null, string? NextStop = null);

public sealed record DashboardResponse(double HoursThisWeek, RoleCardResponse? RoleCard);
