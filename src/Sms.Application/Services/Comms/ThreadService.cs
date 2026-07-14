using Sms.Application.Common;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Comms;

public interface IThreadService
{
    Task<ApiResult<IReadOnlyList<ChatThreadResponse>>> ListAsync(CancellationToken ct = default);
    Task<ApiResult<ChatThreadResponse>> CreateAsync(CreateThreadRequest req, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<ChatMessageResponse>>> ListMessagesAsync(Guid threadId, CancellationToken ct = default);
    Task<ApiResult<ChatMessageResponse>> SendMessageAsync(Guid threadId, SendMessageRequest req, CancellationToken ct = default);
}

public sealed class ThreadService(CommsRepository repo, ITenantContext tenant) : IThreadService
{
    public async Task<ApiResult<IReadOnlyList<ChatThreadResponse>>> ListAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<ChatThreadResponse>>.Ok(await repo.ListThreadsAsync(ct));

    public async Task<ApiResult<ChatThreadResponse>> CreateAsync(CreateThreadRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ChatThreadResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<ChatThreadResponse>.Ok((await repo.CreateThreadAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<ChatMessageResponse>>> ListMessagesAsync(Guid threadId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<ChatMessageResponse>>.Ok(await repo.ListMessagesAsync(threadId, tenant.UserId, ct));

    public async Task<ApiResult<ChatMessageResponse>> SendMessageAsync(Guid threadId, SendMessageRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ChatMessageResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<ChatMessageResponse>.Ok((await repo.AddMessageAsync(tid, threadId, tenant.UserId, req.Text, ct))!, 201);
    }
}
