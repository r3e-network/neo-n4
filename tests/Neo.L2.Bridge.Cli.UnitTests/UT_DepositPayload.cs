using System.Numerics;
using System.Text;
using System.Linq;
using System.Buffers.Binary;
using Neo.L2.Bridge;

namespace Neo.L2.Bridge.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="DepositPayload"/> canonical encoding used in deposit messages.
/// </summary>
[TestClass]
public class UT_DepositPayload
{
    [TestMethod]
    public void Encode_Decode_RoundTrips_SimpleAmount()
    {
        var payload = new DepositPayload
        {
            L1Asset = UInt160.Parse("0x" + new string('a', 40)),
            L2Recipient = UInt160.Parse("0x" + new string('b', 40)),
            Amount = 1000
        };

        var encoded = payload.Encode();
        var decoded = DepositPayload.Decode(encoded);

        Assert.AreEqual(payload.L1Asset, decoded.L1Asset);
        Assert.AreEqual(payload.L2Recipient, decoded.L2Recipient);
        Assert.AreEqual(payload.Amount, decoded.Amount);
    }

    [TestMethod]
    public void Encode_Decode_RejectsNullL1Asset()
    {
        var bad = new DepositPayload
        {
            L1Asset = null!,
            L2Recipient = UInt160.Zero,
            Amount = 100
        };

        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Encode());
    }

    [TestMethod]
    public void Encode_Decode_RejectsNullL2Recipient()
    {
        var bad = new DepositPayload
        {
            L1Asset = UInt160.Zero,
            L2Recipient = null!,
            Amount = 100
        };

        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Encode());
    }

    [TestMethod]
    public void Decode_TruncatedHeader_Throws()
    {
        var truncated = new byte[43]; // Header is 44 bytes minimum
        Assert.ThrowsExactly<ArgumentException>(() => DepositPayload.Decode(truncated));
    }

    [TestMethod]
    public void Decode_TooSmall_Throws()
    {
        var small = new byte[40];
        Assert.ThrowsExactly<ArgumentException>(() => DepositPayload.Decode(small));
    }

    [TestMethod]
    public void Encode_MaximumAmountBytes_Succeeds()
    {
        // Exactly 64 bytes = maximum allowed
        var huge = BigInteger.One << 504; // ~64 bytes when serialized as unsigned LE
        var payload = new DepositPayload
        {
            L1Asset = UInt160.Parse("0x" + new string('c', 40)),
            L2Recipient = UInt160.Parse("0x" + new string('d', 40)),
            Amount = huge
        };

        var encoded = payload.Encode();
        var decoded = DepositPayload.Decode(encoded);
        
        Assert.AreEqual(huge, decoded.Amount);
    }

    [TestMethod]
    public void Encode_ExceedsMaximumAmountBytes_Throws()
    {
        // Exceeds 64 bytes
        var tooHuge = BigInteger.One << 512; // ~65 bytes when serialized
        var payload = new DepositPayload
        {
            L1Asset = UInt160.Parse("0x" + new string('e', 40)),
            L2Recipient = UInt160.Parse("0x" + new string('f', 40)),
            Amount = tooHuge
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => payload.Encode());
    }

    [TestMethod]
    public void Decode_NegativeAmountLength_Throws()
    {
        // Build a 44-byte buffer with negative length field
        var bytes = new byte[44];
        // L1 asset placeholder (20 bytes)
        for (int i = 0; i < 20; i++) bytes[i] = (byte)(i + 1);
        // L2 recipient placeholder (20 bytes) 
        for (int i = 0; i < 20; i++) bytes[20 + i] = (byte)(i + 21);
        // Write negative length (-1)
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40, 4), -1);
        
        Assert.ThrowsExactly<InvalidDataException>(() => DepositPayload.Decode(bytes));
    }

    [TestMethod]
    public void Decode_OversizedAmountLength_Throws()
    {
        // Build a 44-byte buffer with oversized length field (65 bytes exceeds max 64)
        var bytes = new byte[44];
        // L1 asset placeholder (20 bytes)
        for (int i = 0; i < 20; i++) bytes[i] = (byte)(i + 1);
        // L2 recipient placeholder (20 bytes)
        for (int i = 0; i < 20; i++) bytes[20 + i] = (byte)(i + 21);
        // Write oversized length (65 > 64 max allowed)
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40, 4), 65);
        
        Assert.ThrowsExactly<InvalidDataException>(() => DepositPayload.Decode(bytes));
    }

    [TestMethod]
    public void Decode_TrailingBytes_Throws()
    {
        var payload = new DepositPayload
        {
            L1Asset = UInt160.Parse("0x" + new string('a', 40)),
            L2Recipient = UInt160.Parse("0x" + new string('b', 40)),
            Amount = 100
        };

        var encoded = payload.Encode();
        var extended = new byte[encoded.Length + 8];
        Array.Copy(encoded, extended, encoded.Length);
        
        Assert.ThrowsExactly<InvalidDataException>(() => DepositPayload.Decode(extended));
    }

    // === PROPERTY-BASED FUZZ TESTS ===
    
    [TestMethod]
    public void Encode_Decode_RandomInputs_ProducesValidWireFormat()
    {
        var rng = new Random(unchecked((int)0xDEADBEEF));
        
        for (int i = 0; i < 100; i++)
        {
            var l1Asset = RandomUInt160(rng);
            var l2Recipient = RandomUInt160(rng);
            var amount = RandomBigInteger(rng);
            
            var payload = new DepositPayload
            {
                L1Asset = l1Asset,
                L2Recipient = l2Recipient,
                Amount = amount
            };

            var encoded = payload.Encode();
            var decoded = DepositPayload.Decode(encoded);

            Assert.AreEqual(l1Asset, decoded.L1Asset, $"Mismatch on iteration {i}");
            Assert.AreEqual(l2Recipient, decoded.L2Recipient, $"Mismatch on iteration {i}");
            Assert.AreEqual(amount, decoded.Amount, $"Mismatch on iteration {i}");
        }
    }

    [TestMethod]
    public void Encode_DeterministicOutput_IdenticalEncoding()
    {
        var rng = new Random(unchecked((int)0xCAFEBABE));
        
        for (int i = 0; i < 100; i++)
        {
            var l1Asset = RandomUInt160(rng);
            var l2Recipient = RandomUInt160(rng);
            var amount = RandomBigInteger(rng);
            
            var payload1 = new DepositPayload
            {
                L1Asset = l1Asset,
                L2Recipient = l2Recipient,
                Amount = amount
            };
            
            var payload2 = new DepositPayload
            {
                L1Asset = l1Asset,
                L2Recipient = l2Recipient,
                Amount = amount
            };

            var encoded1 = payload1.Encode();
            var encoded2 = payload2.Encode();
            
            CollectionAssert.AreEqual(encoded1, encoded2, "Non-deterministic encoding detected");
        }
    }

    [TestMethod]
    public void Boundary_Case_ZeroAmount_Succeeds()
    {
        var payload = new DepositPayload
        {
            L1Asset = UInt160.Parse("0x" + new string('a', 40)),
            L2Recipient = UInt160.Parse("0x" + new string('b', 40)),
            Amount = 0
        };

        var encoded = payload.Encode();
        var decoded = DepositPayload.Decode(encoded);
        
        Assert.AreEqual(0, decoded.Amount);
        Assert.IsTrue(encoded.Length >= 44); // At least addresses + length prefix
    }

    [TestMethod]
    public void Boundary_Case_OneAmount_Succeeds()
    {
        var payload = new DepositPayload
        {
            L1Asset = UInt160.Parse("0x" + new string('a', 40)),
            L2Recipient = UInt160.Parse("0x" + new string('b', 40)),
            Amount = 1
        };

        var encoded = payload.Encode();
        var decoded = DepositPayload.Decode(encoded);
        
        Assert.AreEqual(1, decoded.Amount);
    }

    [TestMethod]
    public void Boundary_Case_MaxUint160_Addresses_Works()
    {
        var maxAddr = UInt160.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        
        var payload = new DepositPayload
        {
            L1Asset = maxAddr,
            L2Recipient = maxAddr,
            Amount = 999999
        };

        var encoded = payload.Encode();
        var decoded = DepositPayload.Decode(encoded);
        
        Assert.AreEqual(maxAddr, decoded.L1Asset);
        Assert.AreEqual(maxAddr, decoded.L2Recipient);
    }

    [TestMethod]
    public void Edge_NullInput_ValidatesBeforeEncode()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new DepositPayload { L1Asset = null!, L2Recipient = UInt160.Zero, Amount = 0 }.Encode());
        
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new DepositPayload { L1Asset = UInt160.Zero, L2Recipient = null!, Amount = 0 }.Encode());
    }

    [TestMethod]
    public void Fuzz_VaryingAmountSizes_AllSucceed()
    {
        var rng = new Random(0x12345678);
        
        // Test various sizes of amounts: single byte, multi-byte, edge sizes
        int[] sizeBytes = { 1, 2, 4, 8, 16, 32, 64 };
        
        foreach (var targetSize in sizeBytes)
        {
            for (int i = 0; i < 20; i++)
            {
                var amountBytes = new byte[targetSize];
                rng.NextBytes(amountBytes);
                
                // Ensure non-zero for realistic test
                if (amountBytes.All(b => b == 0))
                    amountBytes[0] = 1;
                
                var amount = new BigInteger(amountBytes, isUnsigned: true, isBigEndian: false);
                
                var payload = new DepositPayload
                {
                    L1Asset = RandomUInt160(rng),
                    L2Recipient = RandomUInt160(rng),
                    Amount = amount
                };

                var encoded = payload.Encode();
                var decoded = DepositPayload.Decode(encoded);
                
                Assert.AreEqual(amount, decoded.Amount, 
                    $"Failed for amount size {targetSize} bytes, iteration {i}");
            }
        }
    }

    [TestMethod]
    public void Fuzz_SameAddressReuse_DoesNotCauseCorruption()
    {
        var rng = new Random(unchecked((int)0xAABBCCDD));
        
        for (int i = 0; i < 100; i++)
        {
            // Reuse addresses across iterations
            var reusedAddr = UInt160.Parse("0x" + new string('1', 40));
            
            var payload = new DepositPayload
            {
                L1Asset = i % 2 == 0 ? reusedAddr : RandomUInt160(rng),
                L2Recipient = i % 3 == 0 ? reusedAddr : RandomUInt160(rng),
                Amount = RandomBigInteger(rng)
            };

            var encoded = payload.Encode();
            var decoded = DepositPayload.Decode(encoded);
            
            Assert.AreEqual(payload.L1Asset, decoded.L1Asset);
            Assert.AreEqual(payload.L2Recipient, decoded.L2Recipient);
        }
    }

    [TestMethod]
    public void Property_EncoderDecomposer_InverseProperty()
    {
        var rng = new Random(unchecked((int)0xFEDCBA98));
        
        for (int i = 0; i < 100; i++)
        {
            var l1 = UInt160.Parse("0x" + GenerateRandomHex(rng, 40));
            var l2 = UInt160.Parse("0x" + GenerateRandomHex(rng, 40));
            // Use non-negative int for BigInteger construction
            var amount = new BigInteger(rng.Next());
            
            var payload = new DepositPayload { L1Asset = l1, L2Recipient = l2, Amount = amount };
            
            var encoded = payload.Encode();
            
            // Verify encoder output structure per wire format spec
            Assert.AreEqual(44 + amount.ToByteArray(isUnsigned: true, isBigEndian: false).Length,
                encoded.Length, 
                $"Encoded length mismatch at iteration {i}");
            
            var decoded = DepositPayload.Decode(encoded);
            
            Assert.AreEqual(l1, decoded.L1Asset);
            Assert.AreEqual(l2, decoded.L2Recipient);
            Assert.AreEqual(amount, decoded.Amount);
        }
    }

    private static UInt160 RandomUInt160(Random rng)
    {
        var bytes = new byte[20];
        rng.NextBytes(bytes);
        return new UInt160(bytes);
    }

    private static BigInteger RandomBigInteger(Random rng)
    {
        // Generate random amounts from 1 byte up to 64 bytes
        var len = rng.Next(1, 65);
        var bytes = new byte[len];
        rng.NextBytes(bytes);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static string GenerateRandomHex(Random rng, int charCount)
    {
        var hexChars = "0123456789abcdef";
        var result = new StringBuilder(charCount);
        for (int i = 0; i < charCount; i++)
        {
            result.Append(hexChars[rng.Next(0, 16)]);
        }
        return result.ToString();
    }
}
