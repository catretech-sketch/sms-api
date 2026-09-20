using FluentAssertions;
using Sms.Shared.Kernel.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class StaffRoleMapperTests
{
    [Theory]
    [InlineData("Driver", "driver")]
    [InlineData("driver", "driver")]
    [InlineData("Conductor", "conductor")]
    [InlineData("Bus Attendant", "conductor")]
    [InlineData("BUS ATTENDANT", "conductor")]
    [InlineData("Watchman", "guard")]
    [InlineData("Security Guard", "guard")]
    [InlineData("Peon", "peon")]
    [InlineData("Sweeper", "sweeper")]
    [InlineData("Gardener", "gardener")]
    public void Maps_recognized_crm_labels_to_canonical_role_key(string raw, string expected) =>
        StaffRoleMapper.ToRoleKey(raw).Should().Be(expected);

    [Fact]
    public void Trims_surrounding_whitespace_before_matching() =>
        StaffRoleMapper.ToRoleKey("  Driver  ").Should().Be("driver");

    [Fact]
    public void Unrecognized_label_falls_back_to_null_instead_of_guessing() =>
        StaffRoleMapper.ToRoleKey("Principal").Should().BeNull();

    [Fact]
    public void Null_input_falls_back_to_null() =>
        StaffRoleMapper.ToRoleKey(null).Should().BeNull();

    [Fact]
    public void Blank_input_falls_back_to_null() =>
        StaffRoleMapper.ToRoleKey("   ").Should().BeNull();
}
