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
public sealed record ChatMessageResponse(
    Guid Id, Guid ThreadId, Guid? SenderId, string Text, DateTime SentAt, bool IsMine, string? ImageUrl)
{
    public DateTime? DeliveredAt { get; init; }
    public DateTime? ReadAt { get; init; }
    public bool IsDelivered => DeliveredAt is not null || ReadAt is not null;
    public bool IsRead => ReadAt is not null;
}
public sealed record SendMessageRequest(string? Text, string? ImageUrl);
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
public sealed record CreateNotificationRequest(string? Icon, string? Tone, string Title, string? Body, Guid? UserId = null);

public sealed class CommsRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record MessageRow(
        Guid Id, Guid ThreadId, Guid? SenderId, string Text, string? ImageUrl, DateTime SentAt,
        DateTime? DeliveredAt, DateTime? ReadAt);
    private sealed record ThreadInfoRow(Guid Id, string Name, string? Role, Guid OwnerUserId, bool IsGroup);
    private sealed record UserIdRow(Guid Id);
    private sealed record UserNameRow(string? Name);
    private sealed record RoleLabelRow(string? RoleLabel);

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

    public Task<IReadOnlyList<NotificationResponse>> ListNotificationsAsync(Guid? userId, CancellationToken ct = default) =>
        QueryInlineAsync<NotificationResponse>(
            $"SELECT {NotificationCols} FROM dbo.Notifications WHERE UserId IS NULL OR UserId = @userId ORDER BY Unread DESC, [Time] DESC",
            new { userId }, ct);

    public Task<int> MarkNotificationsReadAsync(Guid? userId, CancellationToken ct = default) =>
        ExecuteInlineAsync(
            "UPDATE dbo.Notifications SET Unread = 0 WHERE Unread = 1 AND (UserId IS NULL OR UserId = @userId)",
            new { userId }, ct);

    public Task<NotificationResponse?> CreateNotificationAsync(Guid tenantId, CreateNotificationRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<NotificationResponse>("dbo.Notification_Create",
            new { TenantId = tenantId, r.Icon, r.Tone, r.Title, r.Body, r.UserId }, ct);

    // Known limitation: ChatThreads.Name/Role are free-text, not an FK to Users - there's no
    // reliable per-thread "which Users row is this" link today. Presence is matched
    // best-effort by TenantId+Name, which may miss (or mismatch) threads whose contact name
    // doesn't exactly match a Users.Name. A proper ChatThreads.ContactUserId FK is a
    // follow-up, out of scope here.
    public Task<IReadOnlyList<ChatThreadResponse>> ListThreadsAsync(Guid ownerUserId, CancellationToken ct = default) =>
        QueryInlineAsync<ChatThreadResponse>(@"
SELECT th.Id, th.TenantId, th.Name, th.Role, th.LastMessage, th.LastAt, th.Unread, th.IsGroup AS [Group], th.ChildId,
       CAST(CASE WHEN u.LastSeenAt IS NOT NULL AND u.LastSeenAt > DATEADD(MINUTE, -5, SYSUTCDATETIME())
            THEN 1 ELSE 0 END AS bit) AS Online
FROM dbo.ChatThreads th
LEFT JOIN dbo.Users u ON u.TenantId = th.TenantId AND u.Name = th.Name
WHERE th.OwnerUserId = @ownerUserId
ORDER BY th.LastAt DESC", new { ownerUserId }, ct);

    public Task<ChatThreadResponse?> CreateThreadAsync(
        Guid tenantId, Guid ownerUserId, CreateThreadRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<ChatThreadResponse>("dbo.Thread_Create",
            new { TenantId = tenantId, OwnerUserId = ownerUserId, r.Name, r.Role, IsGroup = r.Group, r.ChildId }, ct);

    public async Task<bool> UserOwnsThreadAsync(Guid threadId, Guid ownerUserId, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<int>(
            "SELECT COUNT(1) FROM dbo.ChatThreads WHERE Id = @threadId AND OwnerUserId = @ownerUserId",
            new { threadId, ownerUserId }, ct);
        return rows.FirstOrDefault() > 0;
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> ListMessagesAsync(
        Guid threadId, Guid ownerUserId, Guid? callerId, CancellationToken ct = default)
    {
        if (!await UserOwnsThreadAsync(threadId, ownerUserId, ct))
            return Array.Empty<ChatMessageResponse>();

        await MarkThreadReadAsync(threadId, ownerUserId, ct);

        var rows = await QueryInlineAsync<MessageRow>(
            "SELECT Id, ThreadId, SenderId, [Text], ImageUrl, SentAt, DeliveredAt, ReadAt FROM dbo.ChatMessages WHERE ThreadId = @threadId ORDER BY SentAt",
            new { threadId }, ct);
        return rows.Select(m => ToResponse(m, callerId)).ToList();
    }

    public async Task<ChatMessageResponse?> AddMessageAsync(
        Guid tenantId, Guid threadId, Guid ownerUserId, Guid? senderId, string text, string? imageUrl,
        CancellationToken ct = default)
    {
        if (!await UserOwnsThreadAsync(threadId, ownerUserId, ct))
            return null;

        var correlationId = Guid.NewGuid();
        var m = await QuerySingleProcAsync<MessageRow>("dbo.Message_Add",
            new
            {
                TenantId = tenantId,
                ThreadId = threadId,
                SenderId = senderId,
                Text = text,
                ImageUrl = imageUrl,
                CorrelationId = correlationId,
            }, ct);
        if (m is null)
            return null;

        var delivered = await DeliverToPeerInboxAsync(tenantId, threadId, senderId, text, imageUrl, correlationId, ct);
        DateTime? deliveredAt = m.DeliveredAt;
        if (delivered)
        {
            await ExecuteInlineAsync(
                "UPDATE dbo.ChatMessages SET DeliveredAt = SYSUTCDATETIME() WHERE Id = @id AND DeliveredAt IS NULL",
                new { id = m.Id }, ct);
            deliveredAt = DateTime.UtcNow;
        }

        return ToResponse(m, senderId, deliveredAt);
    }

    /// <summary>
    /// Opening a thread is the read receipt: clear the inbox badge and stamp ReadAt
    /// on the sender's copy (matched by CorrelationId) so they get double blue ticks.
    /// </summary>
    private Task MarkThreadReadAsync(Guid threadId, Guid ownerUserId, CancellationToken ct) =>
        ExecuteInlineAsync(@"
UPDATE dbo.ChatThreads
SET Unread = 0
WHERE Id = @threadId AND OwnerUserId = @ownerUserId AND Unread <> 0;

UPDATE m
SET m.ReadAt = SYSUTCDATETIME()
FROM dbo.ChatMessages m
INNER JOIN dbo.ChatMessages incoming
    ON incoming.CorrelationId = m.CorrelationId
   AND incoming.ThreadId = @threadId
   AND incoming.SenderId IS NOT NULL
   AND incoming.SenderId <> @ownerUserId
WHERE m.ReadAt IS NULL;",
            new { threadId, ownerUserId }, ct);

    private static ChatMessageResponse ToResponse(MessageRow m, Guid? callerId, DateTime? deliveredAt = null) =>
        new(m.Id, m.ThreadId, m.SenderId, m.Text, m.SentAt, m.SenderId == callerId, m.ImageUrl)
        {
            DeliveredAt = deliveredAt ?? m.DeliveredAt,
            ReadAt = m.ReadAt,
        };

    /// <summary>
    /// Mirror an outbound message into the contact's private inbox thread so chat is two-way.
    /// Best-effort match on contact display name (Users / Teachers / Staff) until ContactUserId exists.
    /// </summary>
    private async Task<bool> DeliverToPeerInboxAsync(
        Guid tenantId, Guid sourceThreadId, Guid? senderId, string text, string? imageUrl,
        Guid correlationId, CancellationToken ct)
    {
        if (senderId is not { } sid)
            return false;

        var threadRows = await QueryInlineAsync<ThreadInfoRow>(
            "SELECT Id, Name, Role, OwnerUserId, IsGroup FROM dbo.ChatThreads WHERE Id = @sourceThreadId",
            new { sourceThreadId }, ct);
        var thread = threadRows.FirstOrDefault();
        if (thread is null || thread.IsGroup)
            return false;

        var recipientId = await ResolveContactUserIdAsync(tenantId, thread.Name, sid, ct);
        if (recipientId is null || recipientId == sid)
            return false;

        var senderRows = await QueryInlineAsync<UserNameRow>(
            "SELECT Name FROM dbo.Users WHERE Id = @sid", new { sid }, ct);
        var senderName = senderRows.FirstOrDefault()?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
            return false;

        var senderRole = await ResolveSenderRoleLabelAsync(tenantId, sid, ct) ?? "Staff";

        var peerThread = await QuerySingleProcAsync<ChatThreadResponse>("dbo.Thread_Create",
            new
            {
                TenantId = tenantId,
                OwnerUserId = recipientId,
                Name = senderName,
                Role = senderRole,
                IsGroup = false,
                ChildId = (Guid?)null,
            }, ct);
        if (peerThread is null)
            return false;

        var peer = await QuerySingleProcAsync<MessageRow>("dbo.Message_Add",
            new
            {
                TenantId = tenantId,
                ThreadId = peerThread.Id,
                SenderId = sid,
                Text = text,
                ImageUrl = imageUrl,
                CorrelationId = correlationId,
            }, ct);
        if (peer is null)
            return false;

        await ExecuteInlineAsync(
            "UPDATE dbo.ChatThreads SET Unread = Unread + 1 WHERE Id = @threadId",
            new { threadId = peerThread.Id }, ct);

        var preview = string.IsNullOrWhiteSpace(text)
            ? (string.IsNullOrWhiteSpace(imageUrl) ? "New message" : "[Image]")
            : text.Trim();
        if (preview.Length > 80) preview = preview[..77] + "...";
        await CreateNotificationAsync(tenantId, new CreateNotificationRequest(
            Icon: "chatbubbles",
            Tone: "chat",
            Title: senderName,
            Body: preview,
            UserId: recipientId), ct);
        return true;
    }

    private async Task<Guid?> ResolveContactUserIdAsync(
        Guid tenantId, string contactName, Guid senderId, CancellationToken ct)
    {
        var rows = await QueryInlineAsync<UserIdRow>(@"
SELECT TOP 1 x.Id
FROM (
    SELECT u.Id, 1 AS Pri
    FROM dbo.Users u
    WHERE u.TenantId = @tenantId AND u.Name = @contactName AND u.Id <> @senderId
    UNION ALL
    SELECT t.UserId, 2 AS Pri
    FROM dbo.Teachers t
    WHERE t.TenantId = @tenantId AND t.Name = @contactName AND t.UserId IS NOT NULL AND t.UserId <> @senderId
    UNION ALL
    SELECT s.UserId, 3 AS Pri
    FROM dbo.Staff s
    WHERE s.TenantId = @tenantId AND s.Name = @contactName AND s.UserId IS NOT NULL AND s.UserId <> @senderId
) x
WHERE x.Id IS NOT NULL
ORDER BY x.Pri", new { tenantId, contactName, senderId }, ct);
        return rows.FirstOrDefault()?.Id;
    }

    private async Task<string?> ResolveSenderRoleLabelAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var rows = await QueryInlineAsync<RoleLabelRow>(@"
SELECT TOP 1 COALESCE(
    NULLIF(LTRIM(RTRIM(t.Designation)), ''),
    NULLIF(LTRIM(RTRIM(s.Role)), ''),
    N'Teacher') AS RoleLabel
FROM dbo.Users u
LEFT JOIN dbo.Teachers t ON t.UserId = u.Id AND t.TenantId = @tenantId
LEFT JOIN dbo.Staff s ON s.UserId = u.Id AND s.TenantId = @tenantId
WHERE u.Id = @userId", new { tenantId, userId }, ct);
        return rows.FirstOrDefault()?.RoleLabel;
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
        services.AddScoped<UserAppSettingsRepository>();
        return services;
    }
}
