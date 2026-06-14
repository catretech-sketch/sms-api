using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed class AuthRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>("dbo.User_GetByEmail", new { Email = email }, ct);
}
