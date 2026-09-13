using FluentMigrator;

namespace Sms.Migrations;

/// BusAssignment_Assign's MERGE was keyed on (TenantId, TeacherUserId) — the inverse of what
/// "Bus Duty is one teacher per bus" requires. Reassigning a bus from teacher A to teacher B
/// moved A's row (since A now matched no key) but never touched B's, so B's insert created a
/// SECOND row for the same bus — ListBusesAsync's LEFT JOIN on BusAssignments then duplicated
/// that bus in every list response. Fix: clear any other teacher's row for this BusId first,
/// so a bus always has at most one duty-teacher row, matching StudentBus_Assign's (correct)
/// pattern of keying the MERGE on the thing being assigned TO.
[Migration(182, "BusAssignment_Assign: clear other teachers' rows for this bus before upserting, so a bus never ends up with two duty-teacher rows")]
public sealed class M0182_BusAssignment_Assign_Replace_Per_Bus : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
CREATE OR ALTER PROCEDURE dbo.BusAssignment_Assign
    @TenantId uniqueidentifier, @BusId uniqueidentifier, @TeacherUserId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.BusAssignments
    WHERE TenantId = @TenantId AND BusId = @BusId AND TeacherUserId <> @TeacherUserId;

    MERGE dbo.BusAssignments AS tgt
    USING (SELECT @TenantId AS TenantId, @TeacherUserId AS TeacherUserId) AS src
        ON tgt.TenantId = src.TenantId AND tgt.TeacherUserId = src.TeacherUserId
    WHEN MATCHED THEN UPDATE SET BusId = @BusId
    WHEN NOT MATCHED THEN
        INSERT (TenantId, TeacherUserId, BusId) VALUES (@TenantId, @TeacherUserId, @BusId);
END");
    }

    public override void Down()
    {
        // No-op: the previous proc body (which could leave duplicate per-bus rows) is superseded, not restored.
    }
}
