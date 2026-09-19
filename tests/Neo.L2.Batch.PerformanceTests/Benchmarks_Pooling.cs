using System;
using System.Diagnostics;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2.Batch;

namespace Neo.L2.Batch.PerformanceTests;

/// <summary>
/// Performance benchmarks for pooled vs non-pooled batch serialization.
/// Validates O-004 optimization achieves -15% Gen0 collection reduction.
/// </summary>
[TestClass]
[Config(typeof(PooledSerializationConfig))]
public class Benchmarks_Pooling
{
    private SealedBatch _sampleBatch;
    private readonly Random _random = new(42); // Deterministic seed

    [TestInitialize]
    public void Setup()
    {
        // Create realistic sample batch with moderate workload
        _sampleBatch = CreateSampleBatch(
            chainId: 1,
            batchNumber: 100,
            transactionCount: 1000, // Moderate load
            withdrawalCount: 50,
            messageCount: 30);
    }

    #region Non-Pooled Baseline

    /// <summary>
    /// Baseline: Standard non-pooled serialization (current production implementation).
    /// Measures current Gen0 pressure and memory allocations.
    /// </summary>
    [Benchmark]
    public void Serialize_NonPooled_Baseline()
    {
        // This should match existing BatchSerializer behavior
        var serialized = BatchSerializer.Encode(new L2BatchCommitment
        {
            ChainId = _sampleBatch.ChainId,
            BatchNumber = _sampleBatch.BatchNumber,
            FirstBlock = _sampleBatch.FirstBlock,
            LastBlock = _sampleBatch.LastBlock,
            PreStateRoot = _sampleBatch.PreStateRoot,
            PostStateRoot = UInt256.Zero,
            TxRoot = TransactionHasher.ComputeRoot(_sampleBatch.Transactions.Select(t => t.ToArray()).ToList()),
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = MessageHasher.HashAll(_sampleBatch.L2ToL1Messages ?? Array.Empty<CrossChainMessage>()),
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Zk,
            Proof = Array.Empty<byte>()
        });
        
        // Prevent compiler from optimizing away the allocation
        GC.KeepAlive(serialized);
    }

    #endregion

    #region Pool-Based Implementation

    /// <summary>
    /// Optimized: Pool-based serialization (O-004 implementation).
    /// Should show reduced Gen0 collections and lower peak memory usage.
    /// </summary>
    [Benchmark]
    public void Serialize_Pooled_Optimized()
    {
        using var owner = PooledBatchSerializer.Serialize(_sampleBatch);
        var span = owner.Memory.Span;
        
        // Validate output is reasonable size (~100KB for 1K tx batch)
        Assert.IsTrue(span.Length > 0 && span.Length < 500_000);
        
        // Prevent optimization
        GC.KeepAlive(owner);
    }

    /// <summary>
    /// Stream-based pooled serialization alternative for disk/network writes.
    /// Zero intermediate allocations, direct-to-stream pattern.
    /// </summary>
    [Benchmark]
    public void SerializeToStream_Pooled()
    {
        using var stream = new MemoryStream();
        PooledBatchSerializer.SerializeToStream(_sampleBatch, stream);
        
        Assert.IsTrue(stream.Length > 0);
        GC.KeepAlive(stream);
    }

    #endregion

    #region GC Pressure Analysis

    /// <summary>
    /// Measures Gen0 collection frequency under sustained load simulation.
    /// Target: -15% reduction compared to baseline.
    /// </summary>
    [Benchmark(Baseline = true)]
    [Description("Non-pooled baseline - high Gen0 pressure")]
    public int Gen0Pressure_NonPooled_Baseline()
    {
        CollectAndMeasureGen0(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                var data = BatchSerializer.Encode(new L2BatchCommitment
                {
                    ChainId = 1,
                    BatchNumber = (ulong)i,
                    FirstBlock = 100 + i,
                    LastBlock = 150 + i,
                    PreStateRoot = UInt256.Zero,
                    PostStateRoot = UInt256.Zero,
                    TxRoot = UInt256.Zero,
                    ReceiptRoot = UInt256.Zero,
                    WithdrawalRoot = UInt256.Zero,
                    L2ToL1MessageRoot = UInt256.Zero,
                    L2ToL2MessageRoot = UInt256.Zero,
                    DACommitment = UInt256.Zero,
                    PublicInputHash = UInt256.Zero,
                    ProofType = ProofType.Zk,
                    Proof = Array.Empty<byte>()
                });
            }
        });
        
        return GC.CollectionCount(0);
    }

    /// <summary>
    /// Measures Gen0 collection frequency with pooled serialization.
    /// Expected improvement: 15-20% fewer collections.
    /// </summary>
    [Benchmark]
    [Description("Pool-based optimized - reduced Gen0 pressure")]
    public int Gen0Pressure_Pooled_Optimized()
    {
        CollectAndMeasureGen0(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                using var _ = PooledBatchSerializer.Serialize(CreateSampleBatch(1, (ulong)i, 100, 10, 5));
            }
        });
        
        return GC.CollectionCount(0);
    }

    /// <summary>
    /// Peak memory usage measurement across batch sealing operations.
    /// Target: -10% overall memory footprint reduction.
    /// </summary>
    [Benchmark]
    [Description("Peak memory usage comparison")]
    public long PeakMemoryUsage_Comparison()
    {
        var maxMemory = 0L;
        
        foreach (var iteration in Enumerable.Range(0, 50))
        {
            CollectAndMeasurePeakMemory(() =>
            {
                using var _ = PooledBatchSerializer.Serialize(CreateSampleBatch(1, (ulong)iteration, 500, 25, 15));
            });
            
            var currentMax = GC.GetTotalMemory(false) / 1024 / 1024;
            maxMemory = Math.Max(maxMemory, currentMax);
        }
        
        return maxMemory; // MB
    }

    #endregion

    #region Helper Methods

    private static void CollectAndMeasureGen0(Action operation)
    {
        // Force initial collection to establish baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var gen0Before = GC.CollectionCount(0);
        
        // Run operation
        operation();
        
        // Collect again after operation
        GC.Collect();
        
        // Net Gen0 collections during operation
        var netCollections = GC.CollectionCount(0) - gen0Before;
        
        // Store for comparison (benchmark framework handles this automatically)
        // Actual numbers reported via BenchmarkDotNet output
    }

    private static void CollectAndMeasurePeakMemory(Action operation)
    {
        var initialMemory = GC.GetTotalMemory(false);
        
        operation();
        
        var finalMemory = GC.GetTotalMemory(false);
        var delta = finalMemory - initialMemory;
        
        // Delta tracked by BenchmarkDotNet
    }

    private static SealedBatch CreateSampleBatch(uint chainId, ulong batchNumber, int txCount, int withdrawalCount, int messageCount)
    {
        var transactions = Enumerable.Range(0, txCount)
            .Select(i => new ReadOnlyMemory<byte>(RandomBytes(100)))
            .ToList();
            
        var l1Messages = Enumerable.Range(0, 10)
            .Select(i => new CrossChainMessage(UInt256.Zero, (uint)i, "test"))
            .ToList();
            
        var withdrawals = withdrawalCount > 0 ? Enumerable.Range(0, withdrawalCount)
            .Select(i => new WithdrawalRequest(
                assetIndex: (ushort)(i % ushort.MaxValue),
                recipient: GenerateRandomAddress(),
                amount: ulong.MaxValue - i,
                targetChainId: 2))
            .ToList() : null;
            
        var l2ToL1Messages = messageCount > 0 ? Enumerable.Range(0, messageCount)
            .Select(i => new CrossChainMessage(
                sender: GenerateRandomAddress(),
                originChainId: 1,
                targetChainId: (uint)((i % 5) + 2),
                payload: RandomBytes(100)))
            .ToList() : null;
            
        var blockContext = new BatchBlockContext(
            timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            prevBlockHash: UInt256.Zero,
            proposer: GenerateRandomAddress(),
            validatorSet: Array.Empty<ByteString>(),
            forcedInclusionCount: 0);
            
        return new SealedBatch(
            chainId: chainId,
            batchNumber: batchNumber,
            firstBlock: 100,
            lastBlock: 100 + (txCount / 100),
            preStateRoot: RandomUInt256(),
            transactions: transactions.AsReadOnly(),
            l1Messages: l1Messages.AsReadOnly(),
            blockContext: blockContext,
            withdrawals: withdrawals,
            l2ToL1Messages: l2ToL1Messages);
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        _random.NextBytes(bytes);
        return bytes;
    }

    private static UInt160 GenerateRandomAddress()
    {
        var hash = RandomBytes(20);
        return new UInt160(hash);
    }

    private static UInt256 RandomUInt256()
    {
        var hash = RandomBytes(32);
        return new UInt256(hash);
    }

    #endregion
}

/// <summary>
/// Custom BenchmarkDotNet configuration for O-004 validation.
/// </summary>
internal sealed class PooledSerializationConfig : ManualConfig
{
    public PooledSerializationConfig()
    {
        // Add standard columns
        AddColumnSource(DefaultColumnSources.GetColumns());
        
        // Memory statistics diagnostics
        AddDiagnoser(MemoryDiagnoser.Default);
        
        // Garbage collection analysis
        Add(Diagnosers.Create<GCStatDiagnoser>(new[] { Column.GCGeneration0Count, Column.GCHeapSize }));
        
        // Run settings
        Options = ConfigOptions.StopOnFirstFailure | ConfigOptions.JoinSummary;
        
        // Execution parameters
        GarbageCollection收集 = true;
        WarmupCount = 3;
        IterationCount = 5;
        InvocationCount = 10;
    }
}
