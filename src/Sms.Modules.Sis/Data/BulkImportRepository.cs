using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Sms.Modules.Sis.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Sis.Data;

/// Thrown when RecordResultAsync loses a unique-index race on (TenantId, ImportId, BatchIndex)
/// to a concurrent request for the same batch, but the row that won the race can't be read back
/// (e.g. it was deleted between the conflict and the re-read) — should be effectively
/// unreachable in practice, but guards against silently returning a null result.
public sealed class BulkImportBatchConflictException() : Exception(
    "This batch's result could not be recorded or re-read after a concurrent insert race");

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
    /// same unique-index-race-then-re-read pattern used by TenancyService.cs:114 and
    /// MeSchoolsService.cs:124 (SQL error 2601/2627).
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
