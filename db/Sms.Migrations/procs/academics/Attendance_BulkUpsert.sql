CREATE OR ALTER PROCEDURE dbo.Attendance_BulkUpsert
    @TenantId uniqueidentifier, @ClassId uniqueidentifier, @Date date,
    @MarkedBy uniqueidentifier, @Rows dbo.AttendanceTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    MERGE dbo.AttendanceRecords AS tgt
    USING (SELECT StudentId, Status FROM @Rows) AS src
        ON tgt.TenantId = @TenantId AND tgt.ClassId = @ClassId
           AND tgt.StudentId = src.StudentId AND tgt.[Date] = @Date
    WHEN MATCHED THEN
        UPDATE SET Status = src.Status, MarkedBy = @MarkedBy
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, ClassId, StudentId, [Date], Status, MarkedBy)
        VALUES (NEWID(), @TenantId, @ClassId, src.StudentId, @Date, src.Status, @MarkedBy);
END
