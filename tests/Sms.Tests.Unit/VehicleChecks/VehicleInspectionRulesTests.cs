using Sms.Modules.VehicleChecks;
using Xunit;

namespace Sms.Tests.Unit.VehicleChecks;

public class VehicleInspectionRulesTests
{
    [Fact]
    public void All_eight_checks_true_computes_all_ok_true()
    {
        Assert.True(VehicleInspectionRules.ComputeAllOk(
            brakes: true, tyres: true, lights: true, horn: true,
            firstAidKit: true, fireExtinguisher: true, emergencyExit: true, fuelLevel: true));
    }

    [Theory]
    [InlineData(false, true, true, true, true, true, true, true)]
    [InlineData(true, false, true, true, true, true, true, true)]
    [InlineData(true, true, false, true, true, true, true, true)]
    [InlineData(true, true, true, false, true, true, true, true)]
    [InlineData(true, true, true, true, false, true, true, true)]
    [InlineData(true, true, true, true, true, false, true, true)]
    [InlineData(true, true, true, true, true, true, false, true)]
    [InlineData(true, true, true, true, true, true, true, false)]
    public void Any_single_failed_check_computes_all_ok_false(
        bool brakes, bool tyres, bool lights, bool horn,
        bool firstAidKit, bool fireExtinguisher, bool emergencyExit, bool fuelLevel)
    {
        Assert.False(VehicleInspectionRules.ComputeAllOk(
            brakes, tyres, lights, horn, firstAidKit, fireExtinguisher, emergencyExit, fuelLevel));
    }

    [Fact]
    public void All_eight_checks_false_computes_all_ok_false()
    {
        Assert.False(VehicleInspectionRules.ComputeAllOk(
            brakes: false, tyres: false, lights: false, horn: false,
            firstAidKit: false, fireExtinguisher: false, emergencyExit: false, fuelLevel: false));
    }
}
