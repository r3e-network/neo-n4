using Neo.Json;

namespace Neo.Hub.Deploy;

/// <summary>
/// Generates the canonical default <see cref="DeployPlan"/> that matches the layout in
/// doc.md §3.2 and §13.1. The 9 NeoHub contracts deploy in dependency order; L2 native
/// contracts are listed but commented as "deploy on the L2", not the L1.
/// </summary>
public static class ScaffoldPlan
{
    /// <summary>Build the canonical plan.</summary>
    public static DeployPlan Default()
    {
        return new DeployPlan
        {
            Version = 1,
            Network = "neo-n3-testnet",
            Steps = new[]
            {
                Step("ChainRegistry",
                    "contracts/NeoHub.ChainRegistry/bin/Release/NeoHub.ChainRegistry.nef",
                    OwnerOnly()),

                Step("VerifierRegistry",
                    "contracts/NeoHub.VerifierRegistry/bin/Release/NeoHub.VerifierRegistry.nef",
                    OwnerOnly()),

                Step("TokenRegistry",
                    "contracts/NeoHub.TokenRegistry/bin/Release/NeoHub.TokenRegistry.nef",
                    OwnerOnly()),

                Step("DARegistry",
                    "contracts/NeoHub.DARegistry/bin/Release/NeoHub.DARegistry.nef",
                    OwnerAndDep("SettlementManager"),
                    "SettlementManager"),

                Step("EmergencyManager",
                    "contracts/NeoHub.EmergencyManager/bin/Release/NeoHub.EmergencyManager.nef",
                    OwnerAndDeps("GovernanceController", "SettlementManager"),
                    "GovernanceController", "SettlementManager"),

                Step("GovernanceController",
                    "contracts/NeoHub.GovernanceController/bin/Release/NeoHub.GovernanceController.nef",
                    OwnerOnly()),

                Step("MessageRouter",
                    "contracts/NeoHub.MessageRouter/bin/Release/NeoHub.MessageRouter.nef",
                    OwnerAndDep("SettlementManager"),
                    "SettlementManager"),

                Step("SettlementManager",
                    "contracts/NeoHub.SettlementManager/bin/Release/NeoHub.SettlementManager.nef",
                    OwnerAndDeps("ChainRegistry", "VerifierRegistry"),
                    "ChainRegistry", "VerifierRegistry"),

                Step("SharedBridge",
                    "contracts/NeoHub.SharedBridge/bin/Release/NeoHub.SharedBridge.nef",
                    OwnerAndDeps("SettlementManager", "TokenRegistry"),
                    "SettlementManager", "TokenRegistry"),

                Step("ForcedInclusion",
                    "contracts/NeoHub.ForcedInclusion/bin/Release/NeoHub.ForcedInclusion.nef",
                    OwnerAndDep("SettlementManager"),
                    "SettlementManager"),
            },
        };
    }

    private static DeployStep Step(string name, string nefPath, JArray deployData, params string[] dependsOn)
    {
        return new DeployStep
        {
            Name = name,
            NefPath = nefPath,
            ManifestPath = nefPath.Replace(".nef", ".manifest.json"),
            DeployData = deployData,
            DependsOn = dependsOn,
        };
    }

    private static JArray OwnerOnly()
    {
        return new JArray { "OWNER_REPLACE_ME" };
    }

    private static JArray OwnerAndDep(string depName)
    {
        return new JArray { "OWNER_REPLACE_ME", $"$step:{depName}" };
    }

    private static JArray OwnerAndDeps(params string[] depNames)
    {
        var arr = new JArray { "OWNER_REPLACE_ME" };
        foreach (var d in depNames) arr.Add($"$step:{d}");
        return arr;
    }
}
