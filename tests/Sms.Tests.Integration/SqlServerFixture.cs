using Dapper;
using Microsoft.Data.SqlClient;
using Sms.Migrations;
using Xunit;

namespace Sms.Tests.Integration;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly string _server =
        Environment.GetEnvironmentVariable("SMS_TEST_SQL_SERVER") ?? "DESKTOP-TJL4SG6";
    private readonly string _dbName = "Sms_Test_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";

    private string MasterCs =>
        $"Server={_server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

    public async Task InitializeAsync()
    {
        await using (var master = new SqlConnection(MasterCs))
        {
            await master.OpenAsync();
            await master.ExecuteAsync($"CREATE DATABASE [{_dbName}];");
        }
        ConnectionString =
            $"Server={_server};Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";
        MigrationRunner.Run(ConnectionString); // tables + RLS + procs
    }

    public async Task DisposeAsync()
    {
        await using var master = new SqlConnection(MasterCs);
        await master.OpenAsync();
        await master.ExecuteAsync(
            $"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}];");
    }
}

[CollectionDefinition("sql")]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>;
