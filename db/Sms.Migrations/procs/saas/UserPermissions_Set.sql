CREATE OR ALTER PROCEDURE dbo.UserPermissions_Set
    @UserId uniqueidentifier,
    @Json nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.UserPermissions WHERE UserId = @UserId;

    IF @Json IS NULL OR LTRIM(RTRIM(@Json)) IN (N'', N'[]')
        RETURN;

    INSERT INTO dbo.UserPermissions (UserId, Module, Cap, Effect)
    SELECT
        @UserId,
        j.module,
        j.cap,
        j.effect
    FROM OPENJSON(@Json)
    WITH (
        module nvarchar(64) '$.module',
        cap    char(1)      '$.cap',
        effect nvarchar(16) '$.effect'
    ) j
    WHERE j.module IS NOT NULL
      AND j.cap IN ('V', 'E', 'A')
      AND j.effect IN ('grant', 'revoke');
END
