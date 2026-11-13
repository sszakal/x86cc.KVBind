using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class ValueConstraintBoundsTests
{
    [Theory]
    [InlineData(1e30)]
    [InlineData(double.PositiveInfinity)]
    public void Max_WhenDoubleExceedsDecimalRange_ReportsMaxAgainstBoundOfAnotherNumericType(double value)
    {
        var constraints = new KVValueConstraints();
        constraints.SetMax(100);

        Evaluate(constraints, value).Should().ContainSingle().Which.Code.Should().Be("max");
    }

    [Theory]
    [InlineData(-1e30)]
    [InlineData(double.NegativeInfinity)]
    public void Min_WhenDoubleIsBelowDecimalRange_ReportsMinAgainstBoundOfAnotherNumericType(double value)
    {
        var constraints = new KVValueConstraints();
        constraints.SetMin(0m);

        Evaluate(constraints, value).Should().ContainSingle().Which.Code.Should().Be("min");
    }

    [Fact]
    public void Min_WhenHugeDoubleIsAboveTheBound_ReportsNothing()
    {
        var constraints = new KVValueConstraints();
        constraints.SetMin(0);

        Evaluate(constraints, 1e30).Should().BeEmpty();
    }

    [Fact]
    public void Max_WhenFloatExceedsDecimalRange_ReportsMaxAgainstLongBound()
    {
        var constraints = new KVValueConstraints();
        constraints.SetMax(100L);

        Evaluate(constraints, float.MaxValue).Should().ContainSingle().Which.Code.Should().Be("max");
    }

    [Fact]
    public void Max_WhenHugeDoubleHasADoubleBound_StillReportsMax()
    {
        var constraints = new KVValueConstraints();
        constraints.SetMax(100.0);

        Evaluate(constraints, 1e30).Should().ContainSingle().Which.Code.Should().Be("max");
    }

    [Fact]
    public void Range_WhenDoubleIsInsideTheBounds_ReportsNothing()
    {
        var constraints = new KVValueConstraints();
        constraints.SetMin(0);
        constraints.SetMax(100);

        Evaluate(constraints, 50.5).Should().BeEmpty();
    }

    private static List<KVValidationError> Evaluate(KVValueConstraints constraints, object value)
    {
        var errors = new List<KVValidationError>();
        constraints.Evaluate("Amount", value, errors);
        return errors;
    }
}
