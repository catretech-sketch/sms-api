using FluentAssertions;
using Sms.Migrations;

namespace Sms.Tests.Integration.Migrations;

public class MigrateCliResolveTests
{
    [Fact]
    public void Arg_wins_over_env() =>
        MigrateCli.ResolveConnectionString(["from-arg"], "from-env").Should().Be("from-arg");

    [Fact]
    public void Falls_back_to_env_when_no_arg() =>
        MigrateCli.ResolveConnectionString([], "from-env").Should().Be("from-env");

    [Fact]
    public void Whitespace_arg_falls_back_to_env() =>
        MigrateCli.ResolveConnectionString(["   "], "from-env").Should().Be("from-env");

    [Fact]
    public void Null_when_neither_present() =>
        MigrateCli.ResolveConnectionString([], null).Should().BeNull();
}
