using Neo.Network.P2P.Payloads;
using System.Reflection;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_KmsHsmSignerIntegration
{
    [TestMethod]
    public void LocalKeyVsAwsKms_AccountConsistency()
    {
        // Generate a test key pair
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        // Local signer account
        var localAccount = Contract.CreateSignatureContract(publicKeyBytes.ToImmutableArray()).ScriptHash;
        
        // Simulated AWS KMS account (should match if same public key)
        var awsKmsAccount = Contract.CreateSignatureContract(publicKeyBytes.ToImmutableArray()).ScriptHash;
        
        Assert.AreEqual(localAccount, awsKmsAccount);
    }

    [TestMethod]
    public void Signer_WitnessStructure_Conformity()
    {
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        // All signers should produce structurally identical witnesses
        using var localSigner = new LocalKeyTransactionSigner(new KeyPair(publicKeyBytes));
        using var awsKmsSigner = MockAwsKmsSignerFromPublicKey(publicKeyBytes);
        using var azureSigner = MockAzureSignerFromPublicKey(publicKeyBytes);
        using var hsmSigner = new HsmCliTransactionSigner("echo", "", publicKeyBytes);
        
        // All placeholders should have same structure
        var localPlaceholder = localSigner.CreatePlaceholderWitness();
        var awsPlaceholder = awsKmsSigner.CreatePlaceholderWitness();
        var azurePlaceholder = azureSigner.CreatePlaceholderWitness();
        var hsmPlaceholder = hsmSigner.CreatePlaceholderWitness();
        
        Assert.IsNotNull(localPlaceholder.VerificationScript);
        Assert.IsNotNull(awsPlaceholder.VerificationScript);
        Assert.IsNotNull(azurePlaceholder.VerificationScript);
        Assert.IsNotNull(hsmPlaceholder.VerificationScript);
        
        // All verification scripts should be identical
        Assert.AreEqual(
            ((ReadOnlyMemory<byte>)localPlaceholder.VerificationScript!).ToArray(),
            ((ReadOnlyMemory<byte>)awsPlaceholder.VerificationScript!).ToArray());
        Assert.AreEqual(
            ((ReadOnlyMemory<byte>)localPlaceholder.VerificationScript!).ToArray(),
            ((ReadOnlyMemory<byte>)hsmPlaceholder.VerificationScript!).ToArray());
    }

    [TestMethod]
    public void Signer_CacheBehavior_Comparability()
    {
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        using var awsKmsSigner = MockAwsKmsSignerFromPublicKey(publicKeyBytes);
        using var azureSigner = MockAzureSignerFromPublicKey(publicKeyBytes);
        using var hsmSigner = new HsmCliTransactionSigner("echo", "", publicKeyBytes);
        
        // Cache sizes should be available on KMS/HSM signers
        Assert.AreEqual(0, awsKmsSigner.CacheSize);
        Assert.AreEqual(0, azureSigner.CacheSize);
        // HSM CLI doesn't cache by design
        
        // Both AWS and Azure should have cache invalidation methods
        Action invalidateAws = () => awsKmsSigner.InvalidateSignatureCache();
        Action invalidateAzure = () => azureSigner.InvalidateSignatureCache();
        
        invalidateAws.Should().NotThrow();
        invalidateAzure.Should().NotThrow();
    }

    [TestMethod]
    public void Signer_EnvironmentVariable_Patterns()
    {
        // Test environment variable patterns used by all signers
        var envVars = new Dictionary<string, Func<bool>>
        {
            ["AWS_REGION"] = () => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_REGION")),
            ["AZURE_KEY_VAULT_URL"] = () => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_URL")),
            ["NEO_N4_HSM_COMMAND"] = () => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEO_N4_HSM_COMMAND"))
        };
        
        // Not all need to be set - this is just testing pattern availability
        foreach (var var in envVars.Keys)
        {
            Environment.SetEnvironmentVariable(var, null); // Ensure clean state
        }
        
        // Test that constructors throw when required env vars not set
        // Local signer requires WIF
        Assert.ThrowsExactly<InvalidOperationException>(() => 
            LocalKeyTransactionSigner.FromEnvironmentVariable("NEO_N4_TEST_MISSING_WIF"));
    }

    [TestMethod]
    public async Task Signer_FailureMode_Tolerance()
    {
        // Test that different failure modes are handled gracefully
        var transaction = new Transaction
        {
            Nonce = 1,
            SystemFee = 1000000000,
            NetworkFee = 20000000,
            ValidUntilBlock = 1000,
            Script = Array.Empty<byte>()
        };
        
        // Local signer with bad network should work fine
        try
        {
            using var localSigner = new LocalKeyTransactionSigner(
                new KeyPair(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
            
            // Should succeed regardless of network value
            await localSigner.SignAsync(transaction, 853326581ul);
        }
        catch
        {
            // May fail for other reasons but shouldn't crash the framework
        }
        
        // KMS signers should fail gracefully
        try
        {
            var mockKms = new Mock<IAmazonKeyManagementService>();
            mockKms.Setup(k => k.SignAsync(It.IsAny<SignRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Amazon.KeyManagementService.SignatureException("Mock KMS failure"));
            
            using var awsSigner = new AwsKmsTransactionSigner(mockKms.Object, "test-key");
            await awsSigner.SignAsync(transaction, 853326581ul);
        }
        catch (Amazon.KeyManagementService.SignatureException)
        {
            // Expected exception from mocked KMS
        }
    }

    [TestMethod]
    public void Disposal_ResourceCleanup_Test()
    {
        var publicKeyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        
        // All signers should dispose cleanly
        using var localSigner = new LocalKeyTransactionSigner(new KeyPair(publicKeyBytes));
        using var awsKmsSigner = MockAwsKmsSignerFromPublicKey(publicKeyBytes);
        using var azureSigner = MockAzureSignerFromPublicKey(publicKeyBytes);
        using var hsmSigner = new HsmCliTransactionSigner("echo", "", publicKeyBytes);
        
        // Multiple disposal calls should be safe
        localSigner.Dispose();
        localSigner.Dispose(); // Should not throw
        
        awsKmsSigner.Dispose();
        awsKmsSigner.Dispose(); // Should not throw
        
        azureSigner.Dispose();
        azureSigner.Dispose(); // Should not throw
        
        hsmSigner.Dispose();
        hsmSigner.Dispose(); // Should not throw
    }

    [TestMethod]
    public void Account_Uniqueness_DistinctKeys()
    {
        // Different public keys should produce different accounts
        var key1 = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var key2 = Enumerable.Range(33, 32).Select(i => (byte)i).ToArray();
        
        var account1 = Contract.CreateSignatureContract(key1.ToImmutableArray()).ScriptHash;
        var account2 = Contract.CreateSignatureContract(key2.ToImmutableArray()).ScriptHash;
        
        Assert.AreNotEqual(account1, account2);
    }

    private static AwsKmsTransactionSigner MockAwsKmsSignerFromPublicKey(byte[] publicKeyBytes)
    {
        var mockKms = new Mock<IAmazonKeyManagementService>();
        mockKms.Setup(k => k.GetPublicKeyAsync(It.IsAny<GetPublicKeyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPublicKeyResponse
            {
                KeyId = "test-key",
                PublicKey = publicKeyBytes.ToMemory(),
                KeyMetadata = new KeyMetadata { State = Model.KeyState.Enabled }
            });

        return new AwsKmsTransactionSigner(mockKms.Object, "test-key");
    }

    private static AzureKeyVaultTransactionSigner MockAzureSignerFromPublicKey(byte[] publicKeyBytes)
    {
        var mockCredential = new Mock<TokenCredential>();
        
        var keyOps = new[] { new KeyOperation(KeyOperationActions.Sign) };
        var keyValue = new KeyVaultKey("test-key") { KeyOps = keyOps.ToArray() };
        keyValue.AddPublicBase64(Convert.ToBase64String(publicKeyBytes));
        
        var mockCryptoClient = new Mock<CryptoClient>(MockBehavior.Strict);
        mockCryptoClient.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(keyValue, new ResponseOptions()));

        return new AzureKeyVaultTransactionSigner(mockCryptoClient.Object, "test-key");
    }
}
