using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _h = new PasswordHasher();

    [Fact]
    public void Verify_succeeds_for_correct_password()
    {
        var hash = _h.Hash("Secret123!");
        _h.Verify("Secret123!", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_fails_for_wrong_password()
    {
        var hash = _h.Hash("Secret123!");
        _h.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_is_salted_so_two_hashes_differ()
    {
        _h.Hash("same").Should().NotBe(_h.Hash("same"));
    }
}
