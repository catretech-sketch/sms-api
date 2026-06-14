using FluentAssertions;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Tenancy;

public class TenantContextTests
{
    [Fact]
    public void Holds_tenant_and_user_and_platform_flag()
    {
        var ctx = new TenantContext();
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        ctx.Set(tid, uid, isPlatform: true);
        ctx.TenantId.Should().Be(tid);
        ctx.UserId.Should().Be(uid);
        ctx.IsPlatform.Should().BeTrue();
    }

    [Fact]
    public void Unset_context_has_null_ids()
    {
        var ctx = new TenantContext();
        ctx.TenantId.Should().BeNull();
        ctx.UserId.Should().BeNull();
    }
}
