using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Sms.Tests.Integration.Finance;

[Collection("sql")]
public class AuditLogsTests(SqlServerFixture fx)
{
    [Fact]
    public async Task AuditLogs_table_exists_with_expected_columns()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var cols = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AuditLogs'")).ToList();
        cols.Should().Contain([
            "Id", "TenantId", "ActorUserId", "Action", "Module", "EntityType", "EntityId",
            "TimestampUtc", "BeforeData", "AfterData",
        ]);
    }
}
