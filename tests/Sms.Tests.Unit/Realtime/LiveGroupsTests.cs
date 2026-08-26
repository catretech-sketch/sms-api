using Sms.Application.Services.Realtime;

namespace Sms.Tests.Unit.Realtime;

public sealed class LiveGroupsTests
{
    [Fact]
    public void Tenant_and_user_groups_are_stable_guids()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Assert.Equal("tenant:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", LiveGroups.Tenant(id));
        Assert.Equal("user:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", LiveGroups.User(id));
    }
}
