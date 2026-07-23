using System.Net;

namespace Sms.Shared.Kernel.Auth;

/// Shared welcome / onboard email for school invites and Catre client create.
/// Includes school name + password-setup OTP (same reset flow as forgot-password).
public static class InviteWelcomeEmail
{
    private const string Brand = "#4f46e5";

    /// <param name="schoolName">Single school name, or a comma-joined list when one
    /// person was invited to several schools in one batch (only one email is sent).</param>
    /// <param name="link">When set (magic-link method), the email shows ONLY the
    /// clickable link — no OTP code text, since the link already carries it silently.
    /// When null, the email shows the code instead.</param>
    /// <param name="customMessage">Optional personal note from the inviter, shown above
    /// the login link/code.</param>
    public static EmailMessage Build(string to, string schoolName, string code, string? roleLabel = null, string? link = null, string? customMessage = null)
    {
        var school = string.IsNullOrWhiteSpace(schoolName) ? "your school" : schoolName.Trim();
        var roleBit = string.IsNullOrWhiteSpace(roleLabel) ? "" : $" as {roleLabel.Trim()}";
        var subject = $"Welcome to {school} on SchoolMate";
        var noteBit = string.IsNullOrWhiteSpace(customMessage) ? "" : $"\n\"{customMessage.Trim()}\"\n";
        var body = string.IsNullOrWhiteSpace(link)
            ? $"Hello,\n\n" +
              $"You've been onboarded to {school} on SchoolMate{roleBit}.\n" +
              noteBit +
              "\nWelcome! To access the school CRM, set your password with this one-time code:\n\n" +
              $"  {code}\n\n" +
              "This code expires in 10 minutes.\n\n" +
              "How to finish setup:\n" +
              "1. Open SchoolMate\n" +
              "2. Choose First time / forgot password\n" +
              "3. Enter your email and this code\n" +
              "4. Create your password\n" +
              "5. Sign in\n\n" +
              $"If you didn't expect this invite for {school}, you can ignore this email.\n\n" +
              $"— {school} via SchoolMate"
            : $"Hello,\n\n" +
              $"You've been onboarded to {school} on SchoolMate{roleBit}.\n" +
              noteBit +
              "\nWelcome! Click below to set your password and sign in:\n\n" +
              $"  {link}\n\n" +
              "This link expires in 10 minutes.\n\n" +
              $"If you didn't expect this invite for {school}, you can ignore this email.\n\n" +
              $"— {school} via SchoolMate";

        return new EmailMessage(to, subject, body, HtmlBody: BuildHtml(school, code, roleLabel, link, customMessage));
    }

    public static string SmsBody(string schoolName, string code, string? roleLabel = null, string? link = null, string? customMessage = null)
    {
        var school = string.IsNullOrWhiteSpace(schoolName) ? "your school" : schoolName.Trim();
        var roleBit = string.IsNullOrWhiteSpace(roleLabel) ? "" : $" ({roleLabel.Trim()})";
        var noteBit = string.IsNullOrWhiteSpace(customMessage) ? "" : $" \"{customMessage.Trim()}\"";
        return string.IsNullOrWhiteSpace(link)
            ? $"Welcome to {school}{roleBit} on SchoolMate.{noteBit} Your password setup code is {code}. Expires in 10 minutes."
            : $"Welcome to {school}{roleBit} on SchoolMate.{noteBit} Set your password: {link}. Expires in 10 minutes.";
    }

    /// Minimal, inline-styled HTML — inline CSS is required since most mail clients
    /// strip <style> blocks. School/role are user-controlled tenant data, so they're
    /// HTML-encoded before interpolation.
    private static string BuildHtml(string school, string code, string? roleLabel, string? link, string? customMessage = null)
    {
        var schoolSafe = WebEncode(school);
        var roleBadge = string.IsNullOrWhiteSpace(roleLabel)
            ? ""
            : $"""<span style="display:inline-block;padding:3px 10px;border-radius:999px;background:#eef2ff;color:{Brand};font-size:12px;font-weight:600;">{WebEncode(roleLabel!.Trim())}</span>""";
        var noteHtml = string.IsNullOrWhiteSpace(customMessage)
            ? ""
            : $"""<p style="margin:0 0 16px;padding:12px 14px;background:#f8fafc;border-left:3px solid {Brand};border-radius:6px;font-size:13px;color:#334155;font-style:italic;">"{WebEncode(customMessage!.Trim())}"</p>""";

        var actionHtml = string.IsNullOrWhiteSpace(link)
            ? $"""
              <p style="margin:0 0 8px;font-size:14px;color:#475569;">Set your password with this one-time code:</p>
              <div style="font-family:'SFMono-Regular',Consolas,monospace;font-size:28px;font-weight:700;letter-spacing:6px;color:#111827;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:16px 20px;text-align:center;margin:0 0 8px;">{WebEncode(code)}</div>
              <p style="margin:0;font-size:12px;color:#94a3b8;">This code expires in 10 minutes.</p>
              """
            : $"""
              <div style="text-align:center;margin:8px 0 12px;">
                <a href="{link}" style="display:inline-block;background:{Brand};color:#ffffff;text-decoration:none;font-weight:600;font-size:15px;padding:13px 28px;border-radius:8px;">Set your password</a>
              </div>
              <p style="margin:0;font-size:12px;color:#94a3b8;text-align:center;">This link expires in 10 minutes. If the button doesn't work, copy this URL:<br/><span style="word-break:break-all;color:#64748b;">{link}</span></p>
              """;

        return $"""
            <div style="background:#f1f5f9;padding:32px 16px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,sans-serif;">
              <div style="max-width:440px;margin:0 auto;background:#ffffff;border-radius:14px;overflow:hidden;border:1px solid #e2e8f0;">
                <div style="background:{Brand};padding:22px 28px;">
                  <span style="color:#ffffff;font-size:17px;font-weight:700;letter-spacing:.2px;">SchoolMate</span>
                </div>
                <div style="padding:28px;">
                  <h1 style="margin:0 0 6px;font-size:19px;color:#0f172a;">Welcome to {schoolSafe}</h1>
                  <p style="margin:0 0 16px;font-size:14px;color:#475569;">
                    You've been onboarded to <strong>{schoolSafe}</strong> on SchoolMate{(roleBadge == "" ? "." : " —")} {roleBadge}
                  </p>
                  {noteHtml}
                  <div style="margin:20px 0;">
                    {actionHtml}
                  </div>
                  <p style="margin:20px 0 0;font-size:12px;color:#94a3b8;">
                    Didn't expect this invite for {schoolSafe}? You can safely ignore this email.
                  </p>
                </div>
              </div>
              <p style="text-align:center;font-size:11px;color:#94a3b8;margin-top:16px;">— {schoolSafe} via SchoolMate</p>
            </div>
            """;
    }

    private static string WebEncode(string s) => WebUtility.HtmlEncode(s);
}
