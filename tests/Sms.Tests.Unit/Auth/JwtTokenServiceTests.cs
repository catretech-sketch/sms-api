using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService Service() => new(
        new JwtOptions
        {
            Issuer = "sms",
            Audience = "sms-apps",
            SigningKey = "test-signing-key-at-least-32-bytes-long!!",
            AccessTokenMinutes = 15
        },
        new SystemClock());

    [Fact]
    public void Issues_access_token_with_sub_tenant_role_claims()
    {
        var svc = Service();
        var token = svc.IssueAccess(
            userId: Guid.NewGuid(), tenantId: Guid.NewGuid(),
            roles: new[] { "school.admin" }, isPlatform: false);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "sub");
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id");
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "school.admin");
    }

    [Fact]
    public void Issues_opaque_refresh_token_that_is_unique()
    {
        var svc = Service();
        svc.NewRefreshToken().Should().NotBe(svc.NewRefreshToken());
    }
}
