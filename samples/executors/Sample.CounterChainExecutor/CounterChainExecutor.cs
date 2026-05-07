using System.Buffers.Binary;
using Neo;
using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;
using Neo.L2.Messaging;
using Neo.L2.State;

namespace Sample.CounterChainExecutor;

/// <summary>
/// Reference implementation of <see cref="ITransactionExecutor"/> for a domain-specific
/// L2 ("Counter Chain") that demonstrates how an operator plugs custom chain logic into
/// the Neo Elastic Network framework.
/// </summary>
/// <remarks>
/// <para>
/// The framework is intentionally a host of seams (<see cref="ITransactionExecutor"/>,
/// <c>IL2Prover</c> / <c>IL2ProofVerifier</c>, <c>IDAWriter</c>,
/// <c>ISequencerCommitteeProvider</c>) so a new L2 chain doesn't need to fork the framework
/// — it brings its own implementation behind the seam, gets routed through the standard
/// <c>ReferenceBatchExecutor</c> ↦ <c>BatchSerializer</c> ↦ Settlement pipeline, and
/// inherits all the cross-cutting machinery (Merkle / receipt / withdrawal / message roots,
/// fraud-proof bytes, DA publish, fraud verifier, governance gating).
/// </para>
/// <para>
/// This sample handles three custom transaction opcodes:
/// </para>
/// <code>
/// Opcode 0x01 (IncrementCounter):  [1B opcode][20B sender][8B u64 amount LE]
/// Opcode 0x02 (EmitWithdrawal):    [1B opcode][20B recipient][20B token][8B u64 amount LE]
/// Opcode 0x03 (EmitMessage):       [1B opcode][4B destChainId LE][2B msgLen LE][N bytes msg]
/// </code>
/// <para>
/// State model: a <see cref="ICounterChainState"/> seam keeps the executor decoupled from
/// any concrete state store (the test injects a <see cref="InMemoryCounterChainState"/>;
/// production code wires <c>Neo.L2.Executor.State.KeyedStateStore</c>). Per-key state in
/// this sample is keyed by <c>"counter:" + sender(20B)</c> with a u64 LE value.
/// </para>
/// <para>
/// Determinism contract (per <c>Neo.L2.Executor/SPEC.md</c>): receipts MUST be derivable
/// from <c>(serializedTx, batchContext, preStateRoot)</c> alone — no clock, no I/O, no
/// non-deterministic enumeration. This sample reads + writes only through the injected
/// state seam and the input bytes; gas accounting is a fixed schedule per opcode so the
/// total <c>GasConsumed</c> on a batch is reproducible by any verifier.
/// </para>
/// <para>
/// To plug into the framework: instantiate <see cref="CounterChainExecutor"/> with your
/// chainId + state store + emitting-contract sentinel, hand it to a
/// <see cref="ReferenceBatchExecutor"/>, and the rest of the pipeline (batch sealing,
/// proving, settlement, fraud-proof) is already wired by the Neo Elastic Network plug-ins.
/// </para>
/// </remarks>
public sealed class CounterChainExecutor : ITransactionExecutor
{
    /// <summary>Gas charged per <see cref="Opcode.IncrementCounter"/> transaction.</summary>
    public const long GasIncrementCounter = 100;

    /// <summary>Gas charged per <see cref="Opcode.EmitWithdrawal"/> transaction.</summary>
    public const long GasEmitWithdrawal = 500;

    /// <summary>Gas charged per <see cref="Opcode.EmitMessage"/> transaction.</summary>
    public const long GasEmitMessage = 200;

    /// <summary>Cap on the inline message body in <see cref="Opcode.EmitMessage"/>.</summary>
    public const int MaxMessageBytes = 4096;

    /// <summary>Storage-key prefix for per-sender counter values.</summary>
    public static readonly byte[] CounterKeyPrefix = "counter:"u8.ToArray();

    private readonly uint _chainId;
    private readonly ICounterChainState _state;
    private readonly UInt160 _emittingContract;

    /// <summary>Construct with the chain id (so cross-chain messages have the right source),
    /// the state seam this chain mutates, and the L2 contract address that "owns"
    /// withdrawals + messages emitted by this executor.</summary>
    public CounterChainExecutor(uint chainId, ICounterChainState state, UInt160 emittingContract)
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

        // SPEC.md determinism: a malformed tx is a normal "execution failure" — we still
        // produce a Receipt with Success=false so the receipt root is fully derivable.
        // Throwing here would crash the whole batch on one bad tx, which is wrong per spec.
        if (span.Length < 1)
            return Failed(txHash, gas: 0);

        var op = (Opcode)span[0];
        return op switch
        {
            Opcode.IncrementCounter => ExecuteIncrementCounter(span, txHash),
            Opcode.EmitWithdrawal => ExecuteEmitWithdrawal(span, txHash),
            Opcode.EmitMessage => ExecuteEmitMessage(span, txHash),
            _ => Failed(txHash, gas: 0),
        };
    }

    private ValueTask<TransactionExecutionResult> ExecuteIncrementCounter(
        ReadOnlySpan<byte> tx, UInt256 txHash)
    {
        if (tx.Length != 1 + 20 + 8) return Failed(txHash, GasIncrementCounter);
        var sender = tx.Slice(1, 20).ToArray();
        var amount = BinaryPrimitives.ReadUInt64LittleEndian(tx.Slice(21, 8));
        var key = BuildCounterKey(sender);
        var prev = _state.TryGet(key, out var existing)
            ? BinaryPrimitives.ReadUInt64LittleEndian(existing.AsSpan(0, 8))
            : 0UL;
        var next = unchecked(prev + amount);  // overflow wraps — same as Neo Native NEP-17 mint semantics
        var nextBytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(nextBytes, next);
        _state.Put(key, nextBytes);

        return Result(txHash, GasIncrementCounter,
            storageDeltaHash: HashStorageDelta(key, nextBytes),
            eventsHash: UInt256.Zero,
            withdrawals: Array.Empty<WithdrawalRequest>(),
            messages: Array.Empty<CrossChainMessage>());
    }

    private ValueTask<TransactionExecutionResult> ExecuteEmitWithdrawal(
        ReadOnlySpan<byte> tx, UInt256 txHash)
    {
        if (tx.Length != 1 + 20 + 20 + 8) return Failed(txHash, GasEmitWithdrawal);
        var recipient = new UInt160(tx.Slice(1, 20));
        var token = new UInt160(tx.Slice(21, 20));
        var amount = BinaryPrimitives.ReadUInt64LittleEndian(tx.Slice(41, 8));
        if (amount == 0) return Failed(txHash, GasEmitWithdrawal);  // disallow zero-amount withdrawals

        // Nonce derivation: first 8 bytes of the tx hash. Deterministic + unique-per-tx.
        // A real chain would track a per-sender monotonic counter in state; the sample
        // uses txHash to keep the demo self-contained.
        var nonce = BinaryPrimitives.ReadUInt64LittleEndian(txHash.GetSpan().Slice(0, 8));

        var withdrawal = new WithdrawalRequest
        {
            EmittingContract = _emittingContract,
            L2Sender = recipient,
            L1Recipient = recipient,
            L2Asset = token,
            Amount = new System.Numerics.BigInteger(amount),
            Nonce = nonce,
        };

        return Result(txHash, GasEmitWithdrawal,
            storageDeltaHash: UInt256.Zero,
            eventsHash: HashEvent("withdrawal", txHash),
            withdrawals: new[] { withdrawal },
            messages: Array.Empty<CrossChainMessage>());
    }

    private ValueTask<TransactionExecutionResult> ExecuteEmitMessage(
        ReadOnlySpan<byte> tx, UInt256 txHash)
    {
        if (tx.Length < 1 + 4 + 2) return Failed(txHash, GasEmitMessage);
        var destChainId = BinaryPrimitives.ReadUInt32LittleEndian(tx.Slice(1, 4));
        var msgLen = BinaryPrimitives.ReadUInt16LittleEndian(tx.Slice(5, 2));
        if (msgLen > MaxMessageBytes) return Failed(txHash, GasEmitMessage);
        if (tx.Length != 1 + 4 + 2 + msgLen) return Failed(txHash, GasEmitMessage);
        // Self-routed messages (source == target) have no transport — fail at execution
        // time rather than at message-tree-build time. Same constraint MessageBuilder
        // enforces.
        if (destChainId == _chainId) return Failed(txHash, GasEmitMessage);
        var body = tx.Slice(7, msgLen).ToArray();

        var nonce = BinaryPrimitives.ReadUInt64LittleEndian(txHash.GetSpan().Slice(0, 8));
        var message = MessageBuilder.Build(
            sourceChainId: _chainId,
            targetChainId: destChainId,
            nonce: nonce,
            sender: _emittingContract,
            receiver: _emittingContract,  // sample echoes; production routes to a domain-specific receiver
            messageType: MessageType.Call,
            payload: body);

        return Result(txHash, GasEmitMessage,
            storageDeltaHash: UInt256.Zero,
            eventsHash: HashEvent("message", txHash),
            withdrawals: Array.Empty<WithdrawalRequest>(),
            messages: new[] { message });
    }

    private static ValueTask<TransactionExecutionResult> Failed(UInt256 txHash, long gas) =>
        new(new TransactionExecutionResult
        {
            Receipt = new Receipt
            {
                TxHash = txHash,
                Success = false,
                GasConsumed = gas,
                StorageDeltaHash = UInt256.Zero,
                EventsHash = UInt256.Zero,
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
                TxHash = txHash,
                Success = true,
                GasConsumed = gas,
                StorageDeltaHash = storageDeltaHash,
                EventsHash = eventsHash,
            },
            TxHash = txHash,
            Withdrawals = withdrawals,
            Messages = messages,
        });

    private static byte[] BuildCounterKey(ReadOnlySpan<byte> sender)
    {
        var key = new byte[CounterKeyPrefix.Length + sender.Length];
        CounterKeyPrefix.CopyTo(key, 0);
        sender.CopyTo(key.AsSpan(CounterKeyPrefix.Length));
        return key;
    }

    private static UInt256 HashStorageDelta(ReadOnlySpan<byte> key, ReadOnlySpan<byte> newValue)
    {
        var buf = new byte[key.Length + newValue.Length];
        key.CopyTo(buf);
        newValue.CopyTo(buf.AsSpan(key.Length));
        return new UInt256(Crypto.Hash256(buf));
    }

    private static UInt256 HashEvent(string tag, UInt256 txHash)
    {
        var tagBytes = System.Text.Encoding.UTF8.GetBytes(tag);
        var buf = new byte[tagBytes.Length + 32];
        tagBytes.CopyTo(buf, 0);
        txHash.GetSpan().CopyTo(buf.AsSpan(tagBytes.Length));
        return new UInt256(Crypto.Hash256(buf));
    }

    /// <summary>The three opcodes this chain understands.</summary>
    public enum Opcode : byte
    {
        /// <summary>Unrecognized opcodes go to the failed-receipt path with success=false.</summary>
        Invalid = 0,

        /// <summary>Add to the per-sender counter. Body: <c>[20B sender][8B u64 amount LE]</c>.</summary>
        IncrementCounter = 1,

        /// <summary>Emit an L2→L1 withdrawal request. Body: <c>[20B recipient][20B token][8B u64 amount LE]</c>.</summary>
        EmitWithdrawal = 2,

        /// <summary>Emit a generic L2→L2 message. Body: <c>[4B destChainId LE][2B msgLen LE][N bytes msg]</c>.</summary>
        EmitMessage = 3,
    }
}
