using System.Reflection;
using FluentAssertions;
using Sms.Shared.Kernel.Data;
using Xunit;

namespace Sms.Tests.Unit.Data;

public class BaseRepositoryShapeTests
{
    [Theory]
    [InlineData("QueryProcAsync")]
    [InlineData("QuerySingleProcAsync")]
    [InlineData("ExecuteProcAsync")]
    [InlineData("QueryInlineAsync")]
    public void Exposes_data_helpers(string method)
    {
        typeof(BaseRepository)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(m => m.Name)
            .Should().Contain(method);
    }
}
