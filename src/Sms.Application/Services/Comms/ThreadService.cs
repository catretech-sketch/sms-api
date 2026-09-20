using Sms.Application.Common;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Results;
using Sms.Application.Services.Realtime;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Comms;

public interface IThreadService
{
    Task<ApiResult<IReadOnlyList<ChatThreadResponse>>> ListAsync(CancellationToken ct = default);
    Task<ApiResult<ChatThreadResponse>> CreateAsync(CreateThreadRequest req, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<ChatMessageResponse>>> ListMessagesAsync(Guid threadId, CancellationToken ct = default);
    Task<ApiResult<ChatMessageResponse>> SendMessageAsync(Guid threadId, SendMessageRequest req, CancellationToken ct = default);
}

public sealed class ThreadService(CommsRepository repo, ITenantContext tenant, ILiveBroadcaster live) : IThreadService
{
    public async Task<ApiResult<IReadOnlyList<ChatThreadResponse>>> ListAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<ChatThreadResponse>>.Fail(new Error("unauthorized", "unauthorized"), 401);
        return ApiResult<IReadOnlyList<ChatThreadResponse>>.Ok(await repo.ListThreadsAsync(uid, ct));
    }

    public async Task<ApiResult<ChatThreadResponse>> CreateAsync(CreateThreadRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ChatThreadResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (tenant.UserId is not { } uid)
            return ApiResult<ChatThreadResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);
        return ApiResult<ChatThreadResponse>.Ok((await repo.CreateThreadAsync(tid, uid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<ChatMessageResponse>>> ListMessagesAsync(Guid threadId, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<ChatMessageResponse>>.Fail(new Error("unauthorized", "unauthorized"), 401);
        return ApiResult<IReadOnlyList<ChatMessageResponse>>.Ok(
            await repo.ListMessagesAsync(threadId, uid, tenant.UserId, ct));
    }

    public async Task<ApiResult<ChatMessageResponse>> SendMessageAsync(Guid threadId, SendMessageRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ChatMessageResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (tenant.UserId is not { } uid)
            return ApiResult<ChatMessageResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);

        var text = string.IsNullOrWhiteSpace(req.Text) ? string.Empty : req.Text.Trim();
        var imageUrl = ImageUrlValidation.Normalize(req.ImageUrl);

        if (text.Length == 0 && imageUrl is null)
            return ApiResult<ChatMessageResponse>.Fail(new Error("invalid_request", "message text or image is required"), 422);

        if (imageUrl is not null && ImageUrlValidation.Validate(imageUrl) is { } imageError)
            return ApiResult<ChatMessageResponse>.Fail(imageError, 422);

        var msg = await repo.AddMessageAsync(tid, threadId, uid, uid, text, imageUrl, ct);
        if (msg is null)
            return ApiResult<ChatMessageResponse>.Fail(new Error("not_found", "resource not found"), 404);
        await live.PublishAsync(tid, LiveEventTypes.Chat, new { thread_id = threadId }, ct);
        return ApiResult<ChatMessageResponse>.Ok(msg, 201);
    }
}
