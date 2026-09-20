CREATE OR ALTER PROCEDURE dbo.PlatformAdmin_Exists
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM dbo.Users WHERE IsPlatform = 1 AND Status = 'active'
    ) THEN 1 ELSE 0 END;
END
