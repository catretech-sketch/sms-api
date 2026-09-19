using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class RouteGeometryMigrationTests(SqlServerFixture fx)
{
    [Fact]
    public async Task RouteGeometries_table_exists_with_expected_columns()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var columns = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'RouteGeometries'")).ToHashSet();

        Assert.Contains("RouteId", columns);
        Assert.Contains("TenantId", columns);
        Assert.Contains("StopSequenceHash", columns);
        Assert.Contains("Format", columns);
        Assert.Contains("EncodedPolyline", columns);
        Assert.Contains("DistanceMeters", columns);
        Assert.Contains("DurationSeconds", columns);
        Assert.Contains("Provider", columns);
        Assert.Contains("GeneratedAt", columns);
    }
}
