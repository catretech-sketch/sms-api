namespace Sms.Modules.Sports.Contracts;

public sealed record SportsTeamResponse(Guid Id, Guid TenantId, string Name, string Sport, string? Coach, int Athletes);
public sealed record CreateSportsTeamRequest(string Name, string Sport, string? Coach, int Athletes);

public sealed record SportsEventResponse(Guid Id, Guid TenantId, string Name, DateTime EventDate, string? Venue);
public sealed record CreateSportsEventRequest(string Name, DateTime EventDate, string? Venue);

public sealed record SportsMedalResponse(Guid Id, Guid TenantId, string Kind, string? Title, int Year);
public sealed record CreateSportsMedalRequest(string Kind, string? Title, int? Year);

/// Aggregate KPIs for the Operations · Sports dashboard. Medals counts the current calendar year.
public sealed record SportsSummaryResponse(int Teams, int Events, int Athletes, int Medals);
