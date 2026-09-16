using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Tasks;

public static class TaskEnums
{
    public static readonly string[] ValidPriorities = ["urgent", "normal"];
    public static readonly string[] ValidStatuses = ["pending", "in_progress", "completed"];

    /// The six canonical duty-role keys the staff app understands (same set StaffRoleMapper
    /// produces from a Staff row's free-text Designation).
    public static readonly string[] ValidRoleKeys = ["driver", "conductor", "sweeper", "gardener", "guard", "peon"];
}

/// Full task row, used for manager create/list-all responses.
public sealed record TaskResponse(
    Guid Id, Guid TenantId, string Title, string? Detail, string? Category,
    Guid? AssignedToUserId, string? AssignedToRoleKey, string Priority, string Status,
    DateTime? DueDate, string? Remarks, string? PhotoUrl,
    Guid CreatedByUserId, Guid? CompletedByUserId, DateTime CreatedAt, DateTime? CompletedAt);

public sealed record CreateTaskRequest(
    string Title, string? Detail, string? Category, string Priority,
    DateTime? DueDate, Guid? AssignedToUserId, string? AssignedToRoleKey);

public sealed class TaskRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string TaskCols =
        "Id, TenantId, Title, Detail, Category, AssignedToUserId, AssignedToRoleKey, Priority, Status, " +
        "DueDate, Remarks, PhotoUrl, CreatedByUserId, CompletedByUserId, CreatedAt, CompletedAt";

    private sealed record RoleRow(string? Role);

    public Task<IReadOnlyList<TaskResponse>> ListForCallerAsync(
        Guid userId, string? roleKey, CancellationToken ct = default) =>
        QueryInlineAsync<TaskResponse>(
            $"SELECT {TaskCols} FROM dbo.Tasks " +
            "WHERE AssignedToUserId = @userId " +
            "   OR (AssignedToRoleKey = @roleKey AND AssignedToUserId IS NULL AND @roleKey IS NOT NULL) " +
            "ORDER BY CreatedAt DESC",
            new { userId, roleKey }, ct);

    public Task<IReadOnlyList<TaskResponse>> ListAllAsync(CancellationToken ct = default) =>
        QueryInlineAsync<TaskResponse>($"SELECT {TaskCols} FROM dbo.Tasks ORDER BY CreatedAt DESC", null, ct);

    public async Task<TaskResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<TaskResponse>($"SELECT {TaskCols} FROM dbo.Tasks WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<TaskResponse?> CreateAsync(
        Guid tenantId, Guid createdByUserId, CreateTaskRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TaskResponse>("dbo.Task_Create", new
        {
            TenantId = tenantId,
            CreatedByUserId = createdByUserId,
            r.Title,
            r.Detail,
            r.Category,
            r.AssignedToUserId,
            r.AssignedToRoleKey,
            r.Priority,
            r.DueDate,
        }, ct);

    public Task<TaskResponse?> CompleteAsync(Guid id, Guid completedByUserId, CancellationToken ct = default) =>
        QuerySingleProcAsync<TaskResponse>("dbo.Task_Complete", new { Id = id, CompletedByUserId = completedByUserId }, ct);

    public Task<TaskResponse?> AttachPhotoAsync(Guid id, string? photoUrl, CancellationToken ct = default) =>
        QuerySingleProcAsync<TaskResponse>("dbo.Task_AttachPhoto", new { Id = id, PhotoUrl = photoUrl }, ct);

    /// The caller's canonical duty role_key, derived the same way /auth/me computes it:
    /// the caller's own linked dbo.Staff row's free-text Role, normalized via StaffRoleMapper.
    /// Null for non-staff users (teachers, admins, parents, students) who have no Staff row.
    public async Task<string?> GetCallerRoleKeyAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<RoleRow>(
            "SELECT TOP 1 Role FROM dbo.Staff WHERE UserId = @userId", new { userId }, ct);
        return StaffRoleMapper.ToRoleKey(rows.FirstOrDefault()?.Role);
    }
}

public static class TaskModule
{
    public static IServiceCollection AddTasksModule(this IServiceCollection services)
    {
        services.AddScoped<TaskRepository>();
        return services;
    }
}
