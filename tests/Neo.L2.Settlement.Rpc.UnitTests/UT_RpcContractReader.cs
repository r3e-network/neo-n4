using System.Numerics;
using Neo.Json;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public sealed class UT_RpcContractReader
{
    [TestMethod]
    public void BuildParamsArray_EncodesEverySupportedReferenceAndScalarType()
    {
        var hash160 = UInt160.Parse("0x" + new string('1', 40));
        var hash256 = UInt256.Parse("0x" + new string('2', 64));
        var bytes = new byte[] { 0x01, 0x02, 0x03 };

        var parameters = RpcContractReader.BuildParamsArray(
            new object[] { 7u, 8ul, -9, "neo", hash160, hash256, bytes });

        Assert.AreEqual(7, parameters.Count);
        AssertEntry(parameters, 0, "Integer", "7");
        AssertEntry(parameters, 1, "Integer", "8");
        AssertEntry(parameters, 2, "Integer", "-9");
        AssertEntry(parameters, 3, "String", "neo");
        AssertEntry(parameters, 4, "Hash160", hash160.ToString());
        AssertEntry(parameters, 5, "Hash256", hash256.ToString());
        AssertEntry(parameters, 6, "ByteArray", Convert.ToBase64String(bytes));
        Assert.ThrowsExactly<ArgumentException>(() =>
            RpcContractReader.BuildParamsArray(new object[] { DateTime.UnixEpoch }));
    }

    [TestMethod]
    public void ParseStackItems_RejectsWrongShapeAndDecodesCanonicalByteStrings()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RpcContractReader.ParseBoolean(null));

        var encodedInteger = new JObject
        {
            ["type"] = "ByteString",
            ["value"] = Convert.ToBase64String(new byte[] { 0xff, 0x00 }),
        };
        Assert.AreEqual(new BigInteger(255), RpcContractReader.ParseBigInteger(encodedInteger));

        var emptyInteger = new JObject
        {
            ["type"] = "ByteString",
            ["value"] = string.Empty,
        };
        Assert.AreEqual(BigInteger.Zero, RpcContractReader.ParseBigInteger(emptyInteger));

        var integerBoolean = new JObject
        {
            ["type"] = "Integer",
            ["value"] = "1",
        };
        Assert.IsTrue(RpcContractReader.ParseBoolean(integerBoolean));

        var addressBytes = Enumerable.Range(1, UInt160.Length).Select(static value => (byte)value).ToArray();
        var encodedAddress = new JObject
        {
            ["type"] = "ByteString",
            ["value"] = Convert.ToBase64String(addressBytes),
        };
        Assert.AreEqual(new UInt160(addressBytes), RpcContractReader.ParseUInt160(encodedAddress));

        var shortAddress = new JObject
        {
            ["type"] = "ByteString",
            ["value"] = Convert.ToBase64String(addressBytes[..^1]),
        };
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RpcContractReader.ParseUInt160(shortAddress));
    }

    private static void AssertEntry(
        JArray parameters,
        int index,
        string expectedType,
        string expectedValue)
    {
        var entry = (JObject)parameters[index]!;
        Assert.AreEqual(expectedType, entry["type"]?.AsString());
        Assert.AreEqual(expectedValue, entry["value"]?.AsString());
    }
}
