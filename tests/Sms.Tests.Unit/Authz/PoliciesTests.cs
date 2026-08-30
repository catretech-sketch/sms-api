using FluentAssertions;
using Sms.Shared.Kernel.Authz;
using Xunit;

namespace Sms.Tests.Unit.Authz;

public class PoliciesTests
{
    [Fact]
    public void Driver_is_a_canonical_policy_alongside_the_existing_six()
    {
        Policies.All.Should().Contain(Policies.Driver);
        Policies.All.Should().HaveCount(8, "the original 7 plus the new driver policy");
    }
}
