using System.Net;
using System.Text;

namespace Sms.Shared.Kernel.Auth;

/// Modern SchoolMate / Catre notice email (calendar, announcements).
public static class AnnouncementNoticeEmail
{
    public sealed record Model(
        string SchoolName,
        string Kind,
        string Title,
        string? DateLabel,
        string? Details);

    public static (string Subject, string Plain, string Html) Build(Model model)
    {
        var school = string.IsNullOrWhiteSpace(model.SchoolName) ? "your school" : model.SchoolName.Trim();
        var kind = string.IsNullOrWhiteSpace(model.Kind) ? "Notice" : model.Kind.Trim();
        var title = string.IsNullOrWhiteSpace(model.Title) ? kind : model.Title.Trim();
        var subject = $"{school} · {kind}: {title}";

        var plain = new StringBuilder();
        plain.AppendLine($"{kind}");
        plain.AppendLine(new string('─', 28));
        plain.AppendLine(title);
        if (!string.IsNullOrWhiteSpace(model.DateLabel))
            plain.AppendLine($"When · {model.DateLabel.Trim()}");
        if (!string.IsNullOrWhiteSpace(model.Details))
        {
            plain.AppendLine();
            plain.AppendLine(model.Details.Trim());
        }
        plain.AppendLine();
        plain.AppendLine($"— {school}");
        plain.AppendLine("Powered by Catre · SchoolMate");
        plain.AppendLine("A calendar notice PDF is attached for your records.");

        var esc = (string? s) => WebUtility.HtmlEncode(s ?? "");
        var detailsHtml = string.IsNullOrWhiteSpace(model.Details)
            ? ""
            : $"<p style=\"margin:16px 0 0;font-size:15px;line-height:1.55;color:#334155\">{esc(model.Details)}</p>";
        var dateHtml = string.IsNullOrWhiteSpace(model.DateLabel)
            ? ""
            : $"<div style=\"display:inline-block;margin-top:12px;padding:8px 14px;border-radius:999px;background:#eff6ff;color:#1d4ed8;font-size:13px;font-weight:600\">📅 {esc(model.DateLabel)}</div>";

        var html =
            "<!DOCTYPE html><html><body style=\"margin:0;padding:0;background:#f1f5f9;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif\">" +
            "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#f1f5f9;padding:28px 12px\">" +
            "<tr><td align=\"center\">" +
            "<table role=\"presentation\" width=\"560\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:560px;width:100%;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 8px 30px rgba(15,23,42,.08)\">" +
            "<tr><td style=\"padding:22px 28px;background:linear-gradient(135deg,#0f766e,#0ea5e9)\">" +
            $"<div style=\"color:#ecfeff;font-size:12px;letter-spacing:.08em;text-transform:uppercase;font-weight:700\">Catre · SchoolMate</div>" +
            $"<div style=\"color:#ffffff;font-size:22px;font-weight:700;margin-top:6px\">{esc(school)}</div>" +
            "</td></tr>" +
            "<tr><td style=\"padding:28px\">" +
            $"<div style=\"display:inline-block;padding:4px 10px;border-radius:8px;background:#f0fdfa;color:#0f766e;font-size:12px;font-weight:700\">{esc(kind)}</div>" +
            $"<h1 style=\"margin:12px 0 0;font-size:24px;line-height:1.25;color:#0f172a\">{esc(title)}</h1>" +
            dateHtml +
            detailsHtml +
            "<hr style=\"border:none;border-top:1px solid #e2e8f0;margin:24px 0\"/>" +
            "<p style=\"margin:0;font-size:13px;color:#64748b;line-height:1.5\">Attachments: your uploaded file (if any) plus a Catre notice PDF for your records.</p>" +
            "</td></tr>" +
            "<tr><td style=\"padding:16px 28px 22px;background:#f8fafc;color:#94a3b8;font-size:12px\">" +
            $"Sent for {esc(school)} · End of notice · © Catre Technology" +
            "</td></tr>" +
            "</table></td></tr></table></body></html>";

        return (subject, plain.ToString(), html);
    }
}
