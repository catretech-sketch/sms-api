using System.Data;
using Dapper;

namespace Sms.Shared.Kernel.Data;

/// Base for all repositories. Stored procedures for writes/complex reads;
/// QueryInlineAsync for simple single-table reads (parameterised only — never string-concat).
public abstract class BaseRepository(IDbConnectionFactory factory)
{
    protected IDbConnectionFactory Factory { get; } = factory;

    protected async Task<IReadOnlyList<T>> QueryProcAsync<T>(
        string proc, object? args = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<T>(
            new CommandDefinition(proc, args, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.AsList();
    }

    protected async Task<T?> QuerySingleProcAsync<T>(
        string proc, object? args = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(proc, args, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    protected async Task<int> ExecuteProcAsync(
        string proc, object? args = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        return await conn.ExecuteAsync(
            new CommandDefinition(proc, args, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    protected async Task<IReadOnlyList<T>> QueryInlineAsync<T>(
        string sql, object? args = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<T>(
            new CommandDefinition(sql, args, commandType: CommandType.Text, cancellationToken: ct));
        return rows.AsList();
    }
}
