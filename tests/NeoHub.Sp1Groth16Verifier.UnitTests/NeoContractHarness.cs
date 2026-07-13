using Neo;
using Neo.Extensions.IO;
using Neo.Extensions.VM;
using Neo.Persistence;
using Neo.Persistence.Providers;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;

namespace NeoHub.Sp1Groth16Verifier.UnitTests;

internal sealed record VmInvocation<T>(VMState State, T? Result, long FeeConsumed, string? Fault);

internal sealed class NeoContractHarness : IDisposable
{
    private const long ExecutionFeeLimit = 100_00000000;
    private const int VerifierContractId = 1;
    private const int RouterContractId = 2;

    private readonly MemoryStore _store = new();
    private readonly StoreCache _snapshot;

    internal NeoContractHarness()
    {
        _snapshot = new StoreCache(_store.GetSnapshot());
        var currentBlock = new Neo.VM.Types.Struct(null) { UInt256.Zero.ToArray(), 0 };
        _snapshot.Add(
            new KeyBuilder(NativeContract.Ledger.Id, 12),
            new StorageItem(BinarySerializer.Serialize(currentBlock, ExecutionEngineLimits.Default)));
        _snapshot.Add(
            new KeyBuilder(NativeContract.Policy.Id, 18),
            new StorageItem(PolicyContract.DefaultExecFeeFactor));
        _snapshot.Add(
            new KeyBuilder(NativeContract.Policy.Id, 19),
            new StorageItem(PolicyContract.DefaultStoragePrice));

        AddContract(NativeContract.CryptoLib.GetContractState(ProtocolSettings.Default, 0));
        ContractHash = AddContract(
            VerifierContractId,
            Sp1Groth16VerifierArtifact.Nef,
            Sp1Groth16VerifierArtifact.ManifestJson);
        RouterHash = AddContract(
            RouterContractId,
            ContractZkVerifierArtifact.Nef,
            ContractZkVerifierArtifact.ManifestJson);
    }

    private UInt160 AddContract(int id, byte[] nefBytes, string manifestJson)
    {
        var nef = NefFile.Parse(nefBytes);
        var manifest = ContractManifest.Parse(manifestJson);
        var hash = Helper.GetContractHash(UInt160.Zero, nef.CheckSum, manifest.Name);
        AddContract(new ContractState
        {
            Id = id,
            UpdateCounter = 0,
            Hash = hash,
            Nef = nef,
            Manifest = manifest,
        });
        return hash;
    }

    private void AddContract(ContractState state)
    {
        _snapshot.Add(
            new KeyBuilder(NativeContract.ContractManagement.Id, 8).Add(state.Hash),
            new StorageItem(state));
        _snapshot.Add(
            new KeyBuilder(NativeContract.ContractManagement.Id, 12).Add(state.Id),
            new StorageItem(state.Hash.ToArray()));
    }

    internal UInt160 ContractHash { get; }
    internal UInt160 RouterHash { get; }

    internal void ConfigureRouterForSp1(byte[] verificationKeyId)
    {
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        if (verificationKeyId.Length != UInt256.Length)
            throw new ArgumentException("SP1 program vkey must be 32 bytes.", nameof(verificationKeyId));

        var verificationKeyStorageKey = new byte[34];
        verificationKeyStorageKey[0] = 0x02;
        verificationKeyStorageKey[1] = 1;
        verificationKeyId.CopyTo(verificationKeyStorageKey, 2);
        PutRouterStorage(verificationKeyStorageKey, [1]);
        PutRouterStorage([0x03, 1], ContractHash.ToArray());
        PutRouterStorage([0x05, 1], [1]);
        PutRouterStorage([0x06, 1], verificationKeyId);
    }

    private void PutRouterStorage(byte[] key, byte[] value) =>
        _snapshot.Add(new StorageKey { Id = RouterContractId, Key = key }, new StorageItem(value));

    internal VmInvocation<bool> InvokeBoolean(string method, params object?[] args) =>
        Invoke(method, static item => item.GetBoolean(), args);

    internal VmInvocation<byte[]> InvokeBytes(string method, params object?[] args) =>
        Invoke(method, static item => item.GetSpan().ToArray(), args);

    internal VmInvocation<bool> InvokeRouterBoolean(string method, params object?[] args) =>
        Invoke(RouterHash, method, static item => item.GetBoolean(), args);

    private VmInvocation<T> Invoke<T>(string method, Func<StackItem, T> readResult, params object?[] args)
        => Invoke(ContractHash, method, readResult, args);

    private VmInvocation<T> Invoke<T>(
        UInt160 contractHash,
        string method,
        Func<StackItem, T> readResult,
        params object?[] args)
    {
        using var script = new ScriptBuilder();
        script.EmitDynamicCall(contractHash, method, args);

        using var engine = ApplicationEngine.Create(
            TriggerType.Application,
            container: null,
            snapshot: _snapshot,
            settings: ProtocolSettings.Default,
            gas: ExecutionFeeLimit);
        engine.LoadScript(script.ToArray());

        var state = engine.Execute();
        var result = state == VMState.HALT && engine.ResultStack.Count == 1
            ? readResult(engine.ResultStack.Pop())
            : default;

        return new VmInvocation<T>(state, result, engine.FeeConsumed, engine.FaultException?.ToString());
    }

    public void Dispose()
    {
        _snapshot.Dispose();
        _store.Dispose();
    }
}
