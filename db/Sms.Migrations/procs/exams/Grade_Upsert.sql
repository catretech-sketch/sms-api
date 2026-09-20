CREATE OR ALTER PROCEDURE dbo.Grade_Upsert
    @TenantId uniqueidentifier, @StudentId uniqueidentifier, @StudentName nvarchar(200),
    @ExamPaperId uniqueidentifier, @Marks decimal(6,2)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Max decimal(6,2);
    SELECT @Max = MaxMarks FROM dbo.ExamPapers WHERE Id = @ExamPaperId;
    SET @Max = ISNULL(NULLIF(@Max, 0), 100);

    DECLARE @Pct decimal(6,2) = @Marks * 100.0 / @Max;
    DECLARE @Grade nvarchar(4) =
        CASE WHEN @Pct >= 91 THEN 'A1' WHEN @Pct >= 81 THEN 'A2' WHEN @Pct >= 71 THEN 'B1'
             WHEN @Pct >= 61 THEN 'B2' WHEN @Pct >= 51 THEN 'C1' WHEN @Pct >= 41 THEN 'C2'
             WHEN @Pct >= 33 THEN 'D' ELSE 'E' END;
    DECLARE @Gpa decimal(4,2) =
        CASE WHEN @Pct >= 91 THEN 10 WHEN @Pct >= 81 THEN 9 WHEN @Pct >= 71 THEN 8
             WHEN @Pct >= 61 THEN 7 WHEN @Pct >= 51 THEN 6 WHEN @Pct >= 41 THEN 5
             WHEN @Pct >= 33 THEN 4 ELSE 3 END;
    DECLARE @Pass bit = CASE WHEN @Pct >= 33 THEN 1 ELSE 0 END;
    DECLARE @Today date = CAST(SYSUTCDATETIME() AS date);

    MERGE dbo.Grades AS tgt
    USING (SELECT @ExamPaperId AS ExamPaperId, @StudentId AS StudentId) AS src
        ON tgt.TenantId = @TenantId AND tgt.ExamPaperId = src.ExamPaperId AND tgt.StudentId = src.StudentId
    WHEN MATCHED THEN
        UPDATE SET Marks = @Marks, MaxMarks = @Max, Grade = @Grade, Gpa = @Gpa, Pass = @Pass,
                   StudentName = @StudentName, [Date] = @Today
    WHEN NOT MATCHED THEN
        INSERT (Id, TenantId, StudentId, StudentName, ExamPaperId, Marks, MaxMarks, Grade, Gpa, Pass, [Date])
        VALUES (NEWID(), @TenantId, @StudentId, @StudentName, @ExamPaperId, @Marks, @Max, @Grade, @Gpa, @Pass, @Today);

    SELECT Id, TenantId, StudentId, StudentName, ExamPaperId, Marks, MaxMarks, Grade, Gpa, Pass, [Date]
    FROM dbo.Grades WHERE TenantId = @TenantId AND ExamPaperId = @ExamPaperId AND StudentId = @StudentId;
END
