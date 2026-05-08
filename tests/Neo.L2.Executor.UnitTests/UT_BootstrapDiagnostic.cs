using System;
using System.Linq;
using Neo;
using Neo.L2.Executor;
using Neo.L2.Persistence;
using Neo.SmartContract;

namespace Neo.L2.Executor.UnitTests;

[TestClass]
public class UT_BootstrapDiagnostic
{
    [TestMethod]
    public void RunOnCache_PopulatesAdapterChangeSet()
    {
        using var store = new InMemoryKeyValueStore();
        var cache = new L2DataCacheAdapter(store);
        Assert.AreEqual(0, cache.GetChangeSet().Count(),
            "before bootstrap: change-set is empty");

        NeoVMGenesisBootstrap.RunOnCache(cache, NeoVMGenesisBootstrap.DefaultBootstrapSettings);

        var changes = cache.GetChangeSet().ToArray();
        Assert.IsTrue(changes.Length > 0,
            $"after bootstrap: cache change-set must have entries (got {changes.Length})");
    }

    [TestMethod]
    public void Commit_FlushesAdapterChangeSetToStore()
    {
        using var store = new InMemoryKeyValueStore();
        var cache = new L2DataCacheAdapter(store);
        NeoVMGenesisBootstrap.RunOnCache(cache, NeoVMGenesisBootstrap.DefaultBootstrapSettings);

        cache.Commit();

        Assert.IsTrue(cache.AddCount > 0 || cache.UpdateCount > 0,
            $"Commit must call AddInternal or UpdateInternal (Add={cache.AddCount}, Update={cache.UpdateCount})");
        Assert.IsTrue(store.Count > 0,
            $"Commit must flush writes to the underlying store (count={store.Count})");
    }

    [TestMethod]
    public void Bootstrap_WritesPolicyExecFeeFactorKey()
    {
        // PolicyContract has Id=-7, ExecFeeFactor prefix=0x12. The wire-format key
        // is [4B Id LE = 0xFFFFFFF9][1B 0x12].
        using var store = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(store, NeoVMGenesisBootstrap.DefaultBootstrapSettings);

        var keyBytes = new byte[] { 0xF9, 0xFF, 0xFF, 0xFF, 0x12 };
        var got = store.Get(keyBytes);
        if (got is null)
        {
            // Diagnostic dump
            var keys = store.EnumeratePrefix(default).Take(15)
                .Select(kv => Convert.ToHexString(kv.Key)).ToArray();
            Assert.Fail($"PolicyContract.execFeeFactor key not found. store.Count={store.Count}, " +
                $"first keys: [{string.Join(", ", keys)}]");
        }
    }
}
