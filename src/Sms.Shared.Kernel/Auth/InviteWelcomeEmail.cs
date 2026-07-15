namespace Sms.Shared.Kernel.Auth;

/// Shared welcome / onboard email for school invites and Catre client create.
/// Includes school name + password-setup OTP (same reset flow as forgot-password).
public static class InviteWelcomeEmail
{
    public static EmailMessage Build(string to, string schoolName, string code, string? roleLabel = null)
    {
        var school = string.IsNullOrWhiteSpace(schoolName) ? "your school" : schoolName.Trim();
        var roleBit = string.IsNullOrWhiteSpace(roleLabel) ? "" : $" as {roleLabel.Trim()}";
        var subject = $"Welcome to {school} on SchoolMate";
        var body =
            $"Hello,\n\n" +
            $"You've been onboarded to {school} on SchoolMate{roleBit}.\n\n" +
            "Welcome! To access the school CRM, set your password with this one-time code:\n\n" +
            $"  {code}\n\n" +
            "This code expires in 10 minutes.\n\n" +
            "How to finish setup:\n" +
            "1. Open SchoolMate\n" +
            "2. Choose First time / forgot password\n" +
            "3. Enter your email and this code\n" +
            "4. Create your password\n" +
            "5. Sign in\n\n" +
            $"If you didn't expect this invite for {school}, you can ignore this email.\n\n" +
            $"— {school} via SchoolMate";
        return new EmailMessage(to, subject, body);
    }

    public static string SmsBody(string schoolName, string code, string? roleLabel = null)
    {
        var school = string.IsNullOrWhiteSpace(schoolName) ? "your school" : schoolName.Trim();
        var roleBit = string.IsNullOrWhiteSpace(roleLabel) ? "" : $" ({roleLabel.Trim()})";
        return $"Welcome to {school}{roleBit} on SchoolMate. Your password setup code is {code}. Expires in 10 minutes.";
    }
}
