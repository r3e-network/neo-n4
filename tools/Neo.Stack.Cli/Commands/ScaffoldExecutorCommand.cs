using System;
using System.IO;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>scaffold-executor</c> — generate a starter custom <c>ITransactionExecutor</c> project
/// (csproj + executor skeleton + state seam + tx builder + state-store adapter + README)
/// so an operator goes from "I want a custom L2" to a buildable project in one command.
/// </summary>
/// <remarks>
/// <para>
/// Output mirrors the shape of <c>samples/executors/Sample.CounterChainExecutor</c> — a
/// runnable reference for the framework's <c>Neo.L2.Executor.ITransactionExecutor</c>
/// seam. Defaults the output path to <c>./samples/executors/&lt;Name&gt;Executor</c> so the
/// emitted relative <c>&lt;ProjectReference&gt;</c> entries
/// (<c>..\..\..\src\Neo.L2.*</c>) build out of the box from the neo-n4 monorepo root.
/// </para>
/// <para>
/// Usage:
/// </para>
/// <code>
/// neo-stack scaffold-executor --name MyChain [--chain-id 1234] [--output &lt;dir&gt;]
/// </code>
/// <para>
/// The placeholder executor handles a single opcode (NoOp = <c>0x01</c>) so the scaffold
/// builds + tests cleanly with no edits — the operator removes the placeholder once they
/// add their first real opcode.
/// </para>
/// </remarks>
internal static class ScaffoldExecutorCommand
{
    /// <summary>Run the scaffold-executor subcommand.</summary>
    public static int Run(string[] args)
    {
        var name = ArgUtil.Get(args, "--name", "MyChain");
        if (!IsValidIdentifier(name))
        {
            Console.Error.WriteLine(
                $"--name must be a valid C# identifier (letters, digits, underscores; cannot start with a digit), got '{name}'");
            return 1;
        }
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var chainId))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        // Same validator the rest of the CLI uses — chainId 0 is the L1 sentinel; reject early.
        chainId = Neo.L2.ChainIdValidator.ValidateL2(chainId, "--chain-id");

        var pathFlag = ArgUtil.Get(args, "--path", "");
        var output = pathFlag.Length > 0
            ? pathFlag
            : ArgUtil.Get(args, "--output", $"./samples/executors/{name}Executor");

        var projectName = $"{name}Executor";
        var rootNs = projectName;

        if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length > 0)
        {
            Console.Error.WriteLine(
                $"--output directory '{output}' is not empty. Refusing to overwrite. " +
                "Pass --output <empty-dir> or remove the existing files first.");
            return 1;
        }
        Directory.CreateDirectory(output);

        WriteCsproj(output, projectName, rootNs);
        WriteExecutor(output, projectName, rootNs);
        WriteStateSeam(output, projectName, rootNs);
        WriteTxBuilder(output, projectName, rootNs);
        WriteAdapter(output, projectName, rootNs);
        WriteReadme(output, projectName, rootNs, chainId);

        Console.WriteLine($"Scaffolded {projectName} at {output}/");
        Console.WriteLine($"  csproj         = {output}/{projectName}.csproj");
        Console.WriteLine($"  executor       = {output}/{projectName}.cs");
        Console.WriteLine($"  state seam     = {output}/I{name}State.cs");
        Console.WriteLine($"  tx builder     = {output}/{name}TxBuilder.cs");
        Console.WriteLine($"  state adapter  = {output}/{name}KeyedStateStoreAdapter.cs");
        Console.WriteLine($"  README         = {output}/README.md");
        Console.WriteLine($"  default chainId= {chainId}");
        Console.WriteLine();
        Console.WriteLine($"Build: dotnet build {output}/{projectName}.csproj /p:NuGetAudit=false");
        Console.WriteLine($"Next:  edit {projectName}.cs to replace the NoOp opcode with your chain's logic.");
        Console.WriteLine($"       see {output}/README.md for the 5-step customization checklist.");
        return 0;
    }

    /// <summary>True if <paramref name="s"/> is a syntactically-valid C# identifier
    /// (used as namespace + class names, so it must compile).</summary>
    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        for (var i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }

    private static void WriteCsproj(string output, string projectName, string rootNs)
    {
        File.WriteAllText(Path.Combine(output, $"{projectName}.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <RootNamespace>{{rootNs}}</RootNamespace>
                <AssemblyName>{{projectName}}</AssemblyName>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="..\..\..\src\Neo.L2.Abstractions\Neo.L2.Abstractions.csproj" />
                <ProjectReference Include="..\..\..\src\Neo.L2.Executor\Neo.L2.Executor.csproj" />
              </ItemGroup>

            </Project>
            """);
    }

    private static void WriteExecutor(string output, string projectName, string rootNs)
    {
        var name = rootNs.Replace("Executor", "");
        File.WriteAllText(Path.Combine(output, $"{projectName}.cs"), $$"""
            using System.Buffers.Binary;
            using Neo;
            using Neo.Cryptography;
            using Neo.L2;
            using Neo.L2.Executor;
            using Neo.L2.Executor.Receipts;

            namespace {{rootNs}};

            /// <summary>
            /// Custom <see cref="ITransactionExecutor"/> for the {{name}} chain. Generated by
            /// `neo-stack scaffold-executor`. Replace the placeholder NoOp opcode below with
            /// your chain's transaction types.
            /// </summary>
            /// <remarks>
            /// Determinism contract (per <c>Neo.L2.Executor/SPEC.md</c>): receipts MUST be
            /// derivable from <c>(serializedTx, batchContext, preStateRoot)</c> alone — no
            /// clock reads, no RNG, no I/O.
            /// </remarks>
            public sealed class {{projectName}} : ITransactionExecutor
            {
                /// <summary>Gas charged for the placeholder NoOp opcode.</summary>
                public const long GasNoOp = 1;

                private readonly uint _chainId;
                private readonly I{{name}}State _state;
                private readonly UInt160 _emittingContract;

                /// <summary>Construct with chain id + state seam + emitting-contract sentinel.</summary>
                public {{projectName}}(uint chainId, I{{name}}State state, UInt160 emittingContract)
                {
                    ArgumentNullException.ThrowIfNull(state);
                    ArgumentNullException.ThrowIfNull(emittingContract);
                    _chainId = chainId;
                    _state = state;
                    _emittingContract = emittingContract;
                }

                /// <inheritdoc />
                public ValueTask<TransactionExecutionResult> ExecuteAsync(
                    ReadOnlyMemory<byte> serializedTx,
                    BatchBlockContext batchContext,
                    CancellationToken cancellationToken = default)
                {
                    ArgumentNullException.ThrowIfNull(batchContext);
                    cancellationToken.ThrowIfCancellationRequested();

                    var span = serializedTx.Span;
                    var txHash = new UInt256(Crypto.Hash256(span));

                    if (span.Length < 1) return Failed(txHash, gas: 0);
                    var op = (Opcode)span[0];
                    return op switch
                    {
                        Opcode.NoOp => ExecuteNoOp(txHash),
                        // TODO: add your chain's opcodes here.
                        _ => Failed(txHash, gas: 0),
                    };
                }

                private static ValueTask<TransactionExecutionResult> ExecuteNoOp(UInt256 txHash) =>
                    Result(txHash, GasNoOp,
                        storageDeltaHash: UInt256.Zero,
                        eventsHash: UInt256.Zero,
                        withdrawals: Array.Empty<WithdrawalRequest>(),
                        messages: Array.Empty<CrossChainMessage>());

                private static ValueTask<TransactionExecutionResult> Failed(UInt256 txHash, long gas) =>
                    new(new TransactionExecutionResult
                    {
                        Receipt = new Receipt
                        {
                            TxHash = txHash, Success = false, GasConsumed = gas,
                            StorageDeltaHash = UInt256.Zero, EventsHash = UInt256.Zero,
                        },
                        TxHash = txHash,
                        Withdrawals = Array.Empty<WithdrawalRequest>(),
                        Messages = Array.Empty<CrossChainMessage>(),
                    });

                private static ValueTask<TransactionExecutionResult> Result(
                    UInt256 txHash, long gas, UInt256 storageDeltaHash, UInt256 eventsHash,
                    IReadOnlyList<WithdrawalRequest> withdrawals, IReadOnlyList<CrossChainMessage> messages) =>
                    new(new TransactionExecutionResult
                    {
                        Receipt = new Receipt
                        {
                            TxHash = txHash, Success = true, GasConsumed = gas,
                            StorageDeltaHash = storageDeltaHash, EventsHash = eventsHash,
                        },
                        TxHash = txHash,
                        Withdrawals = withdrawals,
                        Messages = messages,
                    });

                /// <summary>The opcodes this chain understands. Add your own here.</summary>
                public enum Opcode : byte
                {
                    /// <summary>Unrecognized opcodes go to the failed-receipt path.</summary>
                    Invalid = 0,

                    /// <summary>Placeholder. Body: <c>[1B opcode]</c> (no payload). Returns a Success receipt with no effects.</summary>
                    NoOp = 1,
                }
            }
            """);
    }

    private static void WriteStateSeam(string output, string projectName, string rootNs)
    {
        var name = rootNs.Replace("Executor", "");
        File.WriteAllText(Path.Combine(output, $"I{name}State.cs"), $$"""
            namespace {{rootNs}};

            /// <summary>
            /// State seam for the {{name}} chain. Tests + the in-process devnet inject
            /// <see cref="InMemory{{name}}State"/>; production wires
            /// <see cref="{{name}}KeyedStateStoreAdapter"/> over <c>Neo.L2.Executor.State.KeyedStateStore</c>.
            /// </summary>
            public interface I{{name}}State
            {
                /// <summary>Read the value at <paramref name="key"/>; returns false if absent.</summary>
                bool TryGet(byte[] key, out byte[] value);

                /// <summary>Write (or overwrite) the value at <paramref name="key"/>.</summary>
                void Put(byte[] key, byte[] value);
            }

            /// <summary>Trivial in-memory implementation for tests.</summary>
            public sealed class InMemory{{name}}State : I{{name}}State
            {
                private readonly Dictionary<string, byte[]> _store = new();

                /// <inheritdoc />
                public bool TryGet(byte[] key, out byte[] value)
                {
                    ArgumentNullException.ThrowIfNull(key);
                    if (_store.TryGetValue(Convert.ToHexString(key), out var v)) { value = v; return true; }
                    value = Array.Empty<byte>();
                    return false;
                }

                /// <inheritdoc />
                public void Put(byte[] key, byte[] value)
                {
                    ArgumentNullException.ThrowIfNull(key);
                    ArgumentNullException.ThrowIfNull(value);
                    _store[Convert.ToHexString(key)] = value;
                }
            }
            """);
    }

    private static void WriteTxBuilder(string output, string projectName, string rootNs)
    {
        var name = rootNs.Replace("Executor", "");
        File.WriteAllText(Path.Combine(output, $"{name}TxBuilder.cs"), $$"""
            using System.Buffers.Binary;
            using Neo;

            namespace {{rootNs}};

            /// <summary>
            /// Helpers to build canonical {{name}} transaction bytes. Mirrors the wire format
            /// the executor decodes — keeping encode + decode in one repo means a future format
            /// change must update both sides at once.
            /// </summary>
            public static class {{name}}TxBuilder
            {
                /// <summary>Build a NoOp transaction: <c>[0x01]</c> (no payload).</summary>
                public static byte[] NoOp() => new[] { (byte){{projectName}}.Opcode.NoOp };

                // TODO: add builders for your chain's opcodes here.
            }
            """);
    }

    private static void WriteAdapter(string output, string projectName, string rootNs)
    {
        var name = rootNs.Replace("Executor", "");
        File.WriteAllText(Path.Combine(output, $"{name}KeyedStateStoreAdapter.cs"), $$"""
            using Neo.L2.Executor.State;

            namespace {{rootNs}};

            /// <summary>
            /// Production-ready bridge between <see cref="I{{name}}State"/> and the framework's
            /// canonical <see cref="KeyedStateStore"/>. With this adapter wired, the executor's
            /// writes flow into the same store the post-state-root oracle hashes — so
            /// <c>BatchExecutionResult.PostStateRoot</c> reflects the executor's actual state
            /// mutations.
            /// </summary>
            public sealed class {{name}}KeyedStateStoreAdapter : I{{name}}State
            {
                private readonly KeyedStateStore _store;

                /// <summary>Construct over the same <see cref="KeyedStateStore"/> the post-state-root oracle hashes.</summary>
                public {{name}}KeyedStateStoreAdapter(KeyedStateStore store)
                {
                    ArgumentNullException.ThrowIfNull(store);
                    _store = store;
                }

                /// <inheritdoc />
                public bool TryGet(byte[] key, out byte[] value)
                {
                    ArgumentNullException.ThrowIfNull(key);
                    var raw = _store.Get(key);
                    if (raw.Length == 0) { value = Array.Empty<byte>(); return false; }
                    value = raw.ToArray();
                    return true;
                }

                /// <inheritdoc />
                public void Put(byte[] key, byte[] value)
                {
                    ArgumentNullException.ThrowIfNull(key);
                    ArgumentNullException.ThrowIfNull(value);
                    _store.Put(key, value);
                }
            }
            """);
    }

    private static void WriteReadme(string output, string projectName, string rootNs, uint chainId)
    {
        var name = rootNs.Replace("Executor", "");
        File.WriteAllText(Path.Combine(output, "README.md"), $$"""
            # {{projectName}}

            Custom `ITransactionExecutor` scaffold for the **{{name}}** chain
            (chainId `{{chainId}}` by default). Generated by `neo-stack scaffold-executor`.

            ## What's in here

            | File | Role |
            |------|------|
            | `{{projectName}}.cs` | The executor — implements `ITransactionExecutor.ExecuteAsync` |
            | `I{{name}}State.cs` | State seam + `InMemory{{name}}State` for tests |
            | `{{name}}TxBuilder.cs` | Canonical tx-byte builders (mirrors executor's decoder) |
            | `{{name}}KeyedStateStoreAdapter.cs` | Production bridge to `Neo.L2.Executor.State.KeyedStateStore` |
            | `{{projectName}}.csproj` | Builds against `Neo.L2.Abstractions` + `Neo.L2.Executor` |

            ## Build

            ```bash
            dotnet build {{projectName}}.csproj /p:NuGetAudit=false
            ```

            The scaffold compiles as-is — the placeholder `NoOp` opcode (success-receipt
            with no effects) is enough to exercise the seam.

            ## 5-step customization checklist

            1. **Define your opcodes.** Edit the `Opcode` enum in `{{projectName}}.cs` to add
               `IncrementCounter`, `EmitWithdrawal`, `EmitMessage`, etc. Each opcode is
               one byte at offset 0; the rest of the tx is opcode-specific body bytes.
            2. **Add executor methods.** For each opcode, add an `Execute<Op>` private method
               that decodes the body, mutates `_state` if needed, returns a `Receipt` +
               withdrawals + messages via `Result(...)`. Update the dispatch `switch` in
               `ExecuteAsync`.
            3. **Match tx builders.** For each opcode, add a `{{name}}TxBuilder.<Op>(...)` method
               that builds the canonical bytes — same shape the decoder reads. Tests build
               via the helpers; encode/decode parity is enforced by the build.
            4. **Define your state model.** Replace the per-key reads/writes with whatever
               state shape your chain needs. The `I{{name}}State` interface is intentionally
               byte-array-level so both `InMemory{{name}}State` (tests) and
               `{{name}}KeyedStateStoreAdapter` (production) can implement it cleanly.
            5. **Wire into a batch executor.** In production, instantiate
               `{{projectName}}` with your `chainId`, a `{{name}}KeyedStateStoreAdapter` over
               your `KeyedStateStore`, and an emitting-contract `UInt160`. Hand it to
               `ReferenceBatchExecutor`. The standard pipeline (sealing, proving, settlement,
               fraud-proof) takes it from there with no further wiring.

            ## Reference

            - Working example: [`samples/executors/Sample.CounterChainExecutor`](https://github.com/r3e-network/neo-n4/tree/master/samples/executors/Sample.CounterChainExecutor) — same shape, with three opcodes (IncrementCounter / EmitWithdrawal / EmitMessage) instead of just NoOp.
            - Determinism contract: [`Neo.L2.Executor/SPEC.md`](https://github.com/r3e-network/neo-n4/blob/master/src/Neo.L2.Executor/SPEC.md)
            - Mapping doc.md → code: [`AGENTS.md`](https://github.com/r3e-network/neo-n4/blob/master/AGENTS.md)
            """);
    }
}
