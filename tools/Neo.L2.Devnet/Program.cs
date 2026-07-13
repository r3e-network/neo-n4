using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Extensions.IO;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;
using Neo.L2.Executor.RiscV;
using Neo.L2.Executor.State;
using Neo.L2.Messaging;
using Neo.L2.Persistence;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Sequencer;
using Neo.L2.State;
using Neo.L2.Audit;
using Neo.L2.Telemetry;
using Neo.Plugins.L2;
using Neo.Plugins.L2Rpc;
using Neo.Network.P2P.Payloads;
using Sample.CounterChainExecutor;

namespace Neo.L2.Devnet;

/// <summary>
/// In-process devnet runner. Boots all the off-chain L2 pieces, walks through a few batches
/// (deposit → execute → prove → verify → withdraw), and prints the resulting state. Real
/// <see cref="KeyedStateStore"/> backs the post-state-root oracle, so each batch's
/// <c>preStateRoot</c> equals the previous batch's <c>postStateRoot</c>.
/// </summary>
internal static class Program
{
    private const uint LocalChainId = 1001;
    private static readonly UInt160 NeoL1 = UInt160.Parse("0x" + new string('9', 40));
    private static readonly UInt160 NeoL2 = PlatformAssets.L2NeoAsset;
    private static readonly UInt160 GasL1 = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 GasL2 = PlatformAssets.L2GasAsset;
    private static readonly UInt160 UsdtL1 = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 UsdtL2 = PlatformAssets.L2UsdtAsset;
    private static readonly UInt160 UsdcL1 = UInt160.Parse("0x" + new string('3', 40));
    private static readonly UInt160 UsdcL2 = PlatformAssets.L2UsdcAsset;
    private static readonly UInt160 BtcL1 = UInt160.Parse("0x" + new string('4', 40));
    private static readonly UInt160 BtcL2 = PlatformAssets.L2BtcAsset;
    private static readonly UInt160 Alice = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Bob = UInt160.Parse("0x" + new string('b', 40));

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
        {
            PrintUsage();
            return 0;
        }
        var batches = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 3;
        var metricsPort = DevnetArgs.ParseMetricsPort(args);
        var dataDir = DevnetArgs.ParseDataDir(args);
        var configPath = DevnetArgs.ParseConfigPath(args);
        var executorMode = DevnetArgs.ParseExecutor(args);
        // Pull §16.2 security label from operator config so e.g. `neo-stack create-chain
        // --template validium` flows into the devnet preview without re-typing. Defaults
        // (Optimistic / NeoFS / DbftCommittee / Permissionless / gateway=off) match
        // the repository's canonical N4 DA policy when no --config is supplied.
        var labelOverrides = DevnetLabelOverrides.ReadFromConfig(configPath);

        Console.WriteLine("┌─────────────────────────────────────────────┐");
        Console.WriteLine("│  Neo Elastic Network — devnet runner v0.2    │");
        Console.WriteLine($"│  chainId = {LocalChainId}, batches = {batches,2}                      │");
        Console.WriteLine("└─────────────────────────────────────────────┘");
        Console.WriteLine();
        if (dataDir is not null)
        {
            Directory.CreateDirectory(dataDir);
            Console.WriteLine($"[persist] RocksDB-backed stores at {dataDir} (data survives restart)");
        }
        else
        {
            Console.WriteLine("[persist] in-memory stores (devnet default — data lost on restart)");
        }
        Console.WriteLine();

        // ---- Wire components ----
        var registry = new AssetRegistry();
        registry.Register(PlatformAssets.CreateGasMapping(GasL1, LocalChainId) with { L2Asset = GasL2 });
        registry.Register(PlatformAssets.CreateNeoMapping(NeoL1, LocalChainId) with { L2Asset = NeoL2 });
        registry.Register(PlatformAssets.CreateUsdtMapping(UsdtL1, LocalChainId) with { L2Asset = UsdtL2 });
        registry.Register(PlatformAssets.CreateUsdcMapping(UsdcL1, LocalChainId) with { L2Asset = UsdcL2 });
        registry.Register(PlatformAssets.CreateBtcMapping(BtcL1, LocalChainId) with { L2Asset = BtcL2 });
        Console.WriteLine($"[wire] asset registry: 5 platform mappings (NEO 0→8, GAS/USDT/USDC/BTC fixed decimals; sample GAS L1={Truncate160(GasL1)} → L2={Truncate160(GasL2)})");

        var depositProcessor = new DepositProcessor(LocalChainId, registry);
        var withdrawalProcessor = new WithdrawalProcessor(LocalChainId, registry);

        var validators = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(validators);
        var prover = new AttestationProver(signers);
        var verifier = new AttestationVerifier(validators.Select(v => v.pub), threshold: 3);
        Console.WriteLine($"[wire] {validators.Count} validators, attestation threshold = 3");

        var verifierRegistry = new VerifierRegistry();
        verifierRegistry.Register(verifier);

        // Sequencer committee — 3 sequencers, all bonded. Optionally RocksDB-backed
        // when --data-dir was supplied so state survives a restart.
        var committeeStore = MaybeRocks(dataDir, "sequencer");
        // `using` for proper disposal on shutdown — RocksDB's WAL guarantees durability,
        // but a clean dispose flushes the memtable + closes file handles deterministically
        // so a restart re-opens against a tidy DB rather than the WAL replay path.
        using var committeeProvider = committeeStore is null
            ? new InMemorySequencerCommitteeProvider(LocalChainId, maxCommitteeSize: 7)
            : new InMemorySequencerCommitteeProvider(LocalChainId, committeeStore, ownsStore: true, maxCommitteeSize: 7);
        // Skip re-registration if the store already had members from a prior run.
        if ((await committeeProvider.GetActiveCommitteeAsync()).Count == 0)
        {
            for (var i = 0; i < 3; i++)
            {
                var (pub, _) = GenKey((byte)(100 + i));
                var addr = new byte[20];
                for (var j = 0; j < 20; j++) addr[j] = (byte)(0x10 * (i + 1));
                committeeProvider.Register(pub, new UInt160(addr));
            }
        }
        var initialCommittee = await committeeProvider.GetActiveCommitteeAsync();
        Console.WriteLine($"[wire] sequencer committee: {initialCommittee.Count} active members");

        // Real state store + oracle. Seed Alice with 0 balance.
        var stateBacking = MaybeRocks(dataDir, "state");
        using var stateStore = stateBacking is null
            ? new KeyedStateStore()
            : new KeyedStateStore(stateBacking, ownsBacking: true);
        var stateRootOracle = new KeyedStateRootOracle(stateStore);
        Console.WriteLine($"[wire] keyed state store + oracle ({stateStore.Count} initial entries)");

        var rpcProofStore = MaybeRocks(dataDir, "rpc-proofs");
        using var rpcStore = rpcProofStore is null
            ? new InMemoryL2RpcStore(LocalChainId, labelOverrides.SecurityLevel)
            {
                DAMode = labelOverrides.DAMode,
                GatewayEnabled = labelOverrides.GatewayEnabled,
                Sequencer = labelOverrides.Sequencer,
                Exit = labelOverrides.Exit,
            }
            : new InMemoryL2RpcStore(LocalChainId, labelOverrides.SecurityLevel, rpcProofStore, ownsProofs: true)
            {
                DAMode = labelOverrides.DAMode,
                GatewayEnabled = labelOverrides.GatewayEnabled,
                Sequencer = labelOverrides.Sequencer,
                Exit = labelOverrides.Exit,
            };
        rpcStore.RegisterAsset(GasL1, GasL2);
        rpcStore.RegisterAsset(NeoL1, NeoL2);
        rpcStore.RegisterAsset(UsdtL1, UsdtL2);
        rpcStore.RegisterAsset(UsdcL1, UsdcL2);
        rpcStore.RegisterAsset(BtcL1, BtcL2);
        var rpc = new L2RpcMethods(rpcStore);

        // DA writer. With --data-dir, payloads are content-addressed in RocksDB so a
        // restart can resurrect any commitment. Without it, in-memory only — the
        // devnet's executor is deterministic so re-running re-derives every payload.
        // BuildDefaultWriter returns IDAWriter; concrete impls are IDisposable. The cast
        // path here is safe — InMemoryDAWriter, NeoFsLikeDAWriter, and PersistentDAWriter
        // all implement IDisposable. If a future writer doesn't, IDisposable becomes
        // optional and `daWriter as IDisposable` returns null which `using` tolerates.
        IDAWriter daWriter = dataDir is null
            ? L2DAPlugin.BuildDefaultWriter(DAMode.NeoFS, dataDir: null)
            : L2DAPlugin.BuildDefaultWriter(DAMode.NeoFS, Path.Combine(dataDir, "da"));
        using var daWriterDispose = daWriter as IDisposable;
        Console.WriteLine($"[wire] DA writer = {daWriter.GetType().Name} (mode={daWriter.Mode})");

        // Pick the per-tx executor based on --executor flag. Default `reference` keeps
        // the deterministic smoke-test path (no-op tx executor + 8-byte dummy tx).
        // `riscv` is the Neo N4 / NeoVM2 path through the PolkaVM-backed RISC-V host.
        // `counter` remains the sample custom-executor demo.
        ITransactionExecutor txExec;
        // neovm-mode also needs a dedicated NeoVM state store (separate from the L2's
        // domain stateStore — native-contract storage layout differs from KeyedStateStore's).
        // The store is disposed at end-of-run via DeferredDispose's `using` contract.
        IL2KeyValueStore? neovmStore = null;
        switch (executorMode)
        {
            case "counter":
                txExec = new CounterChainExecutor(
                    chainId: LocalChainId,
                    state: new KeyedStateStoreAdapter(stateStore),
                    emittingContract: GasL2);  // sentinel for the demo
                Console.WriteLine($"[wire] tx executor = CounterChainExecutor (chainId={LocalChainId}, --executor counter)");
                break;
            case "neovm":
                // Legacy NeoVM compatibility: useful for N3-era script checks, but not
                // the canonical Neo N4 L2 execution target.
                var neovmBacking = MaybeRocks(dataDir, "neovm-state");
                neovmStore = neovmBacking ?? new InMemoryKeyValueStore();
                NeoVMGenesisBootstrap.Run(neovmStore, NeoVMGenesisBootstrap.DefaultBootstrapSettings);
                txExec = new ApplicationEngineTransactionExecutor(
                    neovmStore, settings: NeoVMGenesisBootstrap.DefaultBootstrapSettings);
                Console.WriteLine($"[wire] tx executor = ApplicationEngineTransactionExecutor (legacy NeoVM compatibility, --executor neovm; bootstrapped {neovmStore.Count} native-contract keys)");
                break;
            case "riscv":
                if (!RiscVHost.IsAvailable)
                {
                    Console.Error.WriteLine("[wire] NeoVM2/RISC-V executor requested but neo_riscv_host is unavailable.");
                    Console.Error.WriteLine("       Build external/neo-riscv-vm and place neo_riscv_host.dll (Windows) or libneo_riscv_host.so (Linux/WSL)");
                    Console.Error.WriteLine("       on PATH/LD_LIBRARY_PATH, or use --executor reference for a smoke test.");
                    Console.Error.WriteLine("       On Windows, also put dependent runtime DLLs such as libunwind.dll on PATH.");
                    if (!string.IsNullOrWhiteSpace(RiscVHost.LastAvailabilityError))
                        Console.Error.WriteLine($"       Native load error: {RiscVHost.LastAvailabilityError}");
                    return 1;
                }
                txExec = new RiscVTransactionExecutor();
                Console.WriteLine("[wire] tx executor = RiscVTransactionExecutor (NeoVM2/RISC-V via PolkaVM, --executor riscv)");
                break;
            default:
                txExec = new ReferenceTransactionExecutor();
                Console.WriteLine("[wire] tx executor = ReferenceTransactionExecutor (no-op smoke-test default — pass --executor riscv for NeoVM2/RISC-V, --executor counter for the sample custom-executor demo, or --executor neovm for legacy NeoVM compatibility)");
                break;
        }
        var executor = new ReferenceBatchExecutor(txExec, stateRootOracle);

        var metrics = new InMemoryMetrics();
        Console.WriteLine($"[wire] in-memory metrics ({MetricNames.BatchesSealed.Substring(0, 3)} canonical naming)");
        Console.WriteLine();

        // ---- Walk through N batches ----
        var preStateRoot = stateStore.ComputeRoot(); // start from empty store root = Zero
        var allCommitments = new List<L2BatchCommitment>();
        var publicInputsByBatch = new Dictionary<ulong, PublicInputs>();
        var daReceiptsByBatch = new Dictionary<ulong, DAReceipt>();
        for (var batchNum = 1; batchNum <= batches; batchNum++)
        {
            Console.WriteLine($"────── batch #{batchNum} ──────");

            // 1. Deposit message from L1 → mint balance to Alice in the state store.
            var depositAmount = new BigInteger(1_000_000 * batchNum);
            var deposit = new DepositPayload { L1Asset = GasL1, L2Recipient = Alice, Amount = depositAmount };
            var depositMsg = MessageBuilder.Build(0, LocalChainId, (ulong)batchNum, UInt160.Zero, Alice, MessageType.Deposit, deposit.Encode());
            var mint = depositProcessor.Process(depositMsg);
            // Apply to the store (this is what a real L2BridgeContract.mint would do).
            ApplyMint(stateStore, mint.L2Asset, mint.Recipient, mint.Amount);
            Console.WriteLine($"  [deposit] minted {mint.Amount} → Alice (nonce={mint.SourceNonce})");

            // 2. Stage a withdrawal — Alice → Bob on L1.
            var withdrawalAmount = new BigInteger(10_000 * batchNum);
            var withdrawal = new WithdrawalRequest
            {
                ChainId = LocalChainId,
                EmittingContract = UInt160.Zero,
                L2Sender = Alice,
                L1Recipient = Bob,
                L2Asset = GasL2,
                Amount = withdrawalAmount,
                Nonce = (ulong)batchNum,
            };
            withdrawalProcessor.Stage(withdrawal);
            ApplyBurn(stateStore, withdrawal.L2Asset, withdrawal.L2Sender, withdrawal.Amount);
            Console.WriteLine($"  [withdraw] staged {withdrawal.Amount} from Alice → Bob (nonce={withdrawal.Nonce})");

            // 3. Build batch + run executor. Tx bytes vary by --executor mode:
            //    - reference: 8-byte dummy (legacy ReferenceTransactionExecutor just hashes them)
            //    - counter:   3 Counter opcodes that exercise IncrementCounter (state mutation),
            //                 EmitWithdrawal (withdrawal channel), EmitMessage (cross-chain channel)
            ReadOnlyMemory<byte>[] txList;
            byte[] daPayload;
            if (executorMode == "counter")
            {
                txList = new ReadOnlyMemory<byte>[]
                {
                    CounterTxBuilder.IncrementCounter(Alice, (ulong)(100 * batchNum)),
                    CounterTxBuilder.IncrementCounter(Bob, (ulong)(50 * batchNum)),
                    CounterTxBuilder.EmitMessage(destChainId: 1002, body: new byte[] { (byte)batchNum }),
                };
                // Concatenate tx bytes for the DA payload so an off-chain consumer can
                // re-derive the batch contents from DA without needing the L2 itself.
                var totalLen = 0;
                foreach (var t in txList) totalLen += t.Length;
                daPayload = new byte[totalLen];
                var dst = 0;
                foreach (var t in txList)
                {
                    t.Span.CopyTo(daPayload.AsSpan(dst));
                    dst += t.Length;
                }
            }
            else if (executorMode == "riscv")
            {
                var serializedTransaction = BuildDevnetTransaction(
                    new byte[] { 0x40 },
                    (uint)batchNum);
                txList = new ReadOnlyMemory<byte>[] { serializedTransaction };
                daPayload = serializedTransaction;
            }
            else
            {
                var legacyTx = BitConverter.GetBytes((long)batchNum * 17);
                txList = new ReadOnlyMemory<byte>[] { legacyTx };
                daPayload = legacyTx;
            }
            var ctx = new BatchBlockContext
            {
                L1FinalizedHeight = (uint)(1000 + batchNum),
                FirstBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000),
                LastBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000 + 5_000),
                SequencerCommitteeHash = HashCommittee(initialCommittee),
                Network = 0x4F454E,
            };
            var execReq = new BatchExecutionRequest
            {
                ChainId = LocalChainId,
                BatchNumber = (ulong)batchNum,
                PreStateRoot = preStateRoot,
                Transactions = txList,
                L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
                BlockContext = ctx,
            };
            var execResult = await executor.ApplyBatchAsync(execReq);
            if (executorMode == "counter")
            {
                Console.WriteLine($"  [exec] {txList.Length} Counter txs → gas={execResult.GasConsumed}, txRoot={Truncate(execResult.TxRoot.ToString())}, l2L2Root={Truncate(execResult.L2ToL2MessageRoot.ToString())}");
            }

            // Replace executor's WithdrawalRoot with the processor's view (matches what NeoHub commits to).
            var (wRoot, _) = withdrawalProcessor.SealBatch();
            execResult = execResult with { WithdrawalRoot = wRoot };

            // 4. DA: publish the batch payload — the devnet sends the canonical-encoded
            // commitment as the payload (a real L2 deployment sends the ordered tx blob;
            // the commitment-as-payload is sufficient for the devnet's content-addressing
            // pin). With --data-dir the payload lands in RocksDB; without, it sits in an
            // in-memory store. Either way the resulting DACommitment goes into PublicInputs
            // so the proof binds to *this* DA layer.
            var daReceipt = await daWriter.PublishAsync(new DAPublishRequest
            {
                ChainId = LocalChainId,
                BatchNumber = (ulong)batchNum,
                Payload = daPayload,
            });
            Console.WriteLine($"  [DA]   layer={daReceipt.Layer} commitment={Truncate(daReceipt.Commitment)}");

            // 5. Sign + verify.
            var publicInputs = new PublicInputs
            {
                ChainId = LocalChainId,
                BatchNumber = (ulong)batchNum,
                PreStateRoot = preStateRoot,
                PostStateRoot = execResult.PostStateRoot,
                TxRoot = execResult.TxRoot,
                ReceiptRoot = execResult.ReceiptRoot,
                WithdrawalRoot = execResult.WithdrawalRoot,
                L2ToL1MessageRoot = execResult.L2ToL1MessageRoot,
                L2ToL2MessageRoot = execResult.L2ToL2MessageRoot,
                L1MessageHash = StateRootCalculator.HashL1Messages(execReq.L1MessagesConsumed),
                DACommitment = daReceipt.Commitment,
                BlockContextHash = StateRootCalculator.HashBlockContext(ctx),
            };
            var proofResult = await prover.ProveAsync(new ProofRequest
            {
                PublicInputs = publicInputs,
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = ProofType.Multisig,
            });

            var commitment = new L2BatchCommitment
            {
                ChainId = LocalChainId,
                BatchNumber = (ulong)batchNum,
                FirstBlock = (ulong)(100 * batchNum),
                LastBlock = (ulong)(100 * batchNum + 50),
                PreStateRoot = preStateRoot,
                PostStateRoot = execResult.PostStateRoot,
                TxRoot = execResult.TxRoot,
                ReceiptRoot = execResult.ReceiptRoot,
                WithdrawalRoot = execResult.WithdrawalRoot,
                L2ToL1MessageRoot = execResult.L2ToL1MessageRoot,
                L2ToL2MessageRoot = execResult.L2ToL2MessageRoot,
                DACommitment = daReceipt.Commitment,
                PublicInputHash = proofResult.PublicInputHash,
                ProofType = ProofType.Multisig,
                Proof = proofResult.Proof,
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var verify = await verifierRegistry.VerifyAsync(commitment, publicInputs);
            sw.Stop();
            Console.WriteLine($"  [seal] preRoot={Truncate(preStateRoot)} postRoot={Truncate(commitment.PostStateRoot)} verify={verify.Valid}");
            if (!verify.Valid)
            {
                Console.Error.WriteLine($"  ❌ verification failed: {verify.FailureReason}");
                return 1;
            }

            // Emit metrics for this batch.
            metrics.IncrementCounter(MetricNames.BatchesSealed);
            metrics.RecordHistogram(MetricNames.BatchSealLatencyMs, sw.Elapsed.TotalMilliseconds);
            metrics.SetGauge(MetricNames.BatchTxCount, execReq.Transactions.Count);
            metrics.IncrementCounter(MetricNames.ProofsGenerated, 1, ("kind", commitment.ProofType.ToString()));
            metrics.IncrementCounter(MetricNames.DepositsProcessed);
            metrics.IncrementCounter(MetricNames.WithdrawalsStaged);

            // 6. Finalize in RPC store.
            rpcStore.AddBatch(commitment, BatchStatus.Pending);
            rpcStore.Finalize((ulong)batchNum);
            rpcStore.RecordDeposit(new DepositStatus(0, (ulong)batchNum, ConsumedOnL2: true, IncludedInBatch: (ulong)batchNum));

            allCommitments.Add(commitment);
            publicInputsByBatch[(ulong)batchNum] = publicInputs;
            daReceiptsByBatch[(ulong)batchNum] = daReceipt;

            // Continuity: next batch's pre = this batch's post.
            preStateRoot = commitment.PostStateRoot;
        }

        // ---- Audit pass over the produced sequence ----
        // Skip audit when no batches were produced this run — the operator likely passed
        // 0 to verify rehydration only. The auditor's "no batches supplied" check is
        // intentional for serving real audit requests; firing it on an empty re-run
        // would mask the persistence verification path with a confusing audit failure.
        Console.WriteLine();
        if (allCommitments.Count == 0)
        {
            Console.WriteLine("───── audit pass ─────");
            Console.WriteLine("  (skipped — 0 batches this run; rehydration verification only)");
            Console.WriteLine();
            Console.WriteLine("✅ devnet run complete.");
            return 0;
        }
        Console.WriteLine("───── audit pass ─────");
        var auditor = new ChainAuditor(metrics)
            .Register(new ContinuityCheck())
            .Register(new NoZeroProofCheck())
            .Register(new ProofValidityCheck(verifierRegistry, c => publicInputsByBatch[c.BatchNumber]))
            // Catches intra-batch range inversions + zero batch numbers; cheap and
            // catches a class of buggy-sequencer bugs not covered by ContinuityCheck.
            .Register(new BatchRangeCheck())
            // Catches "DA layer dropped the payload" — relevant only when --data-dir is
            // set, but cheap enough to always run (in-memory writer never drops).
            .Register(new DAAvailabilityCheck(
                daWriter,
                batch => daReceiptsByBatch.TryGetValue(batch.BatchNumber, out var receipt)
                    ? receipt
                    : null));
        var report = await auditor.AuditAsync(allCommitments);
        Console.WriteLine(report.Summarize());
        if (!report.Passed)
        {
            Console.Error.WriteLine("❌ audit failed");
            return 1;
        }

        // ---- Metrics summary ----
        Console.WriteLine();
        Console.WriteLine("───── metrics ─────");
        Console.WriteLine($"  {MetricNames.BatchesSealed,-32} {metrics.GetCounter(MetricNames.BatchesSealed)}");
        Console.WriteLine($"  {MetricNames.DepositsProcessed,-32} {metrics.GetCounter(MetricNames.DepositsProcessed)}");
        Console.WriteLine($"  {MetricNames.WithdrawalsStaged,-32} {metrics.GetCounter(MetricNames.WithdrawalsStaged)}");
        Console.WriteLine($"  {MetricNames.ProofsGenerated,-32} {metrics.GetCounter(MetricNames.ProofsGenerated, ("kind", "Multisig"))} (kind=Multisig)");
        Console.WriteLine($"  {MetricNames.AuditsRun,-32} {metrics.GetCounter(MetricNames.AuditsRun)}");
        var latencies = metrics.GetHistogram(MetricNames.BatchSealLatencyMs);
        if (latencies.Count > 0)
        {
            Console.WriteLine($"  {MetricNames.BatchSealLatencyMs,-32} count={latencies.Count} avg={latencies.Average():F2}ms max={latencies.Max():F2}ms");
        }

        // Same data, rendered in Prometheus exposition format. In production, the L2 node
        // exposes this via an HTTP /metrics endpoint and Prometheus scrapes it directly.
        Console.WriteLine();
        Console.WriteLine("───── /metrics (Prometheus text format) ─────");
        var promText = PrometheusExporter.Format(metrics.Snapshot());
        foreach (var line in promText.Split('\n'))
        {
            if (line.Length > 0) Console.WriteLine("  " + line);
        }

        Console.WriteLine();
        Console.WriteLine("───── post-run RPC snapshot ─────");
        var latest = rpc.GetL2StateRoot(new Json.JArray { LocalChainId });
        Console.WriteLine($"  getl2stateroot:   {latest!.AsString()}");
        Console.WriteLine($"  state entries:    {stateStore.Count}");
        Console.WriteLine($"  committee active: {(await committeeProvider.GetActiveCommitteeAsync()).Count}");

        // doc.md §16.2 5-dimension security label — InMemoryL2RpcStore returns the
        // strongest-default values for dimensions the devnet doesn't explicitly set.
        // Showcasing this here makes the new getsecuritylabel RPC visible to operators
        // following the devnet output as an introduction to the system.
        var label = (Json.JObject)rpc.GetSecurityLabel(new Json.JArray { LocalChainId })!;
        Console.WriteLine($"  getsecuritylabel: securityLevel={label["securityLevelName"]!.AsString()} " +
            $"daMode={label["daModeName"]!.AsString()} sequencer={label["sequencerName"]!.AsString()} " +
            $"exit={label["exitName"]!.AsString()} gateway={label["gatewayEnabled"]!.AsBoolean()}");

        // Show Alice's net position: deposits - withdrawals over N batches.
        var aliceBalance = ReadBalance(stateStore, GasL2, Alice);
        var expected = BigInteger.Zero;
        for (var i = 1; i <= batches; i++) expected += new BigInteger(1_000_000 * i) - new BigInteger(10_000 * i);
        Console.WriteLine($"  alice balance:    {aliceBalance} (expected {expected})");
        if (aliceBalance != expected)
        {
            Console.Error.WriteLine("❌ Alice's balance disagrees with expected; state-store wiring broken.");
            return 1;
        }

        // ---- Live HTTP /metrics demonstration ----
        if (metricsPort.HasValue)
        {
            Console.WriteLine();
            Console.WriteLine("───── live HTTP /metrics ─────");
            var handler = new MetricsRequestHandler(metrics);
            using var server = new MetricsHttpServer(System.Net.IPAddress.Loopback, metricsPort.Value, handler);
            server.Start();
            using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
            var url = $"http://127.0.0.1:{server.Endpoint.Port}/metrics";
            Console.WriteLine($"  scraping: {url}");
            var resp = await http.GetAsync(url);
            Console.WriteLine($"  status:   {(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.WriteLine($"  type:     {resp.Content.Headers.ContentType}");
            var body = await resp.Content.ReadAsStringAsync();
            var lines = body.Split('\n');
            Console.WriteLine($"  body:     {lines.Length} lines, {body.Length} bytes (first 3 below)");
            for (var i = 0; i < Math.Min(3, lines.Length); i++)
                Console.WriteLine($"    | {lines[i]}");
            Console.WriteLine($"  health:   {(int)(await http.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/healthz")).StatusCode}");
            Console.WriteLine($"  ready:    {(int)(await http.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/readyz")).StatusCode}");
        }

        Console.WriteLine();
        Console.WriteLine("✅ devnet run complete.");
        // Explicit dispose for the conditionally-allocated neovm state store.
        // Other stores use `using var` directly; this one is conditional on the
        // --executor flag so it has to be disposed manually here.
        neovmStore?.Dispose();
        return 0;
    }

    // Argument-parsing helpers moved to DevnetArgs.cs for direct unit testability
    // (tests can call DevnetArgs.ParseMetricsPort etc. without subprocess-invoking
    // the binary). Program.Main consumes DevnetArgs directly.

    // LabelOverrides + ReadLabelOverrides + ParseEnumOrDefault moved to
    // DevnetLabelOverrides.cs for direct unit testability.

    private static IL2KeyValueStore? MaybeRocks(string? baseDir, string subDir)
    {
        if (baseDir is null) return null;
        var path = Path.Combine(baseDir, subDir);
        Directory.CreateDirectory(path);
        return new RocksDbKeyValueStore(path);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("neo-l2-devnet — in-process Neo Elastic Network devnet runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  neo-l2-devnet [<batches>] [--metrics-port <port>] [--data-dir <path>] [--config <path>] [--executor <kind>]");
        Console.WriteLine("  neo-l2-devnet --help");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <batches>            Number of batches to seal (default: 3, 0 = rehydrate-only).");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --metrics-port <p>   Stand up a Prometheus /metrics + /healthz + /readyz HTTP server.");
        Console.WriteLine("  --data-dir <path>    Wire RocksDB-backed stores under <path>/{state,rpc-proofs,sequencer,da}.");
        Console.WriteLine("                       Without this, every store is in-memory and data is lost on restart.");
        Console.WriteLine("  --config <path>      Read §16.2 security-label dimensions (securityLevel / daMode /");
        Console.WriteLine("                       sequencerModel / exitModel / gatewayEnabled) from a chain.config.json");
        Console.WriteLine("                       (typically produced by `neo-stack create-chain --template <X>`) so the");
        Console.WriteLine("                       devnet's getsecuritylabel RPC reflects your operator config.");
        Console.WriteLine("  --executor <kind>    Pick the per-tx executor. Default 'reference' is the no-op");
        Console.WriteLine("                       ReferenceTransactionExecutor. 'counter' wires the");
        Console.WriteLine("                       Sample.CounterChainExecutor end-to-end (state mutation via");
        Console.WriteLine("                       KeyedStateStoreAdapter, real receipts/withdrawals/messages");
        Console.WriteLine("                       from CounterTxBuilder-built transactions). 'riscv' /");
        Console.WriteLine("                       'neovm2-riscv' wires the canonical Neo N4 L2 execution");
        Console.WriteLine("                       target through RiscVTransactionExecutor and neo_riscv_host.");
        Console.WriteLine("                       'neovm' wires the legacy compatibility path via");
        Console.WriteLine("                       ApplicationEngineTransactionExecutor against a fresh KV store");
        Console.WriteLine("                       bootstrapped via NeoVMGenesisBootstrap.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  neo-l2-devnet 5                         # 5 batches, in-memory, default Optimistic label");
        Console.WriteLine("  neo-l2-devnet 5 --metrics-port 9090     # 5 batches + live HTTP scrape");
        Console.WriteLine("  neo-l2-devnet 5 --data-dir /tmp/dn1     # 5 batches, persisted to disk");
        Console.WriteLine("  neo-l2-devnet 0 --data-dir /tmp/dn1     # rehydrate state from disk only");
        Console.WriteLine("  neo-l2-devnet 5 --config ./my-l2/chain.config.json");
        Console.WriteLine("                                          # preview an operator-template config end-to-end");
        Console.WriteLine("  neo-l2-devnet 5 --executor counter      # run with the Sample.CounterChainExecutor demo");
        Console.WriteLine();
        Console.WriteLine("See docs/getting-started.md and docs/persistence.md for more.");
    }

    // ---- Helpers ----

    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    private static string Truncate(UInt256 root)
    {
        var s = root.ToString();
        return s.Length <= 18 ? s : s[..10] + "…" + s[^6..];
    }

    private static byte[] BuildDevnetTransaction(byte[] script, uint nonce)
    {
        return new Transaction
        {
            Version = 0,
            Nonce = nonce,
            SystemFee = 0,
            NetworkFee = 0,
            ValidUntilBlock = 100_000,
            Script = script,
            Signers = new[]
            {
                new Signer { Account = Alice, Scopes = WitnessScope.None },
            },
            Attributes = Array.Empty<TransactionAttribute>(),
            Witnesses = new[]
            {
                new Witness
                {
                    InvocationScript = ReadOnlyMemory<byte>.Empty,
                    VerificationScript = ReadOnlyMemory<byte>.Empty,
                },
            },
        }.ToArray();
    }

    private static string Truncate160(UInt160 h)
    {
        var s = h.ToString();
        return s.Length <= 14 ? s : s[..10] + "…" + s[^4..];
    }

    private static UInt256 HashCommittee(IReadOnlyList<CommitteeMember> members)
    {
        if (members.Count == 0) return UInt256.Zero;
        var bytes = new List<byte>();
        foreach (var m in members.OrderBy(x => x.PublicKey)) bytes.AddRange(m.PublicKey.EncodePoint(true));
        return new UInt256(Crypto.Hash256(bytes.ToArray()));
    }

    private static byte[] BalanceKey(UInt160 asset, UInt160 holder)
    {
        var k = new byte[1 + 20 + 20];
        k[0] = 0x01; // domain prefix for "balance"
        asset.GetSpan().CopyTo(k.AsSpan(1, 20));
        holder.GetSpan().CopyTo(k.AsSpan(21, 20));
        return k;
    }

    private static void ApplyMint(KeyedStateStore store, UInt160 asset, UInt160 holder, BigInteger amount)
    {
        var key = BalanceKey(asset, holder);
        var current = ReadBalance(store, asset, holder);
        store.Put(key, (current + amount).ToByteArray(isUnsigned: true, isBigEndian: false));
    }

    private static void ApplyBurn(KeyedStateStore store, UInt160 asset, UInt160 holder, BigInteger amount)
    {
        var key = BalanceKey(asset, holder);
        var current = ReadBalance(store, asset, holder);
        var next = current - amount;
        if (next < 0) throw new InvalidOperationException("insufficient balance");
        store.Put(key, next.ToByteArray(isUnsigned: true, isBigEndian: false));
    }

    private static BigInteger ReadBalance(KeyedStateStore store, UInt160 asset, UInt160 holder)
    {
        var bytes = store.Get(BalanceKey(asset, holder));
        if (bytes.Length == 0) return BigInteger.Zero;
        return new BigInteger(bytes.Span, isUnsigned: true, isBigEndian: false);
    }
}
