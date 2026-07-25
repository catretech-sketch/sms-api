using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Data;

namespace Sms.Infrastructure.DAO;

public sealed class ProfileDao(IDbConnectionFactory factory) : BaseRepository(factory), IProfileDao
{
    public async Task<string?> GetTeacherTitleByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await QueryInlineAsync<string>(
            "SELECT Designation FROM dbo.Teachers WHERE UserId = @userId", new { userId }, ct))
        .FirstOrDefault();

    public async Task<string?> GetStaffTitleByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await QueryInlineAsync<string>(
            "SELECT Role FROM dbo.Staff WHERE UserId = @userId", new { userId }, ct))
        .FirstOrDefault();

    public async Task<string?> GetClassroomNameByTeacherUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (await QueryInlineAsync<string>(
            @"SELECT c.Name FROM dbo.Classes c
              JOIN dbo.Teachers t ON t.Id = c.ClassTeacherId
              WHERE t.UserId = @userId", new { userId }, ct))
        .FirstOrDefault();
}
