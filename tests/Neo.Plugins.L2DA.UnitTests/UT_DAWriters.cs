using Neo.Cryptography;

namespace Neo.Plugins.L2DA.UnitTests;

[TestClass]
public class UT_DAWriters
{
    [TestMethod]
    public async Task InMemory_PublishHashesPayload()
    {
        var w = new InMemoryDAWriter();
        var payload = new byte[] { 1, 2, 3, 4 };
        var receipt = await w.PublishAsync(new DAPublishRequest
        {
            ChainId = 1001,
            BatchNumber = 1,
            Payload = payload,
        });

        Assert.AreEqual(DAMode.External, receipt.Layer);
        Assert.AreEqual(new UInt256(Crypto.Hash256(payload)), receipt.Commitment);
        Assert.IsTrue(await w.IsAvailableAsync(receipt));
    }

    [TestMethod]
    public void ResolveDAMode_AcceptsAllValidEnumValues()
    {
        // Boundary partner of the rejection test below.
        Assert.AreEqual(DAMode.L1, L2DAPlugin.ResolveDAMode(0));
        Assert.AreEqual(DAMode.NeoFS, L2DAPlugin.ResolveDAMode(1));
        Assert.AreEqual(DAMode.External, L2DAPlugin.ResolveDAMode(2));
        Assert.AreEqual(DAMode.DAC, L2DAPlugin.ResolveDAMode(3));
    }

    [TestMethod]
    public void ResolveDAMode_RejectsUnknownByte()
    {
        // Regression: previously L2DAPlugin.Configure silently fell through to
        // InMemoryDAWriter for any DAMode byte > 3. An operator who misconfigured
        // DAMode = 99 would think they had real DA but lose batch payloads to a
        // process-local hash table — the kind of failure that surfaces only after
        // the chain has been running for hours.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => L2DAPlugin.ResolveDAMode(99));
        StringAssert.Contains(ex.Message, "DAMode 99");
    }

    [TestMethod]
    public async Task InMemory_AvailabilityFalseForUnknownReceipt()
    {
        var w = new InMemoryDAWriter();
        var fake = new DAReceipt
        {
            Commitment = UInt256.Parse("0x" + new string('f', 64)),
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = DAMode.External,
        };
        Assert.IsFalse(await w.IsAvailableAsync(fake));
    }

    [TestMethod]
    public async Task NeoFsLike_PublishProducesPointer()
    {
        var w = new NeoFsLikeDAWriter();
        var receipt = await w.PublishAsync(new DAPublishRequest
        {
            ChainId = 1001,
            BatchNumber = 5,
            Payload = new byte[] { 0xAA, 0xBB },
        });

        Assert.AreEqual(DAMode.NeoFS, receipt.Layer);
        Assert.AreEqual(36, receipt.Pointer.Length);            // 4B chainId + 32B objectId
        Assert.AreEqual(0xE9, receipt.Pointer.Span[0]);          // 1001 = 0x000003E9 LE → low byte 0xE9
        Assert.AreEqual(0x03, receipt.Pointer.Span[1]);
        Assert.IsTrue(await w.IsAvailableAsync(receipt));
        Assert.AreEqual(1, w.ObjectCount);
    }

    [TestMethod]
    public async Task NeoFsLike_IdempotentPublish()
    {
        var w = new NeoFsLikeDAWriter();
        var payload = new byte[] { 0x10, 0x20 };

        var r1 = await w.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = 1, Payload = payload });
        var r2 = await w.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = 2, Payload = payload });

        Assert.AreEqual(r1.Commitment, r2.Commitment);          // content-addressed
        Assert.AreEqual(1, w.ObjectCount);                       // dedup'd
    }

    [TestMethod]
    public async Task NeoFsLike_PerChainIsolation()
    {
        var w = new NeoFsLikeDAWriter();
        var payload = new byte[] { 0x99 };

        var r1 = await w.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = 1, Payload = payload });
        var r2 = await w.PublishAsync(new DAPublishRequest { ChainId = 1002, BatchNumber = 1, Payload = payload });

        Assert.AreEqual(r1.Commitment, r2.Commitment);          // same content
        Assert.AreEqual(2, w.ObjectCount);                       // separate per-chain entries
        Assert.IsNotNull(w.TryGet(1001, r1.Commitment));
        Assert.IsNotNull(w.TryGet(1002, r2.Commitment));
    }

    [TestMethod]
    public async Task NeoFsLike_AvailabilityFalseForBadPointer()
    {
        var w = new NeoFsLikeDAWriter();
        var bad = new DAReceipt
        {
            Commitment = UInt256.Zero,
            Pointer = new byte[10], // wrong length
            Layer = DAMode.NeoFS,
        };
        Assert.IsFalse(await w.IsAvailableAsync(bad));
    }

    [TestMethod]
    public async Task NeoFsLike_AvailabilityFalseForUnknownObject()
    {
        var w = new NeoFsLikeDAWriter();
        await w.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = 1, Payload = new byte[] { 1 } });

        var pointer = new byte[36];
        pointer[0] = 0xE9;
        pointer[1] = 3; // chainId 1001 differs (still LE-encoded fake)
        var fake = new DAReceipt
        {
            Commitment = UInt256.Parse("0x" + new string('f', 64)),
            Pointer = pointer,
            Layer = DAMode.NeoFS,
        };
        Assert.IsFalse(await w.IsAvailableAsync(fake));
    }

    [TestMethod]
    public async Task InMemoryDAWriter_PublishAsync_RejectsNullRequest()
    {
        // Pin InMemoryDAWriter.cs:28. Null DAPublishRequest would NRE on `.Payload.Span`
        // with no link to the bad input.
        var w = new InMemoryDAWriter();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await w.PublishAsync(null!));
    }

    [TestMethod]
    public async Task InMemoryDAWriter_IsAvailableAsync_RejectsNullReceipt()
    {
        // Pin InMemoryDAWriter.cs:45.
        var w = new InMemoryDAWriter();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await w.IsAvailableAsync(null!));
    }

    [TestMethod]
    public async Task InMemoryDAWriter_IsAvailableAsync_RejectsNullCommitment()
    {
        // Pin InMemoryDAWriter.cs:49. Without it ContainsKey(null) throws ArgumentNull
        // with a generic "key" message. Same iter-148/183/184 pattern.
        var w = new InMemoryDAWriter();
        var bad = new DAReceipt { Commitment = null!, Pointer = ReadOnlyMemory<byte>.Empty, Layer = DAMode.External };
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await w.IsAvailableAsync(bad));
    }

    [TestMethod]
    public async Task NeoFsLikeDAWriter_PublishAsync_RejectsNullRequest()
    {
        // Pin NeoFsLikeDAWriter.cs:32.
        var w = new NeoFsLikeDAWriter();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await w.PublishAsync(null!));
    }

    [TestMethod]
    public async Task NeoFsLikeDAWriter_IsAvailableAsync_RejectsNullReceipt()
    {
        // Pin NeoFsLikeDAWriter.cs:59.
        var w = new NeoFsLikeDAWriter();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await w.IsAvailableAsync(null!));
    }

    [TestMethod]
    public async Task NeoFsLikeDAWriter_IsAvailableAsync_RejectsNullCommitment()
    {
        // Pin NeoFsLikeDAWriter.cs:60.
        var w = new NeoFsLikeDAWriter();
        var bad = new DAReceipt { Commitment = null!, Pointer = new byte[36], Layer = DAMode.NeoFS };
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await w.IsAvailableAsync(bad));
    }

    [TestMethod]
    public void NeoFsLikeDAWriter_TryGet_RejectsNullObjectId()
    {
        // Pin NeoFsLikeDAWriter.cs:82. UInt256 reference-typed; null would NRE inside
        // the tuple key (chainId, null) Dictionary lookup.
        var w = new NeoFsLikeDAWriter();
        Assert.ThrowsExactly<ArgumentNullException>(() => w.TryGet(1001, null!));
    }

    [TestMethod]
    public void L2DAPlugin_WithMetrics_RejectsNullMetrics()
    {
        // Pin L2DAPlugin.cs:37. Symmetric to other plugin WithMetrics pins.
        using var plugin = new L2DAPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.WithMetrics(null!));
    }

    [TestMethod]
    public void L2DAPlugin_DefaultWriter_IsInMemory()
    {
        // Pre-Configure default — pinned so a refactor that changes the field initializer
        // doesn't silently break tests / devnet that construct the plugin and immediately
        // call GetWriter() without a config section.
        using var plugin = new L2DAPlugin();
        Assert.IsInstanceOfType(plugin.GetWriter(), typeof(InMemoryDAWriter));
    }

    [TestMethod]
    public void L2DAPlugin_WithWriter_OverridesDefault()
    {
        // Production deployments inject a custom IDAWriter (real NeoFS SDK, L1 RPC client,
        // DAC committee adapter). WithWriter must replace the default before Configure runs.
        using var plugin = new L2DAPlugin();
        var custom = new NeoFsLikeDAWriter();
        plugin.WithWriter(custom);
        Assert.AreSame(custom, plugin.GetWriter());
    }

    [TestMethod]
    public void L2DAPlugin_WithWriter_RejectsNull()
    {
        using var plugin = new L2DAPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.WithWriter(null!));
    }

    [TestMethod]
    public void L2DAPlugin_NameAndDescription_AreNonEmpty()
    {
        // Surfaced in plugin host startup logs; pin so a refactor doesn't accidentally
        // empty either. Same convention as UT_L2BridgePlugin / UT_L2GatewayPlugin /
        // UT_L2ProverPlugin.
        using var plugin = new L2DAPlugin();
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Description));
    }

    [TestMethod]
    public void BuildDefaultWriter_External_NoDataDir_ReturnsInMemory()
    {
        // Pin the dev/test default — the bare External mode without a DataDirectory
        // is the path that single-node demos hit, and silently swapping it for
        // anything else would break those.
        var w = L2DAPlugin.BuildDefaultWriter(DAMode.External, dataDir: null);
        Assert.IsInstanceOfType(w, typeof(InMemoryDAWriter));
        Assert.AreEqual(DAMode.External, w.Mode);
    }

    [TestMethod]
    public void BuildDefaultWriter_NeoFS_NoDataDir_ReturnsNeoFsLike()
    {
        var w = L2DAPlugin.BuildDefaultWriter(DAMode.NeoFS, dataDir: null);
        Assert.IsInstanceOfType(w, typeof(NeoFsLikeDAWriter));
        Assert.AreEqual(DAMode.NeoFS, w.Mode);
    }

    [TestMethod]
    public void BuildDefaultWriter_L1_NoDataDir_Throws()
    {
        // L1 mode has no built-in writer; without WithWriter() OR DataDirectory the
        // plugin must surface a clear message at Configure-time rather than silently
        // falling through to in-memory (which would lose batch payloads).
        var ex = Assert.ThrowsExactly<NotSupportedException>(
            () => L2DAPlugin.BuildDefaultWriter(DAMode.L1, dataDir: null));
        StringAssert.Contains(ex.Message, "DAMode.L1");
        StringAssert.Contains(ex.Message, "WithWriter");
        StringAssert.Contains(ex.Message, "DataDirectory");
    }

    [TestMethod]
    public void BuildDefaultWriter_DAC_NoDataDir_Throws()
    {
        var ex = Assert.ThrowsExactly<NotSupportedException>(
            () => L2DAPlugin.BuildDefaultWriter(DAMode.DAC, dataDir: null));
        StringAssert.Contains(ex.Message, "DAMode.DAC");
        StringAssert.Contains(ex.Message, "WithWriter");
    }

    [TestMethod]
    public void BuildDefaultWriter_DataDirectorySet_ReturnsPersistentDAWriter()
    {
        // The production default. With DataDirectory set, mode is irrelevant: every
        // mode resolves to PersistentDAWriter over RocksDB. Pinning this prevents
        // a refactor that tightens the dataDir branch to "External only" from
        // silently regressing the production wiring.
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-da-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var w = (PersistentDAWriter)L2DAPlugin.BuildDefaultWriter(DAMode.External, dir);
            Assert.AreEqual(DAMode.External, w.Mode);
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void BuildDefaultWriter_DataDirectorySet_OverridesL1Throw()
    {
        // L1 mode without DataDirectory throws (no built-in default). With
        // DataDirectory, the same mode succeeds via PersistentDAWriter — the
        // dataDir path "wins" over the mode-specific NotSupportedException. This
        // is the operator escape hatch documented in the L1 throw message.
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-da-l1esc-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var w = (PersistentDAWriter)L2DAPlugin.BuildDefaultWriter(DAMode.L1, dir);
            Assert.AreEqual(DAMode.L1, w.Mode);
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void BuildDefaultWriter_EmptyDataDir_TreatedAsAbsent()
    {
        // string.IsNullOrWhiteSpace is the gate — empty string + whitespace must NOT
        // resolve to PersistentDAWriter (RocksDb at "" or "   " would either fail or
        // succeed in the wrong place). They should be treated as "no dataDir".
        var w1 = L2DAPlugin.BuildDefaultWriter(DAMode.External, dataDir: "");
        Assert.IsInstanceOfType(w1, typeof(InMemoryDAWriter));
        var w2 = L2DAPlugin.BuildDefaultWriter(DAMode.External, dataDir: "   ");
        Assert.IsInstanceOfType(w2, typeof(InMemoryDAWriter));
    }

    [TestMethod]
    public async Task NeoFsLike_TryGet_DefensiveCopy_CallerCannotCorruptStore()
    {
        // Regression for iter 188: TryGet previously returned the raw stored byte[]
        // wrapped in ReadOnlyMemory<byte>?. A debug consumer that mutated the returned
        // bytes would silently corrupt the store. Same iter-176 pattern as
        // KeyedStateStore.EnumerateSorted.
        var w = new NeoFsLikeDAWriter();
        var receipt = await w.PublishAsync(new DAPublishRequest
        {
            ChainId = 1001, BatchNumber = 1, Payload = new byte[] { 0x11, 0x22, 0x33 },
        });

        var first = w.TryGet(1001, receipt.Commitment)!.Value.ToArray();
        // Mutate the caller's copy.
        first[0] = 0xFF; first[1] = 0xFF; first[2] = 0xFF;

        var second = w.TryGet(1001, receipt.Commitment)!.Value.ToArray();
        Assert.AreEqual(0x11, second[0], "stored bytes must survive caller mutations");
        Assert.AreEqual(0x22, second[1]);
        Assert.AreEqual(0x33, second[2]);
    }
}
