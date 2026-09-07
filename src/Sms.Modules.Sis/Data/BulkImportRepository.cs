using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Sms.Modules.Sis.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Sis.Data;

/// Thrown when a batch's (TenantId, ImportId, BatchIndex) already has a *different* stored
/// result than what's about to be inserted — should never happen in practice (the same
/// batchIndex is always sent with the same rows), but guards against a client bug the same
/// way IdempotencyKeyConflictException guards fee payments.
public sealed class BulkImportBatchConflictException() : Exception("This batch was already recorded with a different result");

public sealed class BulkImportRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<BulkImportBatchResponse?> GetExistingResultAsync(
        Guid tenantId, Guid importId, int batchIndex, CancellationToken ct = default)
    {
        var row = (await QueryInlineAsync<string>(
            "SELECT ResultJson FROM dbo.BulkImportBatches WHERE TenantId = @tenantId AND ImportId = @importId AND BatchIndex = @batchIndex",
            new { tenantId, importId, batchIndex }, ct)).FirstOrDefault();
        return row is null ? null : JsonSerializer.Deserialize<BulkImportBatchResponse>(row);
    }

    /// Inserts the batch's result. If a concurrent request for the same (tenantId, importId,
    /// batchIndex) won the race, returns that row's already-stored result instead of throwing —
    /// mirrors FinanceModule.cs's fee-payment idempotency race handling (SQL error 2601/2627).
    public async Task<BulkImportBatchResponse> RecordResultAsync(
        Guid tenantId, Guid importId, int batchIndex, BulkImportBatchResponse result, CancellationToken ct = default)
    {
        await using var conn = await Factory.OpenAsync(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.BulkImportBatches (Id, TenantId, ImportId, BatchIndex, ResultJson, CreatedAt)
                VALUES (@id, @tenantId, @importId, @batchIndex, @resultJson, SYSUTCDATETIME())
                """,
                new { id = Guid.NewGuid(), tenantId, importId, batchIndex, resultJson = JsonSerializer.Serialize(result) },
                commandType: CommandType.Text, cancellationToken: ct));
            return result;
        }
        catch (SqlException sqlEx) when (sqlEx.Number is 2601 or 2627)
        {
            var raced = await GetExistingResultAsync(tenantId, importId, batchIndex, ct);
            if (raced is null) throw new BulkImportBatchConflictException();
            return raced;
        }
    }
}
