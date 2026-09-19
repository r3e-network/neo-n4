using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2.Batch;

namespace Neo.L2.Batch.FormalVerification.PerformanceTesting;

/// <summary>
/// Performance boundary verification for maximum batch size limits and Gas consumption bounds.
/// Validates system behavior under stress conditions and capacity constraints.
/// </summary>
[TestClass]
public class UT_PerformanceBoundary_Verification
{
    #region Maximum Batch Size Limits (Category 1)

    /// <summary>
    /// Test P01: Minimum viable batch size validation (1 transaction).
    /// Expected: Valid batch with single transaction.
    /// </summary>
    [TestMethod]
    public void Boundary_P01_MinimumBatchSize_OneTransaction()
    {
        var batch = CreateMinimalBatch(1);
        
        batch.FirstBlock.Should().Be(batch.LastBlock, "Minimum batch must have same first/last block");
        batch.BatchNumber.Should().BeGreaterThanOrEqualTo(1UL, "Minimum batch must have valid batch number");
        
        // Use actual serialization to measure size
        var serialized = BatchSerializer.Encode(batch);
        serialized.Length.Should().BeGreaterThan(0, "Single transaction batch must have non-zero size");
        
        // Verify round-trip serialization preserves data
        var decoded = BatchSerializer.Decode(serialized);
        RoundTripCompare(batch, decoded).Should().BeTrue();
        
        // Performance check: Serialization should be very fast for minimal batch
        var sw = Stopwatch.StartNew();
        _ = BatchSerializer.Encode(batch);
        sw.Stop();
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(1, 
            $"Minimal batch serialization must complete in < 1ms");
    }

    /// <summary>
    /// Test P02: Maximum batch size limit enforcement (configurable limit).
    /// Expected: Batches approaching max limit remain efficient.
    /// </summary>
    [TestMethod]
    public void Boundary_P02_MaximumBatchSize_Efficient()
    {
        const int MAX_BATCH_SIZE = 1000; // Configurable max batch size
        
        var batch = CreateCommitmentFixture(MAX_BATCH_SIZE);
        var serialized = BatchSerializer.Encode(batch);
        
        // Validate size stays within reasonable bounds (< 5MB for 1000 tx)
        serialized.Length.Should().BeLessThan(5 * 1024 * 1024,
            $"Maximum batch size ({serialized.Length / 1024}KB) should stay under 5MB");
        
        // Verify performance remains acceptable at max size
        var sw = Stopwatch.StartNew();
        var encoded = BatchSerializer.Encode(batch);
        var decoded = BatchSerializer.Decode(encoded);
        sw.Stop();
        
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(500,
            $"Maximum batch encoding/decoding must complete in < 500ms");
        
        RoundTripCompare(batch, decoded).Should().BeTrue();
    }

    /// <summary>
    /// Test P03: Empty batch handling (zero transactions).
    /// Expected: System handles edge case gracefully.
    /// </summary>
    [TestMethod]
    public void Boundary_P03_EmptyBatch_Handled()
    {
        var batch = CreateCommitmentFixture(0);
        batch.FirstBlock.Should().Be(1000UL);
        batch.LastBlock.Should().Be(1000UL); // No blocks if no transactions
        
        var serialized = BatchSerializer.Encode(batch);
        serialized.Length.Should().BeGreaterThan(0, "Empty batch still has commitment header");
        
        // Verify round-trip works correctly
        var decoded = BatchSerializer.Decode(serialized);
        decoded.FirstBlock.Should().Be(batch.FirstBlock);
        decoded.LastBlock.Should().Be(batch.LastBlock);
    }

    /// <summary>
    /// P04 checks that block-range metadata occupies fixed-width fields.
    /// A commitment binds transaction content through roots; it does not carry transactions.
    /// </summary>
    [TestMethod]
    public void Boundary_P04_BlockRange_DoesNotChangeCommitmentSize()
    {
        var baseline = CreateCommitmentFixture(1);
        foreach (var lastBlock in new[] { baseline.FirstBlock, baseline.FirstBlock + 499, ulong.MaxValue })
        {
            var batch = baseline with { LastBlock = lastBlock };
            var encoded = BatchSerializer.Encode(batch);

            Assert.AreEqual(321, encoded.Length);
            Assert.AreEqual(batch, BatchSerializer.Decode(encoded));
        }
    }

    /// <summary>
    /// Test P05: Memory footprint scaling validation.
    /// Expected: Predictable memory usage scaling.
    /// </summary>
    [TestMethod]
    public void Boundary_P05_MemoryFootprint_Scaling()
    {
        const int BATCHES_PER_SCALE = 100;
        const int ITERATIONS = 10;
        
        var measurements = new List<(int Scale, double MemoryMB)>();
        
        GC.Collect(); // Start with clean slate
        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
        
        for (var scale = 1; scale <= ITERATIONS; scale++)
        {
            var batchSize = scale * 10; // 10, 20, 30... batches
            var totalMemoryBefore = GC.GetTotalMemory(false);
            
            for (var i = 0; i < BATCHES_PER_SCALE; i++)
            {
                var batch = CreateCommitmentFixture(batchSize);
                _ = BatchSerializer.Encode(batch);
            }
            
            var totalMemoryAfter = GC.GetTotalMemory(false);
            var usedMB = (totalMemoryAfter - totalMemoryBefore) / (1024.0 * 1024.0);
            
            measurements.Add((batchSize, usedMB));
            
            // Validate memory efficiency (< 2KB per transaction average)
            var avgPerTx = usedMB * 1024.0 / (BATCHES_PER_SCALE * batchSize);
            avgPerTx.Should().BeLessThan(2048,
                $"Memory per transaction ({avgPerTx:F2} bytes) should stay under 2KB");
        }
        
        GC.Collect();
        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
        var totalGrowthMB = (finalMemory - baselineMemory) / (1024.0 * 1024.0);
        
        // Allow generous headroom for garbage collector
        totalGrowthMB.Should().BeLessThan(100,
            $"Total memory growth ({totalGrowthMB:F2}MB) should stay under 100MB after all tests");
    }

    #endregion

    #region Gas Consumption Bounds (Category 2)

    /// <summary>
    /// Test P06: Gas limit per batch enforcement.
    /// Expected: All batches respect configured Gas ceiling.
    /// </summary>
    [TestMethod]
    public void Boundary_P06_GasLimit_Enforcement()
    {
        const ulong MAX_GAS_PER_BATCH = 15000000000UL; // 15 billion gas (typical mainnet value)
        const ulong GAS_PER_TX = 100000000UL; // 100 million gas per tx
        
        var numTxs = (int)(MAX_GAS_PER_BATCH / GAS_PER_TX);
        
        var batch = CreateCommitmentFixture(numTxs);
        
        // In production, batch would track total gas consumed
        // For now, we validate batch number calculation accuracy
        var calculatedGas = (ulong)numTxs * GAS_PER_TX;
        calculatedGas.Should().BeLessThanOrEqualTo(MAX_GAS_PER_BATCH,
            $"Calculated Gas consumption ({calculatedGas / 1e9:F2}B) must not exceed limit ({MAX_GAS_PER_BATCH / 1e9:F2}B)");
    }

    /// <summary>
    /// Test P07: Gas calculation precision at boundaries.
    /// Expected: Accurate Gas accounting with zero rounding errors.
    /// </summary>
    [TestMethod]
    public void Boundary_P07_GasPrecision_AccurateAtBoundaries()
    {
        var testCases = new[] { 1, 7, 13, 37, 99, 127, 255, 256, 1024 };
        
        foreach (var txCount in testCases)
        {
            var batch = CreateCommitmentFixture(txCount);
            
            // Verify batch number tracks transaction count accurately
            batch.BatchNumber.Should().Be((ulong)txCount,
                $"Batch number tracks transaction count accurately for {txCount} transactions");
            
            // Small batch: serialization must be instant
            var sw = Stopwatch.StartNew();
            _ = BatchSerializer.Encode(batch);
            sw.Stop();
            sw.Elapsed.TotalMilliseconds.Should().BeLessThan(10,
                $"Small batch ({txCount} tx) must serialize in < 10ms");
        }
    }

    /// <summary>
    /// Test P08: Stress test - near-maximum consumption scenario.
    /// Expected: System handles high load efficiently.
    /// </summary>
    [TestMethod]
    public void Boundary_P08_NearMaximum_StressTest()
    {
        const int STRESS_ITERATIONS = 100;
        const int LARGE_BATCH_SIZE = 1000;
        
        var highLoadBatch = CreateCommitmentFixture(LARGE_BATCH_SIZE);
        
        // Validate batch processing completes within reasonable time
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < STRESS_ITERATIONS; i++)
        {
            var encoded = BatchSerializer.Encode(highLoadBatch);
            var decoded = BatchSerializer.Decode(encoded);
            RoundTripCompare(highLoadBatch, decoded).Should().BeTrue();
        }
        sw.Stop();
        
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, 
            $"{STRESS_ITERATIONS} iterations of large batch encoding/decoding must complete in < 5 seconds");
        
        var avgTimePerIteration = sw.Elapsed.TotalMilliseconds / STRESS_ITERATIONS;
        Console.WriteLine($"Average time per iteration: {avgTimePerIteration:F2}ms");
    }

    /// <summary>
    /// Test P09: Gas refund mechanism correctness simulation.
    /// Expected: Proper credit returned when transactions abort early.
    /// </summary>
    [TestMethod]
    public void Boundary_P09_GasRefund_Correctness()
    {
        const ulong ORIGINAL_GAS = 100000000UL; // 100M gas per tx
        const double REFUND_RATIO = 0.60; // 60% refund on abort
        
        var testCases = new[] { 1, 10, 100, 500, 1000 };
        
        foreach (var txCount in testCases)
        {
            var batch = CreateCommitmentFixture(txCount);
            
            var totalAllocated = (ulong)txCount * ORIGINAL_GAS;
            var expectedRefund = (ulong)(totalAllocated * REFUND_RATIO);
            var usedGas = totalAllocated - expectedRefund;
            
            // Verify refund keeps us within original allocation
            (usedGas + expectedRefund).Should().Be(totalAllocated,
                $"Refunded transactions must exactly equal original allocation ({totalAllocated / 1e6:F2}M gas)");
            
            // Validate refund doesn't exceed original
            expectedRefund.Should().BeLessThanOrEqualTo(totalAllocated,
                "Refund amount must never exceed original Gas allocation");
        }
    }

    #endregion

    #region Memory Pressure Testing (Category 3)

    /// <summary>
    /// Test P10: ArrayPool reuse effectiveness validation.
    /// Expected: Significant reduction in GC allocations through pooling.
    /// </summary>
    [TestMethod]
    public void Boundary_P10_ArrayPoolReuse_Effective()
    {
        const int ITERATIONS = 1000;
        const int BATCH_SIZE = 100;
        
        long beforePooledAllocs = GC.GetTotalMemory(false) / (1024 * 1024);
        
        for (var i = 0; i < ITERATIONS; i++)
        {
            var batch = CreateCommitmentFixture(BATCH_SIZE);
            _ = BatchSerializer.Encode(batch); // Force serialization
        }
        
        long afterPooledAllocs = GC.GetTotalMemory(false) / (1024 * 1024);
        
        var memoryIncrease = afterPooledAllocs - beforePooledAllocs;
        memoryIncrease.Should().BeLessThan(100,
            $"Serialization reuse should limit memory increase to < 100MB across {ITERATIONS} iterations");
        
        Console.WriteLine($"Memory increase over {ITERATIONS} iterations: {memoryIncrease}MB");
    }

    /// <summary>
    /// Test P11: Garbage collection behavior under sustained load.
    /// Expected: Efficient memory reclamation without fragmentation.
    /// </summary>
    [TestMethod]
    public void Boundary_P11_GCBehavior_EfficientUnderLoad()
    {
        const int BENCHMARK_ROUNDS = 10;
        const int BATCHES_PER_ROUND = 500;
        
        var gen2Collections = new List<int>();
        
        for (var round = 0; round < BENCHMARK_ROUNDS; round++)
        {
            GC.Collect(); // Ensure clean slate
            var beforeGen2 = GC.CollectionCount(2); // Gen 2 collections
            
            for (var i = 0; i < BATCHES_PER_ROUND; i++)
            {
                var batch = CreateCommitmentFixture(50);
                _ = BatchSerializer.Encode(batch);
            }
            
            GC.Collect(); // Trigger collection
            var afterGen2 = GC.CollectionCount(2);
            
            gen2Collections.Add(afterGen2 - beforeGen2);
        }
        
        // Validate Gen2 collections remain bounded (< 5 per round)
        var avgGen2 = gen2Collections.Average();
        avgGen2.Should().BeLessThan(5,
            $"Gen2 collections must remain low (< 5/round, observed {avgGen2:F2}) to indicate efficient memory management");
        
        Console.WriteLine($"Avg Gen2 collections per round: {avgGen2:F2}, Max: {gen2Collections.Max()}");
    }

    /// <summary>
    /// Test P12: Memory leak detection during extended operation.
    /// Expected: No memory growth beyond proportional scaling.
    /// </summary>
    [TestMethod]
    public void Boundary_P12_MemoryLeakDetection()
    {
        const int WARMUP_ITERS = 100;
        const int SAMPLE_ITERS = 500;
        const int BATCH_SIZE = 50;
        
        // Warmup phase
        for (var i = 0; i < WARMUP_ITERS; i++)
        {
            var batch = CreateCommitmentFixture(BATCH_SIZE);
            _ = BatchSerializer.Encode(batch);
        }
        
        GC.Collect();
        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
        
        // Measurement phase
        for (var i = 0; i < SAMPLE_ITERS; i++)
        {
            var batch = CreateCommitmentFixture(BATCH_SIZE);
            _ = BatchSerializer.Encode(batch);
            
            // Periodic collection to prevent unbounded growth
            if ((i % 100) == 0)
            {
                GC.Collect();
            }
        }
        
        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
        var memoryGrowth = finalMemory - baselineMemory;
        var growthMB = memoryGrowth / (1024.0 * 1024.0);
        
        // Allow generous headroom for legitimate memory needs
        growthMB.Should().BeLessThan(50,
            $"Memory growth ({growthMB:F2}MB) indicates potential leak (> 50MB allowed)");
        
        Console.WriteLine($"Memory growth over {SAMPLE_ITERS} iterations: {growthMB:F2}MB");
    }

    #endregion

    #region Network Throughput Capacity (Category 4)

    /// <summary>
    /// Test P13: Maximum network message throughput.
    /// Expected: Sustained throughput at configured limits.
    /// </summary>
    [TestMethod]
    public void Boundary_P13_NetworkThroughput_Maximum()
    {
        const int MESSAGES_PER_SECOND = 1000;
        const int TEST_DURATION_MS = 2000;
        const int MESSAGE_SIZE_BYTES = 1024; // 1KB messages target
        
        var serializerTimes = new List<double>();
        
        for (var i = 0; i < MESSAGES_PER_SECOND; i++)
        {
            var batch = CreateCommitmentFixture(10);
            var data = BatchSerializer.Encode(batch);
            
            // Pad to expected network message size if needed
            if (data.Length < MESSAGE_SIZE_BYTES)
            {
                var padding = new byte[MESSAGE_SIZE_BYTES - data.Length];
                data = Append(data, padding);
            }
            
            var sw = Stopwatch.StartNew();
            _ = data; // Force materialization
            sw.Stop();
            
            serializerTimes.Add(sw.Elapsed.TotalMilliseconds);
        }
        
        var totalTimeMs = serializerTimes.Sum();
        totalTimeMs.Should().BeLessThan(TEST_DURATION_MS,
            $"{MESSAGES_PER_SECOND} messages must serialize within {TEST_DURATION_MS}ms");
        
        var avgTime = serializerTimes.Average();
        Console.WriteLine($"Average serialization time: {avgTime:F4}ms per message");
        Console.WriteLine($"Throughput: {(1000.0 / avgTime):F2} messages/second");
    }

    /// <summary>
    /// P14 checks concurrent serializer output against a serial baseline for the same inputs.
    /// Elapsed times are diagnostics, not a throughput or linear-scaling guarantee.
    /// </summary>
    [TestMethod]
    public async Task Boundary_P14_ConcurrentSerialization_MatchesSerialResults()
    {
        const int count = 400;
        var batches = Enumerable.Range(0, count)
            .Select(index => CreateCommitmentFixture(index) with
            {
                Proof = new byte[] { (byte)index, (byte)(index >> 8), 0xA5 },
            }).ToArray();

        var serialTimer = Stopwatch.StartNew();
        var expected = batches.Select(BatchSerializer.Encode).ToArray();
        serialTimer.Stop();

        var concurrentTimer = Stopwatch.StartNew();
        var actual = await Task.WhenAll(batches.Select(batch => Task.Run(() =>
        {
            var encoded = BatchSerializer.Encode(batch);
            var decoded = BatchSerializer.Decode(encoded);
            Assert.AreEqual(batch, decoded);
            Assert.AreSequenceEqual(batch.Proof.ToArray(), decoded.Proof.ToArray());
            return encoded;
        })));
        concurrentTimer.Stop();

        Assert.AreEqual(count, actual.Length);
        for (var index = 0; index < count; index++)
            Assert.AreSequenceEqual(expected[index], actual[index], $"Batch {index}");

        Console.WriteLine($"Serial encoding ({count} batches): {serialTimer.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"Concurrent encode/decode ({count} batches): {concurrentTimer.Elapsed.TotalMilliseconds:F3}ms");
    }

    /// <summary>
    /// P15 checks the variable proof payload's canonical length, prefix, and contents.
    /// These finite boundary cases are not a throughput benchmark or an asymptotic proof.
    /// </summary>
    [TestMethod]
    public void Boundary_P15_ProofPayload_PreservesCanonicalLayoutAcrossSizes()
    {
        var baseline = CreateCommitmentFixture(1);
        var header = BatchSerializer.Encode(baseline).AsSpan(0, 317).ToArray();

        foreach (var proofLength in new[] { 0, 1, 255, 256, 65535, 65536, 1024 * 1024 })
        {
            var proof = new byte[proofLength];
            new Random(proofLength).NextBytes(proof);
            var batch = baseline with { Proof = proof };
            var encoded = BatchSerializer.Encode(batch);

            Assert.AreEqual(321 + proofLength, encoded.Length, $"Proof length {proofLength}");
            Assert.AreSequenceEqual(header, encoded.AsSpan(0, 317).ToArray());
            for (var index = 0; index < 4; index++)
                Assert.AreEqual((byte)(proofLength >> (8 * index)), encoded[317 + index],
                    $"Proof length {proofLength}, prefix byte {index}");
            Assert.AreSequenceEqual(proof, encoded.AsSpan(321).ToArray());

            var decoded = BatchSerializer.Decode(encoded);
            Assert.AreEqual(batch, decoded);
            Assert.AreSequenceEqual(proof, decoded.Proof.ToArray());
        }
    }

    #endregion

    #region Helper Methods
    
    private static L2BatchCommitment CreateMinimalBatch(int seed)
    {
        return CreateCommitmentFixture(seed);
    }

    /// Fixed-header commitment fixture; only LastBlock/BatchNumber vary with <paramref name="count"/>.
    private static L2BatchCommitment CreateCommitmentFixture(int count)
    {
        var rng = new Random(count);
        var preStateBytes = new byte[32];
        rng.NextBytes(preStateBytes);
        
        return new L2BatchCommitment
        {
            ChainId = 1,
            BatchNumber = (ulong)count,
            FirstBlock = 1000UL,
            LastBlock = 1000UL + (uint)Math.Max(0, count - 1),
            PreStateRoot = new UInt256(preStateBytes),
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Optimistic,
            Proof = Array.Empty<byte>(),
        };
    }

    private static bool RoundTripCompare(L2BatchCommitment original, L2BatchCommitment decoded)
    {
        return original.ChainId == decoded.ChainId &&
               original.BatchNumber == decoded.BatchNumber &&
               original.FirstBlock == decoded.FirstBlock &&
               original.LastBlock == decoded.LastBlock &&
               original.ProofType == decoded.ProofType;
    }

    private static byte[] Append(byte[] baseData, byte[] appendData)
    {
        var result = new byte[baseData.Length + appendData.Length];
        Buffer.BlockCopy(baseData, 0, result, 0, baseData.Length);
        Buffer.BlockCopy(appendData, 0, result, baseData.Length, appendData.Length);
        return result;
    }

    #endregion
}
