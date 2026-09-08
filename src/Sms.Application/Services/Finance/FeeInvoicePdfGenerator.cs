using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Sms.Application.Services.Finance;

public sealed record FeeInvoicePdfModel(
    string SchoolName,
    string? LogoUrl,
    string StudentName,
    string Period,
    decimal Amount,
    decimal PaidAmount,
    decimal DueAmount,
    string Status,
    string PaymentMethod,
    DateTime PaymentDate);

public interface IFeeInvoicePdfGenerator
{
    byte[] Generate(FeeInvoicePdfModel model);
}

/// One-page fee invoice/receipt with the school's logo when configured.
public sealed class FeeInvoicePdfGenerator : IFeeInvoicePdfGenerator
{
    static FeeInvoicePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(FeeInvoicePdfModel model)
    {
        var school = string.IsNullOrWhiteSpace(model.SchoolName) ? "School" : model.SchoolName.Trim();
        byte[]? logoBytes = TryDownloadLogo(model.LogoUrl);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken3));

                page.Header().Row(row =>
                {
                    if (logoBytes is { Length: > 0 })
                        row.ConstantItem(60).Height(60).Image(logoBytes).FitArea();
                    row.RelativeItem().PaddingLeft(logoBytes is { Length: > 0 } ? 12 : 0).Column(c =>
                    {
                        c.Item().Text(school).Bold().FontSize(18).FontColor(Colors.BlueGrey.Darken4);
                        c.Item().Text("Fee Payment Receipt").FontSize(10).FontColor(Colors.Grey.Medium);
                    });
                    row.ConstantItem(120).AlignRight().Text(model.PaymentDate.ToString("dd MMM yyyy"))
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(28).Column(col =>
                {
                    col.Item().Text(model.StudentName).Bold().FontSize(16);
                    col.Item().PaddingTop(4).Text(model.Period).FontSize(12).FontColor(Colors.Grey.Darken1);

                    col.Item().PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        void Row(string label, string value)
                        {
                            table.Cell().PaddingVertical(4).Text(label).FontColor(Colors.Grey.Darken1);
                            table.Cell().PaddingVertical(4).AlignRight().Text(value).SemiBold();
                        }
                        Row("Total amount", $"{model.Amount:N2}");
                        Row("Paid amount", $"{model.PaidAmount:N2}");
                        Row("Due amount", $"{model.DueAmount:N2}");
                        Row("Status", model.Status);
                        Row("Payment method", model.PaymentMethod);
                    });
                });

                page.Footer().AlignCenter().Text("Powered by Catre · SchoolMate")
                    .FontSize(8).FontColor(Colors.Grey.Medium);
            });
        }).GeneratePdf();
    }

    private static byte[]? TryDownloadLogo(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            return http.GetByteArrayAsync(logoUrl).GetAwaiter().GetResult();
        }
        catch
        {
            return null; // best-effort — a broken logo URL must never break the invoice PDF
        }
    }
}
