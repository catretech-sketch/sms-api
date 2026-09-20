using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Sms.Application.Services.Comms;

public sealed record NoticePdfModel(
    string SchoolName,
    string Kind,
    string Title,
    string? DateLabel,
    string? Details);

public interface INoticePdfGenerator
{
    byte[] Generate(NoticePdfModel model);
}

/// Modern one-page calendar / announcement notice with Catre branding.
public sealed class NoticePdfGenerator : INoticePdfGenerator
{
    static NoticePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(NoticePdfModel model)
    {
        var school = string.IsNullOrWhiteSpace(model.SchoolName) ? "School" : model.SchoolName.Trim();
        var kind = string.IsNullOrWhiteSpace(model.Kind) ? "Notice" : model.Kind.Trim();
        var title = string.IsNullOrWhiteSpace(model.Title) ? kind : model.Title.Trim();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken3));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("CATRE TECHNOLOGY").Bold().FontSize(16).FontColor(Colors.Teal.Darken2);
                            c.Item().Text("SchoolMate · Academic notice").FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                        row.ConstantItem(120).AlignRight().Text(DateTime.UtcNow.ToString("dd MMM yyyy"))
                            .FontSize(9).FontColor(Colors.Grey.Medium);
                    });
                    col.Item().PaddingTop(10).LineHorizontal(1.5f).LineColor(Colors.Teal.Medium);
                });

                page.Content().PaddingTop(28).Column(col =>
                {
                    col.Item().Background(Colors.Teal.Lighten4).Padding(10).Text(kind.ToUpperInvariant())
                        .SemiBold().FontSize(10).FontColor(Colors.Teal.Darken2);
                    col.Item().PaddingTop(14).Text(title).Bold().FontSize(22).FontColor(Colors.BlueGrey.Darken4);
                    if (!string.IsNullOrWhiteSpace(model.DateLabel))
                    {
                        col.Item().PaddingTop(10).Text($"When · {model.DateLabel.Trim()}")
                            .FontSize(12).FontColor(Colors.Blue.Darken2);
                    }
                    col.Item().PaddingTop(8).Text(school).FontSize(11).FontColor(Colors.Grey.Darken1);

                    if (!string.IsNullOrWhiteSpace(model.Details))
                    {
                        col.Item().PaddingTop(20).Text("Details").SemiBold().FontSize(11);
                        col.Item().PaddingTop(6).Text(model.Details.Trim()).FontSize(11).LineHeight(1.4f);
                    }

                    col.Item().PaddingTop(28).Background(Colors.Grey.Lighten3).Padding(12).Column(box =>
                    {
                        box.Item().Text("Please keep this notice for your records.")
                            .FontSize(10).FontColor(Colors.Grey.Darken2);
                        box.Item().PaddingTop(4).Text("Parents & teachers — mark your calendar accordingly.")
                            .FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Powered by Catre · SchoolMate  ·  ").FontSize(8).FontColor(Colors.Grey.Medium);
                    t.Span("End of notice").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }
}
