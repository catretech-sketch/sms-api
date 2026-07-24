using FluentAssertions;
using Sms.Application.Services.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class IdentifierClassifierTests
{
    [Theory]
    [InlineData("maya@wba.edu")]
    [InlineData("priya.patel@home.com")]
    public void Classifies_email(string identifier) =>
        IdentifierClassifier.Classify(identifier).Should().Be(IdentifierKind.Email);

    [Theory]
    [InlineData("4155550142")]
    [InlineData("+91 98765 43210")]
    [InlineData("(415) 555-0142")]
    public void Classifies_phone(string identifier) =>
        IdentifierClassifier.Classify(identifier).Should().Be(IdentifierKind.Phone);

    [Theory]
    [InlineData("WBA-2024-1042")]
    [InlineData("STU2024001")]
    [InlineData("12A")]
    public void Classifies_admission_id(string identifier) =>
        IdentifierClassifier.Classify(identifier).Should().Be(IdentifierKind.AdmissionId);
}
