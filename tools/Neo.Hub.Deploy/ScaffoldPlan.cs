using Neo.Json;

namespace Neo.Hub.Deploy;

/// <summary>
/// Generates the canonical default <see cref="DeployPlan"/> that matches the layout in
/// doc.md §3.2 and §13.1. The 13 NeoHub contracts deploy in dependency order; L2 native
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

                // SequencerBond holds the bonded GAS that backs each sequencer + pays out
                // slash awards. Initial slashers list is just GovernanceController so the
                // bond contract can deploy first without a cycle. After OptimisticChallenge
                // is up the operator calls SequencerBond.RegisterSlasher(optChallenge) to
                // enable Phase-3 challenge slashing.
                Step("SequencerBond",
                    "contracts/NeoHub.SequencerBond/bin/Release/NeoHub.SequencerBond.nef",
                    BondDeployData(),
                    "GovernanceController"),

                // SequencerRegistry tracks the live committee + exit windows. Bonds are
                // checked against SequencerBond before a register goes through.
                Step("SequencerRegistry",
                    "contracts/NeoHub.SequencerRegistry/bin/Release/NeoHub.SequencerRegistry.nef",
                    OwnerAndDep("SequencerBond"),
                    "SequencerBond"),

                // OptimisticChallenge is the Phase 3 contract. Its window timer + slash
                // payout requires both SettlementManager (for batch lookups) + SequencerBond
                // (to deduct on a successful challenge).
                Step("OptimisticChallenge",
                    "contracts/NeoHub.OptimisticChallenge/bin/Release/NeoHub.OptimisticChallenge.nef",
                    OwnerAndDeps("SettlementManager", "SequencerBond"),
                    "SettlementManager", "SequencerBond"),
            },
        };
    }

    /// <summary>
    /// SequencerBond's deploy_data: (owner, bondAsset, slashers[]). Initial slashers list
    /// only includes GovernanceController to avoid a deploy cycle (OptimisticChallenge
    /// itself depends on SequencerBond). After OptimisticChallenge is deployed, the
    /// operator must call <c>SequencerBond.RegisterSlasher(optChallenge)</c> to enable
    /// the Phase 3 challenge slash payout. The slashers array is resolved by the same
    /// <c>$step:</c> machinery as scalars — see <c>DeployPlanner.ResolveToken</c>'s
    /// JArray branch.
    /// </summary>
    private static JArray BondDeployData()
    {
        return new JArray
        {
            "OWNER_REPLACE_ME",
            "BOND_ASSET_REPLACE_ME",   // canonical GAS hash on the target L1
            new JArray
            {
                "$step:GovernanceController",
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

    /// <summary>
    /// Operator follow-up calls that aren't part of the bundle itself but are needed
    /// before the deployment is fully wired. Returns human-readable lines (each line is
    /// a single contract call) that <c>plan</c> prints after the bundle summary.
    /// </summary>
    /// <remarks>
    /// Currently surfaces:
    /// <list type="bullet">
    ///   <item><description><c>SequencerBond.RegisterSlasher(OptimisticChallenge)</c>: broken cycle workaround — bond can't depend on challenge at deploy time, so the operator wires it post-deploy.</description></item>
    ///   <item><description><c>ChainRegistry.SetGovernanceController(GovernanceController)</c>: doc.md §16.1 admission policy needs the GovernanceController hash wired before <c>RegisterChainPublic</c> works in mode 1/2.</description></item>
    ///   <item><description><c>VerifierRegistry.SetGovernanceController(GovernanceController)</c>: doc.md §16 council-veto needs the GovernanceController hash wired before <c>RegisterVerifierViaProposal</c> can consult <c>IsApprovedAndTimelocked</c>.</description></item>
    /// </list>
    /// </remarks>
    public static IEnumerable<string> PostDeployActions(DeployBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var bond = bundle.Invocations.FirstOrDefault(i => i.Name == "SequencerBond");
        var oc = bundle.Invocations.FirstOrDefault(i => i.Name == "OptimisticChallenge");
        var chainReg = bundle.Invocations.FirstOrDefault(i => i.Name == "ChainRegistry");
        var verifierReg = bundle.Invocations.FirstOrDefault(i => i.Name == "VerifierRegistry");
        var gc = bundle.Invocations.FirstOrDefault(i => i.Name == "GovernanceController");

        if (bond is not null && oc is not null)
        {
            yield return $"SequencerBond.RegisterSlasher({oc.Name})  # enable Phase-3 challenge slashing (broken cycle: bond→challenge dep is post-deploy)";
        }
        if (chainReg is not null && gc is not null)
        {
            yield return $"ChainRegistry.SetGovernanceController({gc.Name})  # enable §16.1 3-phase admission policy (RegisterChainPublic depends on this wiring)";
        }
        if (verifierReg is not null && gc is not null)
        {
            yield return $"VerifierRegistry.SetGovernanceController({gc.Name})  # enable §16 council-veto path (RegisterVerifierViaProposal depends on this wiring)";
        }
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
