using FluentMigrator;

namespace Sms.Migrations;

[Migration(37, "Metrics upsert + real dashboard/revenue procs (embedded CREATE OR ALTER)")]
public sealed class M0037_Procs_Platform_Metrics : Migration
{
    public override void Up()
    {
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.platformmetrics."))
            Execute.Sql(sql);
    }

    public override void Down()
        => Execute.Sql("DROP PROCEDURE IF EXISTS dbo.PlatformMetrics_UpsertCurrentMonth; " +
                       "DROP PROCEDURE IF EXISTS dbo.Report_Revenue;");
    // Dashboard_CatreOverview is intentionally NOT dropped here — it predates this migration (M0008).
}
