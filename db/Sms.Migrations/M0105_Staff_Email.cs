using FluentMigrator;

namespace Sms.Migrations;

[Migration(105, "Staff: add Email column (was missing entirely — staff email edits only lived in browser localStorage) + re-declare Staff_Create/Staff_Update to carry it")]
public sealed class M0105_Staff_Email : Migration
{
    public override void Up()
    {
        Alter.Table("Staff").AddColumn("Email").AsString(256).Nullable();

        // Kept under procs/staffingemail (not procs/staffing, and not procs/staffingidentity
        // either) — same convention as M0095/M0086/M0087/M0089/M0091/M0093, one folder further:
        // the broad "procs.staffing." EmbeddedProcs fragment used by earlier migrations
        // (M0054, M0063-M0065) would otherwise pick up these bodies and re-create them
        // referencing Email ~40 migrations before this one adds the column. procs/staffingidentity
        // is already claimed by M0095's own (EmployeeCode-only) Staff_Update/Teacher_Update
        // bodies, so reusing that folder for the Email-carrying versions would make M0095
        // pick up the Email reference too — hence a third, dedicated folder for this migration.
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingemail.Staff_Create"))
            Execute.Sql(sql);
        foreach (var sql in M0003_Procs_Auth.EmbeddedProcs("procs.staffingemail.Staff_Update"))
            Execute.Sql(sql);
    }

    public override void Down()
    {
        Delete.Column("Email").FromTable("Staff");
    }
}
