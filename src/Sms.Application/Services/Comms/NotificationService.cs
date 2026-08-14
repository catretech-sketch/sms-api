using Sms.Application.Common;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Comms;

public interface INotificationService
{
    Task<ApiResult<IReadOnlyList<NotificationResponse>>> ListAsync(CancellationToken ct = default);
    Task<ApiResult<NotificationResponse>> CreateAsync(CreateNotificationRequest req, CancellationToken ct = default);
    Task<ApiResult> MarkReadAsync(CancellationToken ct = default);
}

public sealed class NotificationService(CommsRepository repo, ITenantContext tenant) : INotificationService
{
    public async Task<ApiResult<IReadOnlyList<NotificationResponse>>> ListAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<NotificationResponse>>.Ok(await repo.ListNotificationsAsync(tenant.UserId, ct));

    public async Task<ApiResult<NotificationResponse>> CreateAsync(CreateNotificationRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<NotificationResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<NotificationResponse>.Ok((await repo.CreateNotificationAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult> MarkReadAsync(CancellationToken ct = default)
    {
        await repo.MarkNotificationsReadAsync(tenant.UserId, ct);
        return ApiResult.NoContent();
    }
}
