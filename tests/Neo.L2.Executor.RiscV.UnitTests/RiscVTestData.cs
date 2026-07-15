using Neo.Extensions.IO;
using Neo.L2.Executor.Receipts;
using Neo.L2.Persistence;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;

namespace Neo.L2.Executor.RiscV.UnitTests;

internal static class RiscVTestData
{
    public static ProtocolSettings Settings { get; } = NeoVMGenesisBootstrap.DefaultBootstrapSettings;

    public static BatchBlockContext Context { get; } = new()
    {
        L1FinalizedHeight = 1,
        FirstBlockTimestamp = 1_700_000_000_000,
        LastBlockTimestamp = 1_700_000_005_000,
        SequencerCommitteeHash = UInt256.Zero,
        Network = 0x4F454E,
    };

    public static InMemoryKeyValueStore CreateStore()
    {
        var store = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(store, Settings);
        return store;
    }

    public static byte[] BuildTransaction(
        byte[] script,
        uint nonce = 1,
        UInt160? signer = null,
        WitnessScope scope = WitnessScope.Global)
    {
        var transaction = new Transaction
        {
            Version = 0,
            Nonce = nonce,
            SystemFee = 0,
            NetworkFee = 0,
            ValidUntilBlock = 100,
            Script = script,
            Signers =
            [
                new Signer
                {
                    Account = signer ?? UInt160.Zero,
                    Scopes = scope,
                },
            ],
            Attributes = System.Array.Empty<TransactionAttribute>(),
            Witnesses =
            [
                new Witness
                {
                    InvocationScript = ReadOnlyMemory<byte>.Empty,
                    VerificationScript = ReadOnlyMemory<byte>.Empty,
                },
            ],
        };
        return transaction.ToArray();
    }

    public static ContractState BuildContract(
        byte[] script,
        params ContractEventDescriptor[] events)
    {
        var nef = new NefFile
        {
            Compiler = "neo-l2-riscv-tests",
            Source = string.Empty,
            Tokens = System.Array.Empty<MethodToken>(),
            Script = script,
            CheckSum = 0,
        };
        nef.CheckSum = NefFile.ComputeChecksum(nef);
        return new ContractState
        {
            Id = 42,
            UpdateCounter = 0,
            Hash = UInt160.Parse("0x1111111111111111111111111111111111111111"),
            Nef = nef,
            Manifest = new ContractManifest
            {
                Name = "StatefulRiscVTest",
                Groups = System.Array.Empty<ContractGroup>(),
                SupportedStandards = System.Array.Empty<string>(),
                Abi = new ContractAbi
                {
                    Methods =
                    [
                        new ContractMethodDescriptor
                        {
                            Name = "main",
                            Parameters = System.Array.Empty<ContractParameterDefinition>(),
                            ReturnType = ContractParameterType.Void,
                            Offset = 0,
                            Safe = false,
                        },
                    ],
                    Events = events,
                },
                Permissions = [ContractPermission.DefaultPermission],
                Trusts = WildcardContainer<ContractPermissionDescriptor>.CreateWildcard(),
                Extra = null,
            },
        };
    }

    public static RiscVExecutionResult Halt(
        RiscVHostExecutionContext context,
        long nativeFeePico = 10_000)
        => new()
        {
            State = RiscVHost.StateHalt,
            FeeConsumedPico = nativeFeePico,
            HostFeeConsumed = context.HostFeeConsumed,
        };

    public static RiscVExecutionResult Fault(
        RiscVHostExecutionContext context,
        long nativeFeePico = 10_000,
        string error = "fault")
        => new()
        {
            State = RiscVHost.StateFault,
            FeeConsumedPico = nativeFeePico,
            HostFeeConsumed = context.HostFeeConsumed,
            ErrorMessage = error,
        };

    public static void AssertReceiptEffectsAreSameSource(TransactionExecutionResult result)
    {
        Assert.AreEqual(result.Effects.StorageHash, result.Receipt.StorageDeltaHash);
        Assert.AreEqual(result.Effects.EventsHash, result.Receipt.EventsHash);
        Assert.AreEqual(Receipt.ReceiptHashSize, result.Receipt.EncodeHashData().Length);
    }
}
