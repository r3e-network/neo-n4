using System;
using System.Collections.Generic;
using System.Linq;
using Neo.Json;

namespace Neo.Hub.Deploy;

/// <summary>
/// Generates the canonical default <see cref="DeployPlan"/> that matches the Lean 4-Pillar
/// architecture (doc.md §3.2, §8, §11, §16):
/// 1. ZkVerifier: Unified Groth16/BN254 + ZK validity routing.
/// 2. GovernanceController: Council multisig, timelock, emergency pause, sequencer staking.
/// 3. RollupHub: Unified chain registry, batch settlement, and DA verification.
/// 4. SharedBridge: Unified asset escrow, token registry, and cross-chain message router.
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
                Step("Sp1Groth16Verifier",
                    "contracts/NeoHub.Sp1Groth16Verifier/bin/sc/NeoHub.Sp1Groth16Verifier.nef",
                    OwnerOnly()),

                Step("ZkVerifier",
                    "contracts/NeoHub.ZkVerifier/bin/sc/NeoHub.ZkVerifier.nef",
                    OwnerOnly()),

                Step("GovernanceController",
                    "contracts/NeoHub.GovernanceController/bin/sc/NeoHub.GovernanceController.nef",
                    GovernanceControllerDeployData()),

                Step("RollupHub",
                    "contracts/NeoHub.RollupHub/bin/sc/NeoHub.RollupHub.nef",
                    OwnerAndDep("ZkVerifier"),
                    "ZkVerifier"),

                Step("SharedBridge",
                    "contracts/NeoHub.SharedBridge/bin/sc/NeoHub.SharedBridge.nef",
                    OwnerAndDep("RollupHub"),
                    "RollupHub")
            }
        };
    }

    internal static void RequireExecutableOptimisticFraudProfile(DeployPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var optimisticSteps = plan.Steps
            .Where(step => step.Name == "OptimisticChallenge")
            .ToArray();
        if (optimisticSteps.Length == 0) return;
        if (optimisticSteps.Length != 1)
            throw new InvalidOperationException(
                "optimistic deployment requires exactly one OptimisticChallenge step");

        var restrictedSteps = plan.Steps
            .Where(step => step.Name == "RestrictedExecutionFraudVerifier")
            .ToArray();
        if (restrictedSteps.Length != 1)
            throw UnsupportedOptimisticPlan();

        var restricted = restrictedSteps[0];
        if (!restricted.DependsOn.Contains("SettlementManager", StringComparer.Ordinal)
            || restricted.DeployData.Count != 2
            || restricted.DeployData[0] is not JString settlementManager
            || settlementManager.AsString() != "$step:SettlementManager"
            || !IsConfiguredReplayDomain(restricted.DeployData[1]))
        {
            throw UnsupportedOptimisticPlan();
        }
    }

    private static void RequireExecutableOptimisticFraudProfile(DeployBundle bundle)
    {
        var optimistic = bundle.Invocations
            .SingleOrDefault(invocation => invocation.Name == "OptimisticChallenge");
        if (optimistic is null) return;

        var restricted = bundle.Invocations
            .SingleOrDefault(invocation => invocation.Name == "RestrictedExecutionFraudVerifier");
        if (restricted is null
            || optimistic.ResolvedDeployData.Count < 2
            || restricted.ResolvedDeployData.Count != 2
            || optimistic.ResolvedDeployData[1] is not JString optimisticSettlementManager
            || restricted.ResolvedDeployData[0] is not JString restrictedSettlementManager
            || optimisticSettlementManager.AsString() != restrictedSettlementManager.AsString()
            || !IsConfiguredReplayDomain(restricted.ResolvedDeployData[1]))
        {
            throw UnsupportedOptimisticPlan();
        }
    }

    private static bool IsConfiguredReplayDomain(JToken? token)
    {
        if (token is not JString text) return false;
        var value = text.AsString();
        if (value == "FRAUD_REPLAY_DOMAIN_REPLACE_ME") return true;
        return UInt256.TryParse(value, out var replayDomain)
            && replayDomain != UInt256.Zero;
    }

    private static InvalidOperationException UnsupportedOptimisticPlan()
    {
        return new InvalidOperationException(
            "unsupported optimistic deployment: state-changing challenges require the exact executable v4 " +
            "RestrictedExecutionFraudVerifier configuration [SettlementManager, non-zero replayDomain]; " +
            "v1/v2/v3 structural evidence remains advisory only and fails closed even with governance or owner witness");
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

    public static IEnumerable<string> PostDeployActions(DeployBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        RequireExecutableOptimisticFraudProfile(bundle);
        var rollupHub = bundle.Invocations.FirstOrDefault(i => i.Name == "RollupHub");
        var zkVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "ZkVerifier");
        var gc = bundle.Invocations.FirstOrDefault(i => i.Name == "GovernanceController");
        var sharedBridge = bundle.Invocations.FirstOrDefault(i => i.Name == "SharedBridge");

        if (rollupHub is not null && gc is not null)
        {
            yield return $"{rollupHub.Name}.SetGovernanceController({gc.Name})  # bind RollupHub to GovernanceController";
        }
        if (sharedBridge is not null && rollupHub is not null)
        {
            yield return $"{sharedBridge.Name}.SetSettlementManager({rollupHub.Name})  # bind SharedBridge to RollupHub";
        }
        if (sharedBridge is not null && gc is not null)
        {
            yield return $"{sharedBridge.Name}.SetEmergencyManager({gc.Name})  # bind SharedBridge to GovernanceController emergency pause";
        }
        if (zkVerifier is not null)
        {
            var dedicated = bundle.Invocations.FirstOrDefault(i => i.Name == "Sp1Groth16Verifier");
            if (dedicated is not null)
                yield return $"{zkVerifier.Name}.RegisterProofVerifier(ProofSystem.Sp1=1, {dedicated.Name}, allowed=true)  # bind the real SP1 Groth16 verifier";
            yield return $"{zkVerifier.Name}.RegisterVerificationKey(ProofSystem.Sp1=1, <SP1_PROGRAM_VK_FROM_RELEASE_MANIFEST>, allowed=true)  # supply the real VK from the audited SP1 release manifest";
            yield return $"{zkVerifier.Name}.DisableEnvelopeOnlyPermanently(ProofSystem.Sp1=1)  # irreversible production gate: SP1 batches must verify real math";
        }

        var chainReg = bundle.Invocations.FirstOrDefault(i => i.Name == "ChainRegistry");
        if (chainReg is not null && gc is not null)
        {
            yield return $"ChainRegistry.SetGovernanceController({gc.Name})  # enable §16.1 3-phase admission policy (RegisterChainPublic depends on this wiring)";
            yield return $"{chainReg.Name}.LockGovernance()  # irreversible production gate: freeze the ChainRegistry controller";
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

    private static JArray GovernanceControllerDeployData()
    {
        return new JArray
        {
            "OWNER_REPLACE_ME",
            new JArray
            {
                "GOVERNANCE_COUNCIL_REPLACE_ME",
            },
            "GOVERNANCE_THRESHOLD_REPLACE_ME",
            3600,
        };
    }
}
