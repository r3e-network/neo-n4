using System.Globalization;
using System.Text.Json;
using Neo.Cryptography.ECC;
using Neo.L2.Sequencer;

namespace Neo.Stack.Cli.Commands;

internal sealed record SequencerLaunchConfiguration(
    uint ChainId,
    uint Network,
    uint MillisecondsPerBlock,
    int CommitteeMembersCount,
    IReadOnlyList<ECPoint> Validators,
    bool GenesisValidatorsMatch,
    string StoragePath,
    bool UnlockWalletActive,
    string WalletPath)
{
    public static bool TryLoad(
        string chainConfigPath,
        string nodeConfigPath,
        uint expectedChainId,
        out SequencerLaunchConfiguration configuration,
        out string error)
    {
        configuration = null!;
        error = "";
        try
        {
            using var chainDocument = JsonDocument.Parse(File.ReadAllText(chainConfigPath));
            using var nodeDocument = JsonDocument.Parse(File.ReadAllText(nodeConfigPath));
            var chain = chainDocument.RootElement;
            var application = nodeDocument.RootElement.GetProperty("ApplicationConfiguration");
            var protocol = nodeDocument.RootElement.GetProperty("ProtocolConfiguration");

            var chainId = chain.GetProperty("chainId").GetUInt32();
            if (chainId != expectedChainId)
                throw new InvalidDataException($"chain.config.json chainId {chainId} does not match --chain-id {expectedChainId}.");
            var sequencerModel = chain.GetProperty("sequencerModel").GetString();
            if (!string.Equals(sequencerModel, "DbftCommittee", StringComparison.Ordinal))
                throw new InvalidDataException("Production start-sequencer requires sequencerModel=DbftCommittee.");

            var validatorCount = protocol.GetProperty("ValidatorsCount").GetInt32();
            var validators = ParseKeys(chain.GetProperty("validators"));
            var canonical = SequencerCommitteeTransactionBuilder.Normalize(validators, validatorCount);
            var standby = ParseKeys(protocol.GetProperty("StandbyCommittee"));
            if (standby.Count < validatorCount)
                throw new InvalidDataException("Node StandbyCommittee contains fewer entries than ValidatorsCount.");
            var genesis = SequencerCommitteeTransactionBuilder.Normalize(standby.Take(validatorCount), validatorCount);
            var genesisValidatorsMatch = canonical.SequenceEqual(genesis);

            var network = protocol.GetProperty("Network").GetUInt32();
            if (network == 0) throw new InvalidDataException("ProtocolConfiguration.Network must be non-zero for an L2 deployment.");
            var millisecondsPerBlock = protocol.GetProperty("MillisecondsPerBlock").GetUInt32();
            if (millisecondsPerBlock == 0)
                throw new InvalidDataException("ProtocolConfiguration.MillisecondsPerBlock must be positive.");
            if (chain.TryGetProperty("milestonePerBlockMs", out var configuredBlockTime)
                && configuredBlockTime.GetUInt32() != millisecondsPerBlock)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"chain.config.json milestonePerBlockMs {configuredBlockTime.GetUInt32()} does not match node MillisecondsPerBlock {millisecondsPerBlock}."));
            }

            var storagePath = application
                .GetProperty("Storage")
                .GetProperty("Path")
                .GetString();
            if (string.IsNullOrWhiteSpace(storagePath) || storagePath.Contains("{0}", StringComparison.Ordinal))
                throw new InvalidDataException(
                    "ApplicationConfiguration.Storage.Path must be a non-empty isolated base directory without a {0} placeholder.");
            var unlockWalletActive = false;
            var walletPath = "";
            if (application.TryGetProperty("UnlockWallet", out var unlockWallet))
            {
                unlockWalletActive = unlockWallet.TryGetProperty("IsActive", out var active) && active.GetBoolean();
                walletPath = unlockWallet.TryGetProperty("Path", out var path)
                    ? path.GetString() ?? ""
                    : "";
            }

            configuration = new SequencerLaunchConfiguration(
                chainId,
                network,
                millisecondsPerBlock,
                standby.Count,
                canonical,
                genesisValidatorsMatch,
                storagePath,
                unlockWalletActive,
                walletPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or KeyNotFoundException
            or InvalidOperationException or FormatException or ArgumentException)
        {
            error = $"Invalid operator configuration: {exception.Message}";
            return false;
        }
    }

    private static IReadOnlyList<ECPoint> ParseKeys(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Validator keys must be a JSON array.");
        return array.EnumerateArray()
            .Select(element => ECPoint.Parse(
                element.GetString() ?? throw new InvalidDataException("Validator key cannot be null."),
                ECCurve.Secp256r1))
            .ToArray();
    }
}
