using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Comms;

// Online is a trailing init-only property with a secondary 10-param constructor, not a
// primary-constructor parameter: Dapper materializes via a constructor matching the exact
// column count of whatever query ran, and Thread_Create still returns the original 9
// columns while ListThreadsAsync now returns 10 (+ Online).
public sealed record ChatThreadResponse(
    Guid Id, Guid TenantId, string Name, string? Role, string? LastMessage, DateTime? LastAt,
    int Unread, bool Group, Guid? ChildId)
{
    public bool Online { get; init; }

    public ChatThreadResponse(
        Guid Id, Guid TenantId, string Name, string? Role, string? LastMessage, DateTime? LastAt,
        int Unread, bool Group, Guid? ChildId, bool Online)
        : this(Id, TenantId, Name, Role, LastMessage, LastAt, Unread, Group, ChildId) =>
        this.Online = Online;
}
public sealed record CreateThreadRequest(string Name, string? Role, bool Group, Guid? ChildId);
public sealed record ChatMessageResponse(Guid Id, Guid ThreadId, Guid? SenderId, string Text, DateTime SentAt, bool IsMine);
public sealed record SendMessageRequest(string Text);
public sealed record AnnouncementResponse(
    Guid Id, Guid TenantId, string Title, string? Body, DateTime Date, string? From, string? Role,
    string Type, bool Pinned, string? Audience)
{
    /// Set after create — how many emails were queued (not a DB column).
    public int Reach { get; init; }
}
public sealed record CreateAnnouncementRequest(
    string Title,
    string Body,
    string? Type,
    string? Audience,
    IReadOnlyList<string>? Emails = null,
    IReadOnlyList<string>? Phones = null,
    IReadOnlyList<string>? Channels = null,
    string? SchoolName = null,
    string? EventDate = null,
    string? EventKind = null,
    /// Optional user-uploaded file (base64, no data: prefix) attached to email.
    string? AttachmentBase64 = null,
    string? AttachmentFileName = null,
    string? AttachmentContentType = null);
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

    // Known limitation: ChatThreads.Name/Role are free-text, not an FK to Users - there's no
    // reliable per-thread "which Users row is this" link today. Presence is matched
    // best-effort by TenantId+Name, which may miss (or mismatch) threads whose contact name
    // doesn't exactly match a Users.Name. A proper ChatThreads.ContactUserId FK is a
    // follow-up, out of scope here.
    public Task<IReadOnlyList<ChatThreadResponse>> ListThreadsAsync(CancellationToken ct = default) =>
        QueryInlineAsync<ChatThreadResponse>(@"
SELECT th.Id, th.TenantId, th.Name, th.Role, th.LastMessage, th.LastAt, th.Unread, th.IsGroup AS [Group], th.ChildId,
       CAST(CASE WHEN u.LastSeenAt IS NOT NULL AND u.LastSeenAt > DATEADD(MINUTE, -5, SYSUTCDATETIME())
            THEN 1 ELSE 0 END AS bit) AS Online
FROM dbo.ChatThreads th
LEFT JOIN dbo.Users u ON u.TenantId = th.TenantId AND u.Name = th.Name
ORDER BY th.LastAt DESC", null, ct);

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
        QueryInlineAsync<AnnouncementResponse>(@"
SELECT a.Id, a.TenantId, a.Title, a.Body, a.[Date], COALESCE(u.Name, a.Role) AS [From], a.Role, a.Type, a.Pinned, a.Audience
FROM dbo.Announcements a
LEFT JOIN dbo.Users u ON u.Id = a.CreatorUserId
WHERE (@audience IS NULL OR a.Audience IS NULL OR a.Audience = @audience)
ORDER BY a.[Date] DESC", new { audience }, ct);

    public Task<AnnouncementResponse?> CreateAnnouncementAsync(
        Guid tenantId, CreateAnnouncementRequest r, Guid? creatorUserId, string? role, CancellationToken ct = default) =>
        QuerySingleProcAsync<AnnouncementResponse>("dbo.Announcement_Create",
            new { TenantId = tenantId, r.Title, r.Body, From = role, Role = role, CreatorUserId = creatorUserId, r.Type, r.Audience }, ct);
}

public static class CommsModule
{
    public static IServiceCollection AddCommsModule(this IServiceCollection services)
    {
        services.AddScoped<CommsRepository>();
        return services;
    }
}
