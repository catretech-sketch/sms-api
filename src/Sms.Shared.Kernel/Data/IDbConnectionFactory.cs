using System.Data.Common;

namespace Sms.Shared.Kernel.Data;

public interface IDbConnectionFactory
{
    /// Opens a connection and, when a tenant/user is in context, stamps SESSION_CONTEXT
    /// ('TenantId','UserId','IsPlatform') so RLS predicates and procs see the caller.
    Task<DbConnection> OpenAsync(CancellationToken ct = default);
}
