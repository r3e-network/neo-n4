using System.Numerics;
using Neo.L2.Bridge.External;
using Neo.L2.Messaging;

namespace Neo.L2.ExternalBridge.UnitTests;

internal static class PayoutTestData
{
    internal const uint ExternalChainId = 0xE0000038;
    internal const uint NeoChainId = 1099;
    internal static readonly UInt160 Adapter = H160(0x11);
    internal static readonly UInt160 ForeignSender = H160(0x22);
    internal static readonly ExternalAssetId ForeignAsset =
        ExternalAssetId.Parse("11223344556677889900aabbccddeeff00112233");
    internal static readonly UInt160 NeoAsset = H160(0x44);
    internal static readonly UInt160 Recipient = H160(0x55);
    internal static readonly UInt160 RelayAccount = H160(0x77);
    internal static readonly UInt160 NativeBridge = H160(0x88);
    internal static readonly UInt256 SourceTransaction = H256(0x66);
    internal static readonly UInt256 L2Transaction = H256(0x99);

    internal static L2PayoutInstruction Instruction(BigInteger? amount = null)
    {
        var payload = new ExternalAssetTransferPayload
        {
            ForeignAsset = ForeignAsset,
            Amount = amount ?? 25,
        }.Encode();
        var message = ExternalMessageBuilder.Build(
            ExternalChainId,
            NeoChainId,
            nonce: 7,
            ExternalBridgeDirection.ForeignToNeo,
            ForeignSender,
            Recipient,
            deadlineUnixSeconds: 1_900_000_000,
            SourceTransaction,
            ExternalMessageType.AssetTransfer,
            payload);
        var canonical = ExternalMessageHasher.EncodeCanonical(message);
        return L2PayoutInstruction.Decode(
            sequence: 1,
            Adapter,
            NeoAsset,
            message.MessageHash,
            canonical,
            NeoChainId);
    }

    internal static UInt160 H160(byte value) =>
        new(Enumerable.Repeat(value, UInt160.Length).ToArray());

    internal static UInt256 H256(byte value) =>
        new(Enumerable.Repeat(value, UInt256.Length).ToArray());
}
