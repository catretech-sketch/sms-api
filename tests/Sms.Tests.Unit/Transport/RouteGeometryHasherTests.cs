using Sms.Modules.Transport;
using Xunit;

namespace Sms.Tests.Unit.Transport;

public class RouteGeometryHasherTests
{
    static RouteStopListItem Stop(Guid id, int seq, double lat, double lng) =>
        new(id, Guid.NewGuid(), "Stop", seq, lat, lng);

    [Fact]
    public void Same_input_produces_same_hash()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var stops = new[] { Stop(id1, 1, 12.1, 77.1), Stop(id2, 2, 12.2, 77.2) };

        var h1 = RouteGeometryHasher.Compute(stops);
        var h2 = RouteGeometryHasher.Compute(stops);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Reordering_stops_changes_hash()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var original = new[] { Stop(id1, 1, 12.1, 77.1), Stop(id2, 2, 12.2, 77.2) };
        var reordered = new[] { Stop(id2, 1, 12.2, 77.2), Stop(id1, 2, 12.1, 77.1) };

        Assert.NotEqual(RouteGeometryHasher.Compute(original), RouteGeometryHasher.Compute(reordered));
    }

    [Fact]
    public void Changing_a_coordinate_changes_hash()
    {
        var id1 = Guid.NewGuid();
        var before = new[] { Stop(id1, 1, 12.1, 77.1) };
        var after = new[] { Stop(id1, 1, 12.10001, 77.1) };

        Assert.NotEqual(RouteGeometryHasher.Compute(before), RouteGeometryHasher.Compute(after));
    }

    [Fact]
    public void Adding_a_stop_changes_hash()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var before = new[] { Stop(id1, 1, 12.1, 77.1) };
        var after = new[] { Stop(id1, 1, 12.1, 77.1), Stop(id2, 2, 12.2, 77.2) };

        Assert.NotEqual(RouteGeometryHasher.Compute(before), RouteGeometryHasher.Compute(after));
    }
}
