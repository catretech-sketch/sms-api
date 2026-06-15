using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Modules.Comms;

public sealed record ChatThreadResponse(
    Guid Id, Guid TenantId, string Name, string? Role, string? LastMessage, DateTime? LastAt,
    int Unread, bool Group, Guid? ChildId);
public sealed record CreateThreadRequest(string Name, string? Role, bool Group, Guid? ChildId);
public sealed record ChatMessageResponse(Guid Id, Guid ThreadId, Guid? SenderId, string Text, DateTime SentAt, bool IsMine);
public sealed record SendMessageRequest(string Text);
public sealed record AnnouncementResponse(
    Guid Id, Guid TenantId, string Title, string? Body, DateTime Date, string? From, string? Role,
    string Type, bool Pinned, string? Audience);
public sealed record CreateAnnouncementRequest(string Title, string Body, string? Type, string? Audience);
public sealed record ComplaintResponse(
    Guid Id, Guid TenantId, string Subject, string? From, string? Category, string Priority, string Status,
    string? Age, string? Assignee, string? Body);
public sealed record CreateComplaintRequest(string Subject, string? From, string? Category, string? Priority, string? Body);
public sealed record UpdateComplaintRequest(string? Status, string? Assignee);
public sealed record NotificationResponse(
    Guid Id, Guid TenantId, string? Icon, string? Tone, string Title, string? Body, string? Time, bool Unread);
public sealed record CreateNotificationRequest(string? Icon, string? Tone, string Title, string? Body);

public sealed class CommsRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record MessageRow(Guid Id, Guid ThreadId, Guid? SenderId, string Text, DateTime SentAt);

    private const string ComplaintCols = "Id, TenantId, Subject, [From], Category, Priority, Status, Age, Assignee, Body";
    private const string NotificationCols = "Id, TenantId, Icon, Tone, Title, Body, [Time], Unread";

    public Task<IReadOnlyList<ComplaintResponse>> ListComplaintsAsync(string? status, CancellationToken ct = default) =>
        QueryInlineAsync<ComplaintResponse>(
            $"SELECT {ComplaintCols} FROM dbo.Complaints WHERE (@status IS NULL OR Status = @status) ORDER BY Priority",
            new { status }, ct);

    public Task<ComplaintResponse?> CreateComplaintAsync(Guid tenantId, CreateComplaintRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<ComplaintResponse>("dbo.Complaint_Create",
            new { TenantId = tenantId, r.Subject, r.From, r.Category, r.Priority, r.Body }, ct);

    public async Task<ComplaintResponse?> GetComplaintAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<ComplaintResponse>($"SELECT {ComplaintCols} FROM dbo.Complaints WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<ComplaintResponse?> UpdateComplaintAsync(Guid id, string? status, string? assignee, CancellationToken ct = default) =>
        QuerySingleProcAsync<ComplaintResponse>("dbo.Complaint_Update", new { Id = id, Status = status, Assignee = assignee }, ct);

    public Task<IReadOnlyList<NotificationResponse>> ListNotificationsAsync(CancellationToken ct = default) =>
        QueryInlineAsync<NotificationResponse>($"SELECT {NotificationCols} FROM dbo.Notifications ORDER BY Unread DESC", null, ct);

    public Task<NotificationResponse?> CreateNotificationAsync(Guid tenantId, CreateNotificationRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<NotificationResponse>("dbo.Notification_Create",
            new { TenantId = tenantId, r.Icon, r.Tone, r.Title, r.Body }, ct);

    public Task<IReadOnlyList<ChatThreadResponse>> ListThreadsAsync(CancellationToken ct = default) =>
        QueryInlineAsync<ChatThreadResponse>(
            "SELECT Id, TenantId, Name, Role, LastMessage, LastAt, Unread, IsGroup AS [Group], ChildId " +
            "FROM dbo.ChatThreads ORDER BY LastAt DESC", null, ct);

    public Task<ChatThreadResponse?> CreateThreadAsync(Guid tenantId, CreateThreadRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<ChatThreadResponse>("dbo.Thread_Create",
            new { TenantId = tenantId, r.Name, r.Role, IsGroup = r.Group, r.ChildId }, ct);

    public async Task<IReadOnlyList<ChatMessageResponse>> ListMessagesAsync(Guid threadId, Guid? callerId, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<MessageRow>(
            "SELECT Id, ThreadId, SenderId, [Text], SentAt FROM dbo.ChatMessages WHERE ThreadId = @threadId ORDER BY SentAt",
            new { threadId }, ct);
        return rows.Select(m => new ChatMessageResponse(m.Id, m.ThreadId, m.SenderId, m.Text, m.SentAt, m.SenderId == callerId)).ToList();
    }

    public async Task<ChatMessageResponse?> AddMessageAsync(Guid tenantId, Guid threadId, Guid? senderId, string text, CancellationToken ct = default)
    {
        var m = await QuerySingleProcAsync<MessageRow>("dbo.Message_Add",
            new { TenantId = tenantId, ThreadId = threadId, SenderId = senderId, Text = text }, ct);
        return m is null ? null : new ChatMessageResponse(m.Id, m.ThreadId, m.SenderId, m.Text, m.SentAt, m.SenderId == senderId);
    }

    public Task<IReadOnlyList<AnnouncementResponse>> ListAnnouncementsAsync(string? audience, CancellationToken ct = default) =>
        QueryInlineAsync<AnnouncementResponse>(
            "SELECT Id, TenantId, Title, Body, [Date], [From], Role, Type, Pinned, Audience FROM dbo.Announcements " +
            "WHERE (@audience IS NULL OR Audience IS NULL OR Audience = @audience) ORDER BY [Date] DESC",
            new { audience }, ct);

    public Task<AnnouncementResponse?> CreateAnnouncementAsync(
        Guid tenantId, CreateAnnouncementRequest r, string? from, string? role, CancellationToken ct = default) =>
        QuerySingleProcAsync<AnnouncementResponse>("dbo.Announcement_Create",
            new { TenantId = tenantId, r.Title, r.Body, From = from, Role = role, r.Type, r.Audience }, ct);
}

public static class CommsModule
{
    public static IServiceCollection AddCommsModule(this IServiceCollection services)
    {
        services.AddScoped<CommsRepository>();
        return services;
    }

    private static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    private static IResult NotFound() =>
        Results.Json(ErrorEnvelope.From(new Error("not_found", "resource not found")), statusCode: 404);

    /// Canonical messaging + announcements (§3C): /v1/threads, /v1/announcements. Tenant-scoped.
    public static IEndpointRouteBuilder MapCommsModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        g.MapGet("/threads", async (CommsRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<ChatThreadResponse>>(await repo.ListThreadsAsync())));

        g.MapPost("/threads", async (CreateThreadRequest req, CommsRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<ChatThreadResponse>((await repo.CreateThreadAsync(tid, req))!), statusCode: 201);
        });

        g.MapGet("/threads/{id:guid}/messages", async (Guid id, CommsRepository repo, ITenantContext tenant) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<ChatMessageResponse>>(await repo.ListMessagesAsync(id, tenant.UserId))));

        g.MapPost("/threads/{id:guid}/messages", async (Guid id, SendMessageRequest req, CommsRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<ChatMessageResponse>(
                (await repo.AddMessageAsync(tid, id, tenant.UserId, req.Text))!), statusCode: 201);
        });

        g.MapGet("/announcements", async (CommsRepository repo, string? audience) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<AnnouncementResponse>>(await repo.ListAnnouncementsAsync(audience))));

        g.MapPost("/announcements", async (CreateAnnouncementRequest req, CommsRepository repo, HttpContext http, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            var role = http.User.FindFirst("role")?.Value;
            return Results.Json(new DataEnvelope<AnnouncementResponse>(
                (await repo.CreateAnnouncementAsync(tid, req, role, role))!), statusCode: 201);
        });

        // ---- Complaints ----
        g.MapGet("/complaints", async (CommsRepository repo, string? status) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<ComplaintResponse>>(await repo.ListComplaintsAsync(status))));

        g.MapPost("/complaints", async (CreateComplaintRequest req, CommsRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<ComplaintResponse>((await repo.CreateComplaintAsync(tid, req))!), statusCode: 201);
        });

        g.MapPatch("/complaints/{id:guid}", async (Guid id, UpdateComplaintRequest req, CommsRepository repo) =>
        {
            if (await repo.GetComplaintAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<ComplaintResponse>((await repo.UpdateComplaintAsync(id, req.Status, req.Assignee))!));
        });

        // ---- Notifications ----
        g.MapGet("/notifications", async (CommsRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<NotificationResponse>>(await repo.ListNotificationsAsync())));

        g.MapPost("/notifications", async (CreateNotificationRequest req, CommsRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<NotificationResponse>((await repo.CreateNotificationAsync(tid, req))!), statusCode: 201);
        });

        return app;
    }
}
