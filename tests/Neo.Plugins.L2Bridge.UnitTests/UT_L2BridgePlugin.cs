namespace Neo.Plugins.L2Bridge.UnitTests;

/// <summary>
/// Tests for <see cref="L2BridgePlugin"/> — the composition root that owns
/// <c>AssetRegistry</c>, <c>DepositProcessor</c>, <c>WithdrawalProcessor</c>, and the
/// <c>L1MessageInbox</c>. Without a Neo plugin host these tests can't drive
/// <see cref="Plugin.Configure"/>; they exercise the constructor, the pre-Configure
/// access guards (iter 178 surface — properties throw <see cref="InvalidOperationException"/>
/// before Configure has run), the <see cref="L2BridgePlugin.WithMetrics"/> setter, and
/// the <see cref="L2BridgePlugin.Inbox"/> + <see cref="L2BridgePlugin.Registry"/>
/// pre-Configure-safe properties.
/// </summary>
[TestClass]
public class UT_L2BridgePlugin
{
    [TestMethod]
    public void Constructor_DoesNotThrow()
    {
        // Plugin construction must not require an active Neo host — this lets tests +
        // composition roots wire it up without starting a NeoSystem.
        using var plugin = new L2BridgePlugin();
    }

    [TestMethod]
    public void Registry_IsAccessibleBeforeConfigure()
    {
        // The asset registry has no per-chain state, so it's safe to access without
        // running Configure. Pinned because production wiring code touches Registry
        // before the host fires Configure (e.g. to seed pre-known mappings).
        using var plugin = new L2BridgePlugin();
        Assert.IsNotNull(plugin.Registry);
        Assert.AreEqual(0, plugin.Registry.Count);
    }

    [TestMethod]
    public void Inbox_IsAccessibleBeforeConfigure()
    {
        // The L1 message inbox is constructed in the field initializer, not Configure.
        // Pinned because the upstream Configure flow appends to Inbox before the host
        // even calls Configure on this plugin.
        using var plugin = new L2BridgePlugin();
        Assert.IsNotNull(plugin.Inbox);
        Assert.AreEqual(0, plugin.Inbox.PendingCount);
    }

    [TestMethod]
    public void DepositProcessor_AfterConstruction_IsAvailable()
    {
        // Neo's Plugin base ctor runs Configure() at construction time (Plugin.cs:107),
        // so by the time `new L2BridgePlugin()` returns, the iter-144 lazy init has
        // populated _depositProcessor via the `??= new DepositProcessor(...)` guard
        // in Configure. Pin so a refactor that defers Configure or breaks the lazy-init
        // ??= surfaces here.
        using var plugin = new L2BridgePlugin();
        Assert.IsNotNull(plugin.DepositProcessor);
    }

    [TestMethod]
    public void WithdrawalProcessor_AfterConstruction_IsAvailable()
    {
        using var plugin = new L2BridgePlugin();
        Assert.IsNotNull(plugin.WithdrawalProcessor);
    }

    [TestMethod]
    public void WithMetrics_RejectsNullMetrics()
    {
        // Pin L2BridgePlugin.cs:58. Symmetric to L2BatchPlugin / L2DAPlugin / L2SettlementPlugin
        // WithMetrics null pins (iters 228, 233).
        using var plugin = new L2BridgePlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.WithMetrics(null!));
    }

    [TestMethod]
    public void CreateFromChainDirectory_LoadsChainIdAndProcessors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-bridge-cfd-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Bridge");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "ChainId": 20260716
                  }
                }
                """);
            using var plugin = L2BridgePlugin.CreateFromChainDirectory(dir);
            Assert.AreEqual(20260716u, plugin.ChainId);
            Assert.AreEqual(20260716u, plugin.Settings.ChainId);
            Assert.AreEqual(20260716u, plugin.DepositProcessor.LocalChainId);
            Assert.IsNotNull(plugin.WithdrawalProcessor);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateFromChainDirectory_MissingConfig_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-bridge-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.ThrowsExactly<FileNotFoundException>(
                () => L2BridgePlugin.CreateFromChainDirectory(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateFromChainDirectory_ZeroChainId_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-bridge-zero-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Bridge");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "ChainId": 0
                  }
                }
                """);
            // ChainIdValidator rejects L2 id 0 (L1 sentinel).
            Assert.ThrowsExactly<InvalidDataException>(
                () => L2BridgePlugin.CreateFromChainDirectory(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void WithDepositSource_WiresPeek()
    {
        using var plugin = new L2BridgePlugin();
        var source = new InMemorySharedBridgeDepositSource(
            chainId: 1001,
            l2BridgeHash: UInt160.Parse("0x" + new string('e', 40)));
        plugin.WithDepositSource(source);
        Assert.AreSame(source, plugin.DepositSource);
        Assert.AreEqual(0, plugin.PeekSharedBridgeDeposits(10).Count);

        source.Enqueue(new SharedBridgeDepositRecord
        {
            Asset = UInt160.Parse("0x" + new string('a', 40)),
            Recipient = UInt160.Parse("0x" + new string('b', 40)),
            Sender = UInt160.Parse("0x" + new string('c', 40)),
            Nonce = 1,
            Amount = 10,
        });
        Assert.AreEqual(1, plugin.PeekSharedBridgeDeposits(10).Count);
        Assert.AreEqual(1, plugin.PeekSharedBridgeDeposits(10).Count, "peek must not reserve");
    }

    [TestMethod]
    public void PeekSharedBridgeDeposits_WithoutSource_ReturnsEmpty()
    {
        using var plugin = new L2BridgePlugin();
        Assert.IsNull(plugin.DepositSource);
        Assert.AreEqual(0, plugin.PeekSharedBridgeDeposits(5).Count);
    }

    [TestMethod]
    public void WithMetrics_BeforeConfigure_DoesNotThrow()
    {
        // The iter-178 lazy-init pattern: WithMetrics tolerates being called before
        // Configure (the processors don't exist yet so the per-processor WithMetrics
        // calls are no-ops via the `?.` operators in the source). Pin so a future
        // refactor that drops the null-conditional surfaces here, not as a runtime NRE
        // in the Neo plugin host's wiring phase.
        using var plugin = new L2BridgePlugin();
        var metrics = new InMemoryMetrics();
        plugin.WithMetrics(metrics);  // must not throw
    }

    [TestMethod]
    public void Plugin_NameAndDescription_AreNonEmpty()
    {
        // Surfaced in plugin host startup logs; pin so a refactor doesn't accidentally
        // empty either.
        using var plugin = new L2BridgePlugin();
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Description));
        StringAssert.Contains(plugin.Name, "L2Bridge");
    }

    [TestMethod]
    public void WithMetrics_PropagatesToExistingProcessors()
    {
        // L2BridgePlugin.WithMetrics must swap the sink on its DepositProcessor +
        // WithdrawalProcessor (the source comment claims this; the test pins it).
        // Without the propagation, operators' metrics dashboard would silently miss
        // every l2.bridge.* signal on a re-wire.
        // The plugin's _chainId is 0 outside a real Plugin host (Configure runs against
        // an empty config), so we exercise the WithdrawalProcessor path which doesn't
        // depend on per-chain message routing — only on AssetRegistry lookup by L2 asset.
        using var plugin = new L2BridgePlugin();
        var l1Asset = UInt160.Parse("0x" + new string('1', 40));
        var l2Asset = UInt160.Parse("0x" + new string('2', 40));
        plugin.Registry.Register(new Neo.L2.AssetMapping
        {
            L1Asset = l1Asset,
            L2Asset = l2Asset,
            L2ChainId = 0u,
            AssetType = Neo.L2.AssetType.Gas,
            L1Decimals = 8,
            L2Decimals = 8,
            MintBurn = true,
            LockMint = false,
            Active = true,
        });

        var captured = new InMemoryMetrics();
        plugin.WithMetrics(captured);

        var withdrawal = new Neo.L2.WithdrawalRequest
        {
            ChainId = 1U,
            EmittingContract = UInt160.Parse("0x" + new string('e', 40)),
            L2Sender = UInt160.Parse("0x" + new string('a', 40)),
            L1Recipient = UInt160.Parse("0x" + new string('b', 40)),
            L2Asset = l2Asset,
            Amount = new System.Numerics.BigInteger(1000),
            Nonce = 1,
        };
        plugin.WithdrawalProcessor.Stage(withdrawal);

        Assert.AreEqual(1, captured.GetCounter(MetricNames.WithdrawalsStaged),
            "metrics swap must reach the existing WithdrawalProcessor");
    }

    [TestMethod]
    public void WithMetrics_PropagatesToDepositProcessor()
    {
        // Symmetric pin to WithMetrics_PropagatesToExistingProcessors above. The plugin
        // wires the metrics sink to BOTH the DepositProcessor and WithdrawalProcessor;
        // the existing test only catches a regression on the withdrawal side.
        // Without this counterpart, a refactor that drops `_depositProcessor?.WithMetrics(metrics)`
        // would silently lose every l2.bridge.deposits/deposits_rejected signal — same
        // operator pain as the withdrawal-side regression but invisible until production.
        using var plugin = new L2BridgePlugin();
        var l1Asset = UInt160.Parse("0x" + new string('3', 40));
        var l2Asset = UInt160.Parse("0x" + new string('4', 40));
        plugin.Registry.Register(new Neo.L2.AssetMapping
        {
            L1Asset = l1Asset,
            L2Asset = l2Asset,
            L2ChainId = 0u,
            AssetType = Neo.L2.AssetType.Gas,
            L1Decimals = 8,
            L2Decimals = 8,
            MintBurn = true,
            LockMint = false,
            Active = true,
        });

        var captured = new InMemoryMetrics();
        plugin.WithMetrics(captured);

        // DepositProcessor.Process needs a CrossChainMessage targeting the local chain
        // (chainId 0 in the no-config-host scenario). The DepositPayload encoder builds
        // the canonical [20B L1Asset][20B L2Recipient][4B amountLen][N bytes amount].
        var recipient = UInt160.Parse("0x" + new string('b', 40));
        var payload = new Neo.L2.Bridge.DepositPayload
        {
            L1Asset = l1Asset,
            L2Recipient = recipient,
            Amount = new System.Numerics.BigInteger(500),
        };
        var message = new Neo.L2.CrossChainMessage
        {
            SourceChainId = 1u,
            TargetChainId = 0u,  // matches plugin._chainId in the no-config host
            Nonce = 7UL,
            MessageType = Neo.L2.MessageType.Deposit,
            Sender = UInt160.Parse("0x" + new string('a', 40)),
            Receiver = recipient,
            Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };
        plugin.DepositProcessor.Process(message);

        Assert.AreEqual(1, captured.GetCounter(MetricNames.DepositsProcessed),
            "metrics swap must reach the existing DepositProcessor");
    }
}
