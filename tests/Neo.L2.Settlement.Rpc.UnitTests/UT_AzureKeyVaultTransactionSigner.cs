using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Cryptography;
using Azure.Security.KeyVault.Cryptography.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Neo.Network.P2P.Payloads;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_AzureKeyVaultTransactionSigner
{
    [TestMethod]
    public void Constructor_ThrowsArgumentNullException_WhenKeyNameIsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => 
            new AzureKeyVaultTransactionSigner(null!));
    }

    [TestMethod]
    public void Constructor_ThrowsArgumentException_WhenKeyNameIsEmpty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => 
            new AzureKeyVaultTransactionSigner(string.Empty));
    }

    [TestMethod]
    public async Task Account_IsComputedFromPublicKey()
    {
        var mockCredential = new Mock<TokenCredential>();
        
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        var mockResponse = Mock.Of<Response<KeyVaultKey>>(r => 
            r.Value == Mock.Of<KeyVaultKey>(k => 
                k.KeyOps!.Any(op => op.Action == KeyOperationActions.Sign) &&
                ((ReadOnlyMemory<byte>)op.PublicKey!).ToArray() == publicKeyBytes
                )
            );

        var mockCryptoClient = new Mock<CryptoClient>(MockBehavior.Strict);
        mockCryptoClient.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse as Response<KeyVaultKey>);

        using var signer = new AzureKeyVaultTransactionSigner(mockCryptoClient.Object, "test-key");
        
        Assert.AreNotEqual(UInt160.Zero, signer.Account);
        
        var expectedAccount = Contract.CreateSignatureContract(publicKeyBytes.ToImmutableArray()).ScriptHash;
        Assert.AreEqual(expectedAccount, signer.Account);
    }

    [TestMethod]
    public void Scope_IsPreserved()
    {
        var mockCredential = new Mock<TokenCredential>();
        
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        var mockResponse = Mock.Of<Response<KeyVaultKey>>(r => 
            r.Value == Mock.Of<KeyVaultKey>(k => 
                k.KeyOps!.Any(op => op.Action == KeyOperationActions.Sign) &&
                ((ReadOnlyMemory<byte>)op.PublicKey!).ToArray() == publicKeyBytes
                )
            );

        var mockCryptoClient = new Mock<CryptoClient>(MockBehavior.Strict);
        mockCryptoClient.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse as Response<KeyVaultKey>);

        using var signer = new AzureKeyVaultTransactionSigner(mockCryptoClient.Object, "test-key", WitnessScope.Global);
        
        Assert.AreEqual(WitnessScope.Global, signer.Scope);
    }

    [TestMethod]
    public void CreatePlaceholderWitness_GeneratesCorrectStructure()
    {
        var mockCredential = new Mock<TokenCredential>();
        
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        var mockResponse = Mock.Of<Response<KeyVaultKey>>(r => 
            r.Value == Mock.Of<KeyVaultKey>(k => 
                k.KeyOps!.Any(op => op.Action == KeyOperationActions.Sign) &&
                ((ReadOnlyMemory<byte>)op.PublicKey!).ToArray() == publicKeyBytes
                )
            );

        var mockCryptoClient = new Mock<CryptoClient>(MockBehavior.Strict);
        mockCryptoClient.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse as Response<KeyVaultKey>);

        using var signer = new AzureKeyVaultTransactionSigner(mockCryptoClient.Object, "test-key");
        
        var placeholder = signer.CreatePlaceholderWitness();
        
        Assert.IsNotNull(placeholder.VerificationScript);
        Assert.AreEqual(publicKeyBytes.ToImmutableArray(), 
            ((ReadOnlyMemory<byte>)placeholder.VerificationScript!.Script).ToArray());
        Assert.IsNotNull(placeholder.InvocationScript);
        Assert.IsTrue(placeholder.InvocationScript.Length == 0);
    }

    [TestMethod]
    public async Task SignAsync_SignsTransaction()
    {
        var mockCredential = new Mock<TokenCredential>();
        
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var signatureBytes = Enumerable.Range(1, 65).Select(i => (byte)i).ToArray();
        
        var keyOps = new[] { new KeyOperation(KeyOperationActions.Sign) };
        var keyValue = new KeyVaultKey("test-key") { KeyOps = keyOps.ToArray() };
        keyValue.AddPublicBase64(Convert.ToBase64String(publicKeyBytes));
        
        var signedValue = new SignedJsonWebKey(signatureBytes.ToMemory()) { KeyId = "test-key" };
        
        var mockCryptoClient = new Mock<CryptoClient>(MockBehavior.Strict);
        
        mockCryptoClient.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(keyValue, new ResponseOptions()));
            
        mockCryptoClient.Setup(c => c.SignAsync(
            SignatureAlgorithm.RS256,
            It.Is<byte[]>(m => m.Length == 32),
            It.IsAny<SignParameters>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(signedValue, new ResponseOptions()));

        using var signer = new AzureKeyVaultTransactionSigner(mockCryptoClient.Object, "test-key");
        
        var transaction = new Transaction
        {
            Nonce = 1,
            SystemFee = 1000000000,
            NetworkFee = 20000000,
            ValidUntilBlock = 1000,
            Script = Array.Empty<byte>()
        };

        var witness = await signer.SignAsync(transaction, 853326581ul);
        
        Assert.IsNotNull(witness.InvocationScript);
        Assert.AreEqual(signatureBytes.ToImmutableArray(), 
            ((ReadOnlyMemory<byte>)witness.InvocationScript!).ToArray());
        Assert.IsNotNull(witness.VerificationScript);
    }

    [TestMethod]
    public void Dispose_DisposesCryptoClient()
    {
        var mockCredential = new Mock<TokenCredential>();
        
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        var keyOps = new[] { new KeyOperation(KeyOperationActions.Sign) };
        var keyValue = new KeyVaultKey("test-key") { KeyOps = keyOps.ToArray() };
        keyValue.AddPublicBase64(Convert.ToBase64String(publicKeyBytes));
        
        var mockCryptoClient = new Mock<CryptoClient>(MockBehavior.Strict);
        mockCryptoClient.As<IDisposable>();
        mockCryptoClient.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(keyValue, new ResponseOptions()));

        using var signer = new AzureKeyVaultTransactionSigner(mockCryptoClient.Object, "test-key");
        
        Action act = () => signer.Dispose();
        act.Should().NotThrow();
    }

    [TestMethod]
    public async Task CacheSize_ReturnsZeroAfterCreation()
    {
        var mockCredential = new Mock<TokenCredential>();
        
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        var keyOps = new[] { new KeyOperation(KeyOperationActions.Sign) };
        var keyValue = new KeyVaultKey("test-key") { KeyOps = keyOps.ToArray() };
        keyValue.AddPublicBase64(Convert.ToBase64String(publicKeyBytes));
        
        var mockCryptoClient = new Mock<CryptoClient>(MockBehavior.Strict);
        mockCryptoClient.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(keyValue, new ResponseOptions()));

        using var signer = new AzureKeyVaultTransactionSigner(mockCryptoClient.Object, "test-key");
        
        Assert.AreEqual(0, signer.CacheSize);
    }

    [TestMethod]
    public async Task CheckKeyStatus_ThrowsWhenNoSigningOperations()
    {
        var mockCredential = new Mock<TokenCredential>();
        
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        // Create key without signing operations
        var keyOps = new[] { new KeyOperation(KeyOperationActions.Encrypt) };
        var keyValue = new KeyVaultKey("test-key") { KeyOps = keyOps.ToArray() };
        keyValue.AddPublicBase64(Convert.ToBase64String(publicKeyBytes));
        
        var mockCryptoClient = new Mock<CryptoClient>(MockBehavior.Strict);
        mockCryptoClient.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(keyValue, new ResponseOptions()));

        using var signer = new AzureKeyVaultTransactionSigner(mockCryptoClient.Object, "test-key");
        
        var status = await signer.CheckKeyStatusAsync();
        
        Assert.AreEqual(KeyVaultKeyStatus.NoSigningOperations, status);
    }
}
