CREATE OR ALTER PROCEDURE dbo.Onboarding_SetChecklist
    @Id uniqueidentifier, @Label nvarchar(100), @Done bit
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OnboardingChecklist SET Done = @Done WHERE OnboardingId = @Id AND Label = @Label;
END
