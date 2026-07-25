namespace Sms.Application.Interfaces.DAO;

public interface IProfileDao
{
    Task<string?> GetTeacherTitleByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<string?> GetStaffTitleByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<string?> GetClassroomNameByTeacherUserIdAsync(Guid userId, CancellationToken ct = default);
}
