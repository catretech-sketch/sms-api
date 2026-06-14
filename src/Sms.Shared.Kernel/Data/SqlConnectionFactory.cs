using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Shared.Kernel.Data;

public sealed class SqlConnectionFactory(string connectionString, ITenantContext tenant) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await StampSessionContextAsync(conn);
        return conn;
    }

    private async Task StampSessionContextAsync(SqlConnection conn)
    {
        if (tenant.TenantId is { } tid)
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@v",
                new { v = tid });
        if (tenant.UserId is { } uid)
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'UserId', @value=@v",
                new { v = uid });
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'IsPlatform', @value=@v",
            new { v = tenant.IsPlatform ? 1 : 0 });
    }
}
