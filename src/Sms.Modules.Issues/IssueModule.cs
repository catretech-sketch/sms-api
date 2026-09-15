using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Issues;

public static class IssueEnums
{
    public static readonly string[] ValidCategories = ["vehicle", "student", "route", "safety", "other"];
    public static readonly string[] ValidPriorities = ["normal", "high", "emergency"];
    public static readonly string[] ValidStatuses = ["open", "in_progress", "resolved", "closed"];
}

public sealed record IssueResponse(
    Guid Id, Guid TenantId, Guid ReporterUserId, string Category, string Title, string Description,
    string Priority, string Status, Guid? VehicleId, Guid? RouteId, Guid? TripId, string? PhotoUrl,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateIssueRequest(
    string Category, string Title, string Description, string Priority, Guid? TripId, string? PhotoUrl);

public sealed record UpdateIssueRequest(string? Status, string? Note);

public sealed record IssueNoteResponse(Guid Id, Guid IssueId, Guid AuthorUserId, string Note, DateTime CreatedAt);

public sealed class IssueRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string IssueCols =
        "Id, TenantId, ReporterUserId, Category, Title, Description, Priority, Status, " +
        "VehicleId, RouteId, TripId, PhotoUrl, CreatedAt, UpdatedAt";

    private sealed record UserIdRow(Guid Id);

    public Task<IReadOnlyList<IssueResponse>> ListAsync(
        string? status, Guid? reporterUserId, CancellationToken ct = default) =>
        QueryInlineAsync<IssueResponse>(
            $"SELECT {IssueCols} FROM dbo.Issues WHERE (@status IS NULL OR Status = @status) " +
            "AND (@reporterUserId IS NULL OR ReporterUserId = @reporterUserId) ORDER BY CreatedAt DESC",
            new { status, reporterUserId }, ct);

    public async Task<IssueResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<IssueResponse>($"SELECT {IssueCols} FROM dbo.Issues WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<IssueNoteResponse>> GetNotesAsync(Guid issueId, CancellationToken ct = default) =>
        QueryInlineAsync<IssueNoteResponse>(
            "SELECT Id, IssueId, AuthorUserId, Note, CreatedAt FROM dbo.IssueNotes WHERE IssueId = @issueId ORDER BY CreatedAt",
            new { issueId }, ct);

    public Task<IssueResponse?> CreateAsync(
        Guid tenantId, Guid reporterUserId, CreateIssueRequest r, Guid? vehicleId, Guid? routeId, string? photoUrl,
        CancellationToken ct = default) =>
        QuerySingleProcAsync<IssueResponse>("dbo.Issue_Create", new
        {
            TenantId = tenantId,
            ReporterUserId = reporterUserId,
            r.Category,
            r.Title,
            r.Description,
            r.Priority,
            VehicleId = vehicleId,
            RouteId = routeId,
            r.TripId,
            PhotoUrl = photoUrl,
        }, ct);

    public Task<IssueResponse?> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default) =>
        QuerySingleProcAsync<IssueResponse>("dbo.Issue_Update", new { Id = id, Status = status }, ct);

    public Task<IssueNoteResponse?> AddNoteAsync(
        Guid tenantId, Guid issueId, Guid authorUserId, string note, CancellationToken ct = default) =>
        QuerySingleProcAsync<IssueNoteResponse>("dbo.IssueNote_Add",
            new { TenantId = tenantId, IssueId = issueId, AuthorUserId = authorUserId, Note = note }, ct);

    /// Tenant's SchoolAdmin/SchoolOwner users — targets for the "new issue" notification.
    /// UserRoles has no TenantId column, so this joins through Users (which does).
    public async Task<IReadOnlyList<Guid>> GetManagerUserIdsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<UserIdRow>(@"
SELECT DISTINCT u.Id
FROM dbo.Users u
INNER JOIN dbo.UserRoles ur ON ur.UserId = u.Id
WHERE u.TenantId = @tenantId AND ur.Role IN (@admin, @owner)",
            new { tenantId, admin = "school.admin", owner = "school.owner" }, ct);
        return rows.Select(r => r.Id).ToList();
    }
}

public static class IssueModule
{
    public static IServiceCollection AddIssuesModule(this IServiceCollection services)
    {
        services.AddScoped<IssueRepository>();
        return services;
    }
}
