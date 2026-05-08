using System;
using System.Collections.Generic;
using System.Numerics;
using Neo.Extensions.VM;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Bridge.Cli.Commands;

/// <summary>
/// Builds canonical Neo VM invocation scripts for the bridge calls. The output is the
/// exact byte sequence Neo's RPC <c>invokefunction</c> / wallet <c>SignAndBroadcast</c>
/// expect — paste-pasting the hex into any Neo wallet that speaks dotnet-Neo's script
/// model produces a valid bridge invocation transaction.
/// </summary>
internal static class InvocationBuilder
{
    /// <summary>
    /// Build the canonical invocation script for <c>SharedBridge.Deposit(asset, amount, targetChainId, l2Recipient)</c>.
    /// </summary>
    public static byte[] BuildDeposit(
        UInt160 bridge,
        UInt160 asset,
        BigInteger amount,
        uint targetChainId,
        UInt160 l2Recipient)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(l2Recipient);
        if (amount <= 0) throw new ArgumentException("amount must be positive", nameof(amount));
        if (targetChainId == 0) throw new ArgumentException("targetChainId must be non-zero (0 is reserved for L1)", nameof(targetChainId));

        using var sb = new ScriptBuilder();
        sb.EmitDynamicCall(bridge, "deposit",
            CallFlags.All,
            new object[] { asset, amount, targetChainId, l2Recipient });
        return sb.ToArray();
    }

    /// <summary>
    /// Build the canonical invocation script for
    /// <c>SharedBridge.FinalizeWithdrawalWithProof(chainId, batchNumber, withdrawalLeafHash, siblings, leafIndex, asset, recipient, amount)</c>.
    /// </summary>
    public static byte[] BuildFinalizeWithdrawalWithProof(
        UInt160 bridge,
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        IReadOnlyList<UInt256> siblings,
        ulong leafIndex,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(withdrawalLeafHash);
        ArgumentNullException.ThrowIfNull(siblings);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(recipient);
        if (chainId == 0) throw new ArgumentException("chainId 0 is reserved for L1", nameof(chainId));
        if (amount <= 0) throw new ArgumentException("amount must be positive", nameof(amount));

        // The contract's `byte[][] siblings` parameter requires an array-of-byte-arrays
        // pushed via the ContractParameter wrapper (Neo's EmitPush(object) doesn't have a
        // built-in case for jagged byte[][]). Wrap each 32-byte sibling as a ByteArray
        // ContractParameter, then wrap the whole list as an Array parameter — the
        // EmitPush switch's Array case PACKs them with the right cardinality.
        var siblingParams = new List<ContractParameter>(siblings.Count);
        foreach (var s in siblings)
        {
            ArgumentNullException.ThrowIfNull(s);
            siblingParams.Add(new ContractParameter(ContractParameterType.ByteArray)
            {
                Value = s.GetSpan().ToArray(),
            });
        }
        var siblingsArrayParam = new ContractParameter(ContractParameterType.Array)
        {
            Value = siblingParams,
        };

        using var sb = new ScriptBuilder();
        sb.EmitDynamicCall(bridge, "finalizeWithdrawalWithProof",
            CallFlags.All,
            new object[]
            {
                chainId,
                batchNumber,
                withdrawalLeafHash,
                siblingsArrayParam,
                leafIndex,
                asset,
                recipient,
                amount,
            });
        return sb.ToArray();
    }
}
