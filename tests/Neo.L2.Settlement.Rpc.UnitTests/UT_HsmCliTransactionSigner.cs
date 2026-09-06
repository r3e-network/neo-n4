using Neo.Network.P2P.Payloads;
using System.Text.Json;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_HsmCliTransactionSigner
{
    [TestMethod]
    public void Constructor_ThrowsArgumentNullException_WhenHsmCommandIsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => 
            new HsmCliTransactionSigner(null!, Array.Empty<byte>()));
    }

    [TestMethod]
    public void Constructor_ThrowsArgumentException_WhenHsmCommandIsEmpty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => 
            new HsmCliTransactionSigner(string.Empty, Array.Empty<byte>()));
    }

    [TestMethod]
    public void Constructor_ThrowsArgumentNullException_WhenPublicKeyBytesIsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => 
            new HsmCliTransactionSigner("echo", null!));
    }

    [TestMethod]
    public void Constructor_ThrowsArgumentException_WhenPublicKeyBytesIsEmpty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => 
            new HsmCliTransactionSigner("echo", Array.Empty<byte>()));
    }

    [TestMethod]
    public async Task Account_IsComputedFromPublicKey()
    {
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        using var signer = new HsmCliTransactionSigner("echo", "", publicKeyBytes);
        
        Assert.AreNotEqual(UInt160.Zero, signer.Account);
        
        var expectedAccount = Contract.CreateSignatureContract(publicKeyBytes.ToImmutableArray()).ScriptHash;
        Assert.AreEqual(expectedAccount, signer.Account);
    }

    [TestMethod]
    public void Scope_IsPreserved()
    {
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        using var signer = new HsmCliTransactionSigner("echo", "", publicKeyBytes, WitnessScope.Global);
        
        Assert.AreEqual(WitnessScope.Global, signer.Scope);
    }

    [TestMethod]
    public void CreatePlaceholderWitness_GeneratesCorrectStructure()
    {
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        using var signer = new HsmCliTransactionSigner("echo", "", publicKeyBytes);
        
        var placeholder = signer.CreatePlaceholderWitness();
        
        Assert.IsNotNull(placeholder.VerificationScript);
        Assert.AreEqual(publicKeyBytes.ToImmutableArray(), 
            ((ReadOnlyMemory<byte>)placeholder.VerificationScript!.Script).ToArray());
        Assert.IsNotNull(placeholder.InvocationScript);
        Assert.IsTrue(placeholder.InvocationScript.Length == 0);
    }

    [TestMethod]
    public async Task Dispose_DisposesWithoutError()
    {
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        using var signer = new HsmCliTransactionSigner("echo", "", publicKeyBytes);
        
        // Disposal should not throw
        Action act = () => signer.Dispose();
        act.Should().NotThrow();
    }

    [TestMethod]
    public async Task CacheSize_ReturnsZeroAfterCreation()
    {
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        using var signer = new HsmCliTransactionSigner("echo", "", publicKeyBytes);
        
        // HSM CLI doesn't maintain cache internally
        Assert.AreEqual(0, signer.CacheSize);
    }

    [TestMethod]
    public void SigningRequest_Deserialization_Tests()
    {
        var txHash = Convert.ToBase64String(new byte[32]);
        var network = 853326581u;
        var timestamp = DateTime.UtcNow.ToUnixTimeSeconds();
        
        var request = new HsmCliTransactionSigner.SigningRequest
        {
            TxHash = txHash,
            Network = (int)network,
            Timestamp = timestamp
        };
        
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<HsmCliTransactionSigner.SigningRequest>(json);
        
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(txHash, deserialized.TxHash);
        Assert.AreEqual((int)network, deserialized.Network);
        Assert.AreEqual(timestamp, deserialized.Timestamp);
    }

    [TestMethod]
    public void SigningResponse_Serialization_Tests()
    {
        var signature = Convert.ToBase64String(new byte[65]);
        
        var response = new HsmCliTransactionSigner.SigningResponse
        {
            Success = true,
            Signature = signature,
            Error = null
        };
        
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<HsmCliTransactionSigner.SigningResponse>(json);
        
        Assert.IsNotNull(deserialized);
        Assert.IsTrue(deserialized.Success);
        Assert.AreEqual(signature, deserialized.Signature);
        Assert.IsNull(deserialized.Error);
    }

    [TestMethod]
    public void BuildSigningRequest_FormatsCorrectly()
    {
        var txHash = Enumerable.Repeat((byte)42, 32).ToArray();
        var network = 853326581u;
        
        var request = HsmCliTransactionSigner.BuildSigningRequestForTest(txHash, network);
        
        Assert.IsNotNull(request);
        Assert.IsTrue(request.Contains("tx"));
        Assert.IsTrue(request.Contains("network"));
        Assert.IsTrue(request.Contains("timestamp"));
    }

    [TestMethod]
    public void ParseSigningResponse_Successful()
    {
        var signature = Convert.ToBase64String(new byte[65]);
        var jsonResponse = $"{{\"success\":true,\"signature\":\"{signature}\"}}";
        
        var response = JsonSerializer.Deserialize<HsmCliTransactionSigner.SigningResponse>(jsonResponse);
        
        Assert.IsNotNull(response);
        Assert.IsTrue(response.Success);
        Assert.AreEqual(signature, response.Signature);
    }

    [TestMethod]
    public void ParseSigningResponse_Failure()
    {
        var errorMessage = "HSM device not available";
        var jsonResponse = $"{{\"success\":false,\"error\":\"{errorMessage}\"}}";
        
        var response = JsonSerializer.Deserialize<HsmCliTransactionSigner.SigningResponse>(jsonResponse);
        
        Assert.IsNotNull(response);
        Assert.IsFalse(response.Success);
        Assert.AreEqual(errorMessage, response.Error);
        Assert.IsNull(response.Signature);
    }

    private static class BuildSigningRequestExtensions
    {
        public static string BuildSigningRequestForTest(byte[] txHash, uint network)
        {
            var request = new Dictionary<string, object>
            {
                ["tx"] = Convert.ToBase64String(txHash),
                ["network"] = (int)network,
                ["timestamp"] = DateTime.UtcNow.ToUnixTimeSeconds()
            };
            
            return JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
    }
}
