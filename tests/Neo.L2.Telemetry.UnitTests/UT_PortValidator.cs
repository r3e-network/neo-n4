namespace Neo.L2.Telemetry.UnitTests;

[TestClass]
public class UT_PortValidator
{
    [TestMethod]
    public void Validate_AcceptsBoundaryValues()
    {
        Assert.AreEqual(0, PortValidator.Validate(0));
        Assert.AreEqual(65535, PortValidator.Validate(65535));
        Assert.AreEqual(9090, PortValidator.Validate(9090));
    }

    [TestMethod]
    public void Validate_RejectsNegative()
    {
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() => PortValidator.Validate(-1));
        StringAssert.Contains(ex.Message, "Port -1");
    }

    [TestMethod]
    public void Validate_RejectsAboveMax()
    {
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() => PortValidator.Validate(65536));
        StringAssert.Contains(ex.Message, "Port 65536");
    }

    [TestMethod]
    public void Validate_UsesContextLabelInError()
    {
        // Caller provides their own label so the operator sees which input was bad
        // (e.g. "L2Metrics Port" vs "--metrics-port" vs other future call sites).
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            PortValidator.Validate(99999, "--metrics-port"));
        StringAssert.Contains(ex.Message, "--metrics-port 99999");
    }
}
