using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed class AuthRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>("dbo.User_GetByEmail", new { Email = email }, ct);

    public Task<UserRecord?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>("dbo.User_GetById", new { Id = id }, ct);

    public Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct = default) =>
        QueryProcAsync<string>("dbo.UserRoles_GetByUser", new { UserId = userId }, ct);
}
