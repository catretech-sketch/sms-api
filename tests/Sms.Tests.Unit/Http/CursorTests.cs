using FluentAssertions;
using Sms.Shared.Kernel.Http;
using Xunit;

namespace Sms.Tests.Unit.Http;

public class CursorTests
{
    [Fact]
    public void Encode_then_Decode_roundtrips()
    {
        var key = "Sharma|3f2504e0-4f89-41d3-9a0c-0305e82c3301";
        Cursor.Decode(Cursor.Encode(key)).Should().Be(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-valid-base64!!")]
    public void Decode_returns_null_for_empty_or_malformed(string? input)
    {
        Cursor.Decode(input).Should().BeNull();
    }

    [Fact]
    public void Encode_is_url_safe_base64_without_padding_newlines()
    {
        var c = Cursor.Encode("a|b");
        c.Should().NotContain("\n").And.NotContain("+").And.NotContain("/").And.NotContain("=");
    }
}
