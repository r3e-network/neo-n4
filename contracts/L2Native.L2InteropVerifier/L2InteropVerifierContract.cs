using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace L2Native.L2InteropVerifier;

/// <summary>
/// L2-side helper that mirrors L1-published global message roots and verifies
/// peer-chain message inclusion locally for dApps.
/// </summary>
[DisplayName("L2Native.L2InteropVerifier")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L2-side global message root verifier for cross-L2 interoperability.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/L2Native.L2InteropVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2InteropVerifierContract : SmartContract
{
    private const byte PrefixGlobalRoot = 0x01;
    private const byte PrefixConsumed = 0x02;
    private const byte PrefixRootHeight = 0x03;
    private const byte KeyBatchInfo = 0xFD;
    private const byte KeySystemAccount = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <summary>Maximum accepted Merkle proof depth.</summary>
    public const int MaxProofDepth = 64;

    /// <summary>Emitted when a global root is mirrored to the L2.</summary>
    [DisplayName("GlobalRootMirrored")]
    public static event Action<ulong, UInt256, uint> OnGlobalRootMirrored = default!;

    /// <summary>Emitted when a message leaf is consumed.</summary>
    [DisplayName("MessageConsumed")]
    public static event Action<ulong, UInt256> OnMessageConsumed = default!;

    /// <summary>Deploy data: [owner, systemAccount, batchInfoContract].</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var systemAccount = (UInt160)arr[1];
        var batchInfo = (UInt160)arr[2];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(systemAccount.IsValid && !systemAccount.IsZero, "invalid system account");
        ExecutionEngine.Assert(batchInfo.IsValid && !batchInfo.IsZero, "invalid batch info");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeySystemAccount }, systemAccount);
        Storage.Put(new byte[] { KeyBatchInfo }, batchInfo);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Sequencer/system account allowed to mirror L1 roots.</summary>
    [Safe]
    public static UInt160 GetSystemAccount()
    {
        var raw = Storage.Get(new byte[] { KeySystemAccount });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>L2BatchInfo contract used to check the mirrored L1 finalized height.</summary>
    [Safe]
    public static UInt160 GetBatchInfo()
    {
        var raw = Storage.Get(new byte[] { KeyBatchInfo });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Mirror a global message root after the L2 has observed a sufficient L1 finalized
    /// height. Publish-once semantics prevent root replacement after dApps take proofs.
    /// </summary>
    public static void PublishGlobalRoot(ulong epoch, UInt256 root, uint l1FinalizedHeight)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetSystemAccount()), "not system");
        ExecutionEngine.Assert(!root.Equals(UInt256.Zero), "root must be non-zero");

        var batchInfo = GetBatchInfo();
        var currentL1Height = (uint)(BigInteger)Contract.Call(
            batchInfo,
            "getL1FinalizedHeight",
            CallFlags.ReadOnly,
            new object[0]);
        ExecutionEngine.Assert(l1FinalizedHeight <= currentL1Height,
            "root height not finalized on this L2 yet");

        var key = GlobalRootKey(epoch);
        ExecutionEngine.Assert(Storage.Get(key) == null, "global root already mirrored");
        Storage.Put(key, (byte[])root);
        Storage.Put(RootHeightKey(epoch), (BigInteger)l1FinalizedHeight);
        OnGlobalRootMirrored(epoch, root, l1FinalizedHeight);
    }

    /// <summary>Read a mirrored global root.</summary>
    [Safe]
    public static UInt256 GetGlobalRoot(ulong epoch)
    {
        var raw = Storage.Get(GlobalRootKey(epoch));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>Read the L1 finalized height used when mirroring the root.</summary>
    [Safe]
    public static uint GetRootL1Height(ulong epoch)
    {
        var raw = Storage.Get(RootHeightKey(epoch));
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>Verify a message leaf against a mirrored global root.</summary>
    [Safe]
    public static bool VerifyMessage(ulong epoch, UInt256 leafHash, byte[][] siblings, ulong leafIndex)
    {
        var root = GetGlobalRoot(epoch);
        if (root.Equals(UInt256.Zero)) return false;
        var computed = FoldMerkle(leafHash, siblings, leafIndex);
        return root.Equals(computed);
    }

    /// <summary>Verify and consume a message leaf to prevent replay by local dApps.</summary>
    public static void ConsumeMessage(ulong epoch, UInt256 leafHash, byte[][] siblings, ulong leafIndex)
    {
        ExecutionEngine.Assert(VerifyMessage(epoch, leafHash, siblings, leafIndex),
            "message proof rejected");
        var key = ConsumedKey(epoch, leafHash);
        ExecutionEngine.Assert(Storage.Get(key) == null, "message already consumed");
        Storage.Put(key, new byte[] { 1 });
        OnMessageConsumed(epoch, leafHash);
    }

    /// <summary>True if the leaf has been consumed locally.</summary>
    [Safe]
    public static bool IsConsumed(ulong epoch, UInt256 leafHash)
    {
        return Storage.Get(ConsumedKey(epoch, leafHash)) != null;
    }

    private static UInt256 FoldMerkle(UInt256 leafHash, byte[][] siblings, ulong leafIndex)
    {
        ExecutionEngine.Assert(siblings.Length <= MaxProofDepth, "proof too deep");
        var current = (byte[])leafHash;
        var index = leafIndex;
        for (var i = 0; i < siblings.Length; i++)
        {
            var sibling = siblings[i];
            ExecutionEngine.Assert(sibling.Length == 32, "sibling must be 32 bytes");
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                for (var j = 0; j < 32; j++) combined[j] = current[j];
                for (var j = 0; j < 32; j++) combined[32 + j] = sibling[j];
            }
            else
            {
                for (var j = 0; j < 32; j++) combined[j] = sibling[j];
                for (var j = 0; j < 32; j++) combined[32 + j] = current[j];
            }

            var h1 = CryptoLib.Sha256((ByteString)combined);
            current = (byte[])CryptoLib.Sha256(h1);
            index = index >> 1;
        }

        return (UInt256)current;
    }

    private static byte[] GlobalRootKey(ulong epoch)
    {
        var k = new byte[9];
        k[0] = PrefixGlobalRoot;
        WriteUInt64(k, 1, epoch);
        return k;
    }

    private static byte[] RootHeightKey(ulong epoch)
    {
        var k = new byte[9];
        k[0] = PrefixRootHeight;
        WriteUInt64(k, 1, epoch);
        return k;
    }

    private static byte[] ConsumedKey(ulong epoch, UInt256 leafHash)
    {
        var k = new byte[1 + 8 + 32];
        k[0] = PrefixConsumed;
        WriteUInt64(k, 1, epoch);
        var leafBytes = (byte[])leafHash;
        for (var i = 0; i < 32; i++) k[9 + i] = leafBytes[i];
        return k;
    }

    private static void WriteUInt64(byte[] k, int offset, ulong value)
    {
        k[offset] = (byte)value;
        k[offset + 1] = (byte)(value >> 8);
        k[offset + 2] = (byte)(value >> 16);
        k[offset + 3] = (byte)(value >> 24);
        k[offset + 4] = (byte)(value >> 32);
        k[offset + 5] = (byte)(value >> 40);
        k[offset + 6] = (byte)(value >> 48);
        k[offset + 7] = (byte)(value >> 56);
    }
}
