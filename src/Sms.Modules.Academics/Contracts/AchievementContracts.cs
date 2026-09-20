namespace Sms.Modules.Academics.Contracts;

public sealed record AchievementResponse(
    string Id,
    string Title,
    DateTime Date,
    string Icon,
    string Hue);

public sealed record AchievementAwardRow(
    Guid Id,
    Guid TenantId,
    Guid StudentId,
    string Title,
    DateTime AwardedOn,
    string Icon,
    string Hue);

public sealed record CreateAchievementRequest(
    Guid StudentId,
    string Title,
    DateTime? AwardedOn = null,
    string? Icon = null,
    string? Hue = null);
