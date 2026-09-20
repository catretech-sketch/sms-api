using Sms.Application.Services.Finance;
using Xunit;

namespace Sms.Tests.Unit.Finance;

public sealed class FeeInvoicePdfGeneratorTests
{
    [Fact]
    public void Generate_produces_non_empty_pdf_bytes_without_a_logo()
    {
        var gen = new FeeInvoicePdfGenerator();
        var bytes = gen.Generate(new FeeInvoicePdfModel(
            SchoolName: "Riverdale School", LogoUrl: null, StudentName: "Aarav Sharma",
            Period: "Term 2 2026", Amount: 8500m, PaidAmount: 8500m, DueAmount: 0m,
            Status: "paid", PaymentMethod: "Cash", PaymentDate: new DateTime(2026, 3, 15)));

        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length > 100, "a real one-page PDF is always well over 100 bytes");
        // %PDF is the standard file-signature for a PDF document.
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void Generate_does_not_throw_when_the_logo_url_is_unreachable()
    {
        var gen = new FeeInvoicePdfGenerator();
        var bytes = gen.Generate(new FeeInvoicePdfModel(
            SchoolName: "Riverdale School", LogoUrl: "https://example.invalid/does-not-exist.png",
            StudentName: "Aarav Sharma", Period: "Term 2 2026", Amount: 8500m, PaidAmount: 8500m,
            DueAmount: 0m, Status: "paid", PaymentMethod: "Cash", PaymentDate: new DateTime(2026, 3, 15)));

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Generate_does_not_throw_with_a_reference_set()
    {
        // QuestPDF embeds a subset font with its own glyph encoding, so the rendered text is not
        // reliably searchable in the raw or decompressed PDF bytes — see PayInvoiceNotifyTests'
        // CapturingPdfGenerator for the real assertion on what the Ref value actually is.
        var gen = new FeeInvoicePdfGenerator();
        var bytes = gen.Generate(new FeeInvoicePdfModel(
            SchoolName: "Riverdale School", LogoUrl: null, StudentName: "Aarav Sharma",
            Period: "Term 2 2026", Amount: 8500m, PaidAmount: 8500m, DueAmount: 0m,
            Status: "paid", PaymentMethod: "Razorpay", PaymentDate: new DateTime(2026, 3, 15),
            Ref: "pay_QWERTY12345"));

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Generate_does_not_throw_without_a_reference()
    {
        var gen = new FeeInvoicePdfGenerator();
        var bytes = gen.Generate(new FeeInvoicePdfModel(
            SchoolName: "Riverdale School", LogoUrl: null, StudentName: "Aarav Sharma",
            Period: "Term 2 2026", Amount: 8500m, PaidAmount: 8500m, DueAmount: 0m,
            Status: "paid", PaymentMethod: "Cash", PaymentDate: new DateTime(2026, 3, 15),
            Ref: null));

        Assert.NotEmpty(bytes);
    }
}
