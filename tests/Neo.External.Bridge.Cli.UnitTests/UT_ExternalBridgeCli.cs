using Neo.Cryptography;
using Neo.External.Bridge.Cli.Commands;
using Neo.Wallets;
using NeoECCurve = Neo.Cryptography.ECC.ECCurve;

namespace Neo.External.Bridge.Cli.UnitTests;

[TestClass]
public sealed class UT_ExternalBridgeCli
{
    [TestMethod]
    public void Program_NoArgs_PrintsUsage()
    {
        var r = Capture(() => Program.Main(Array.Empty<string>()));

        Assert.AreEqual(0, r.Code);
        StringAssert.Contains(r.Stdout, "Usage: neo-external-bridge");
    }

    [TestMethod]
    public void Program_UnknownSubcommand_ReturnsUsageError()
    {
        var r = Capture(() => Program.Main(new[] { "does-not-exist" }));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "unknown subcommand");
        StringAssert.Contains(r.Stdout, "Usage: neo-external-bridge");
    }

    [TestMethod]
    public void GenKey_WritesPrivateKey_AndPrintsPublicIdentityOnlyByDefault()
    {
        using var temp = new TempDir();
        var outPath = Path.Combine(temp.DirectoryPath, "watcher.priv");

        var r = Capture(() => GenKeyCommand.Run(new[] { "--out", outPath }));

        Assert.AreEqual(0, r.Code);
        Assert.IsTrue(File.Exists(outPath));
        Assert.AreEqual(32, File.ReadAllBytes(outPath).Length);

        var pub33 = ExtractAssignedHex(r.Stdout, "pub33");
        var ethAddr = ExtractAssignedHex(r.Stdout, "ethAddr");
        Assert.AreEqual(66, pub33.Length);
        Assert.AreEqual(40, ethAddr.Length);
        Assert.IsFalse(r.Stdout.Contains("priv32", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GenKey_RefusesToOverwriteExistingPrivateKeyFile()
    {
        using var temp = new TempDir();
        var outPath = Path.Combine(temp.DirectoryPath, "watcher.priv");
        File.WriteAllBytes(outPath, new byte[] { 1, 2, 3 });

        var r = Capture(() => GenKeyCommand.Run(new[] { "--out", outPath }));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "refusing to overwrite");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(outPath));
    }

    [TestMethod]
    public void CommitteeBlob_ValidPubkeys_EmitsCanonicalNeoBlobAndEthAddresses()
    {
        var pub1 = PubHex(seed: 1);
        var pub2 = PubHex(seed: 2);

        var r = Capture(() => CommitteeBlobCommand.Run(new[] { "--pubs", $"{pub1},{pub2}" }));

        Assert.AreEqual(0, r.Code);
        StringAssert.Contains(r.Stdout, "# committee size = 2");
        StringAssert.Contains(r.Stdout, $"{pub1}{pub2[2..]}");
        StringAssert.Contains(r.Stdout, "#     [0] = 0x");
        StringAssert.Contains(r.Stdout, "#     [1] = 0x");
    }

    [TestMethod]
    public void CommitteeBlob_RejectsDuplicatePubkeys()
    {
        var pub = PubHex(seed: 7);

        var r = Capture(() => CommitteeBlobCommand.Run(new[] { "--pubs", $"{pub},{pub}" }));

        Assert.AreEqual(2, r.Code);
        StringAssert.Contains(r.Stderr, "duplicate");
        StringAssert.Contains(r.Stderr, "committee members must be distinct");
    }

    [TestMethod]
    public void CommitteeBlob_RejectsInvalidCompressedPoint()
    {
        var invalidPoint = "0x05" + new string('0', 64);

        var r = Capture(() => CommitteeBlobCommand.Run(new[] { "--pubs", invalidPoint }));

        Assert.AreEqual(2, r.Code);
        StringAssert.Contains(r.Stderr, "valid secp256k1 point");
    }

    [TestMethod]
    public void DeployBundle_ValidInputs_PrintOrderedWireUpPlan()
    {
        var r = Capture(() => DeployBundleCommand.Run(DeployArgs()));

        Assert.AreEqual(0, r.Code);
        StringAssert.Contains(r.Stdout, "Step 1");
        StringAssert.Contains(r.Stdout, "RegisterCommittee");
        StringAssert.Contains(r.Stdout, "Step 4");
        StringAssert.Contains(r.Stdout, "setCommittee");
    }

    [TestMethod]
    public void DeployBundle_RejectsThresholdAboveCommitteeSize()
    {
        var r = Capture(() => DeployBundleCommand.Run(DeployArgs(threshold: "2")));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "--threshold 2 > committee size 1");
    }

    [TestMethod]
    public void DeployBundle_RejectsInvalidEthRouterAddress()
    {
        var r = Capture(() => DeployBundleCommand.Run(DeployArgs(ethRouter: "0x1234")));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "--eth-router");
        StringAssert.Contains(r.Stderr, "20-byte hex address");
    }

    [TestMethod]
    public void DeployBundle_RejectsInvalidEthCommitteeAddress()
    {
        var r = Capture(() => DeployBundleCommand.Run(DeployArgs(
            committeeBlob: "0x" + new string('1', 132),
            ethAddresses: $"0x{new string('b', 40)},0x1234")));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "--eth-addresses[1]");
        StringAssert.Contains(r.Stderr, "20-byte hex address");
    }

    [TestMethod]
    public void DeployBundle_RejectsInvalidCommitteeBlobHex()
    {
        var r = Capture(() => DeployBundleCommand.Run(DeployArgs(
            committeeBlob: "0x" + new string('1', 64) + "zz")));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "--committee-blob");
        StringAssert.Contains(r.Stderr, "invalid hex byte");
    }

    [TestMethod]
    public void DeployBundle_RejectsInvalidCommitteeBlobPoint()
    {
        var invalidPoint = "0x05" + new string('0', 64);

        var r = Capture(() => DeployBundleCommand.Run(DeployArgs(committeeBlob: invalidPoint)));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "valid secp256k1 point");
    }

    [TestMethod]
    public void DeployBundle_RejectsDuplicateCommitteePubkeys()
    {
        var pub = PubHex(seed: 1);
        var blob = pub + pub[2..];
        var ethAddresses = $"{EthAddressHex(seed: 1)},{EthAddressHex(seed: 2)}";

        var r = Capture(() => DeployBundleCommand.Run(DeployArgs(
            threshold: "1",
            committeeBlob: blob,
            ethAddresses: ethAddresses)));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "duplicates an earlier committee member");
    }

    [TestMethod]
    public void DeployBundle_RejectsDuplicateEthCommitteeAddresses()
    {
        var blob = PubHex(seed: 1) + PubHex(seed: 2)[2..];
        var eth = EthAddressHex(seed: 1);

        var r = Capture(() => DeployBundleCommand.Run(DeployArgs(
            threshold: "1",
            committeeBlob: blob,
            ethAddresses: $"{eth},{eth}")));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "duplicates an earlier committee address");
    }

    [TestMethod]
    public void DeployBundle_RejectsMismatchedCommitteeIdentity()
    {
        var r = Capture(() => DeployBundleCommand.Run(DeployArgs(
            committeeBlob: PubHex(seed: 1),
            ethAddresses: EthAddressHex(seed: 2))));

        Assert.AreEqual(1, r.Code);
        StringAssert.Contains(r.Stderr, "does not match committee pubkey");
        StringAssert.Contains(r.Stderr, EthAddressHex(seed: 1));
    }

    private static string[] DeployArgs(
        string externalChainId = "0xE0000002",
        string verifier = "0x1111111111111111111111111111111111111111",
        string registry = "0x2222222222222222222222222222222222222222",
        string escrow = "0x3333333333333333333333333333333333333333",
        string ethRouter = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        string threshold = "1",
        string? committeeBlob = null,
        string? ethAddresses = null)
    {
        committeeBlob ??= PubHex(seed: 1);
        ethAddresses ??= EthAddressHex(seed: 1);

        return new[]
        {
            "--external-chain-id", externalChainId,
            "--verifier", verifier,
            "--registry", registry,
            "--escrow", escrow,
            "--eth-router", ethRouter,
            "--threshold", threshold,
            "--committee-blob", committeeBlob,
            "--eth-addresses", ethAddresses,
        };
    }

    private static string PubHex(byte seed)
    {
        var priv = new byte[32];
        priv[^1] = seed;
        var key = new KeyPair(priv, NeoECCurve.Secp256k1);
        return "0x" + GenKeyCommand.HexLower(key.PublicKey.EncodePoint(true));
    }

    private static string EthAddressHex(byte seed)
    {
        var priv = new byte[32];
        priv[^1] = seed;
        var key = new KeyPair(priv, NeoECCurve.Secp256k1);
        var pubUncompressed = key.PublicKey.EncodePoint(false);
        var hash = pubUncompressed.AsSpan(1).ToArray().Keccak256();
        return "0x" + GenKeyCommand.HexLower(hash.AsSpan(12, 20).ToArray());
    }

    private static string ExtractAssignedHex(string stdout, string label)
    {
        foreach (var line in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (!line.StartsWith(label, StringComparison.Ordinal)) continue;
            var start = line.IndexOf("0x", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"missing 0x assignment in stdout line '{line}'");
            return line[(start + 2)..].Trim();
        }

        Assert.Fail($"missing assignment label '{label}' in stdout");
        return string.Empty;
    }

    private static Captured Capture(Func<int> action)
    {
        var oldOut = Console.Out;
        var oldErr = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var code = action();
            return new Captured(code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    private sealed record Captured(int Code, string Stdout, string Stderr);

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "neo-external-bridge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
