CREATE OR ALTER PROCEDURE dbo.Onboarding_Advance
    @Id uniqueidentifier, @Stage nvarchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OnboardingItems SET Stage = @Stage WHERE Id = @Id;
END
