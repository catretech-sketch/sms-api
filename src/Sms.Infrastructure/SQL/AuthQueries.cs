namespace Sms.Infrastructure.SQL;

/// Stored procedure names for auth data access.
public static class AuthQueries
{
    public const string GetByEmail = "dbo.User_GetByEmail";
    public const string GetByPhone = "dbo.User_GetByPhone";
    public const string GetById = "dbo.User_GetById";
    public const string GetRoles = "dbo.UserRoles_GetByUser";
    public const string SetPassword = "dbo.User_SetPassword";
    public const string OtpInsert = "dbo.Otp_Insert";
    public const string OtpGetActive = "dbo.Otp_GetActive";
    public const string OtpConsume = "dbo.Otp_Consume";
    public const string OtpConsumeAll = "dbo.Otp_ConsumeAllForIdentifier";
}
