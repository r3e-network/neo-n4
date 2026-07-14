using Neo.Json;

namespace Neo.Hub.Deploy;

/// <summary>
/// Generates the canonical default <see cref="DeployPlan"/> that matches the layout in
/// doc.md §3.2 and §13.1. The 15 core NeoHub contracts deploy in dependency order, plus
/// the contract-deployed ZK verifier router, its pinned SP1 Groth16 terminal verifier,
/// and the SettlementManager-bound <c>RestrictedExecutionFraudVerifier</c> executable v4
/// profile. The structural v1/v2/v3 verifier contracts remain audit aids and are deliberately
/// excluded from the production deployment bundle because they cannot authorize state changes.
/// The production external-bridge bundle includes the concrete immutable L2 payout adapter;
/// target native contracts remain L2 genesis configuration rather than L1 deploy steps.
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
                    "contracts/NeoHub.ChainRegistry/bin/sc/NeoHub.ChainRegistry.nef",
                    OwnerOnly()),

                Step("VerifierRegistry",
                    "contracts/NeoHub.VerifierRegistry/bin/sc/NeoHub.VerifierRegistry.nef",
                    OwnerOnly()),

                Step("ContractZkVerifier",
                    "contracts/NeoHub.ContractZkVerifier/bin/sc/NeoHub.ContractZkVerifier.nef",
                    OwnerOnly()),

                Step("Sp1Groth16Verifier",
                    "contracts/NeoHub.Sp1Groth16Verifier/bin/sc/NeoHub.Sp1Groth16Verifier.nef",
                    new JArray()),

                Step("TokenRegistry",
                    "contracts/NeoHub.TokenRegistry/bin/sc/NeoHub.TokenRegistry.nef",
                    OwnerOnly()),

                Step("DARegistry",
                    "contracts/NeoHub.DARegistry/bin/sc/NeoHub.DARegistry.nef",
                    OwnerAndDep("SettlementManager"),
                    "SettlementManager"),

                Step("DAValidator",
                    "contracts/NeoHub.DAValidator/bin/sc/NeoHub.DAValidator.nef",
                    OwnerAndDep("DARegistry"),
                    "DARegistry"),

                // EmergencyManager._deploy is (owner, emergencyCouncil, settlementManager)
                // and Pause() requires Runtime.CheckWitness(emergencyCouncil). The council
                // MUST be a real account (multisig/EOA) whose witness can actually be
                // produced — NOT a deployed-contract hash. A contract hash only satisfies
                // CheckWitness while that contract is in the live invocation chain, and
                // GovernanceController makes no outbound Contract.Call to forward a pause,
                // so wiring the council to its hash would make Pause() permanently
                // unreachable and the emergency-response system non-functional. The
                // operator substitutes EMERGENCY_COUNCIL_REPLACE_ME with the emergency
                // multisig signing-account hash before deploy; it must be a callable
                // witness, not a contract that can't sign.
                Step("EmergencyManager",
                    "contracts/NeoHub.EmergencyManager/bin/sc/NeoHub.EmergencyManager.nef",
                    new JArray { "OWNER_REPLACE_ME", "EMERGENCY_COUNCIL_REPLACE_ME", "$step:SettlementManager" },
                    "SettlementManager"),

                Step("GovernanceController",
                    "contracts/NeoHub.GovernanceController/bin/sc/NeoHub.GovernanceController.nef",
                    GovernanceControllerDeployData()),

                Step("MessageRouter",
                    "contracts/NeoHub.MessageRouter/bin/sc/NeoHub.MessageRouter.nef",
                    OwnerAndDep("SettlementManager"),
                    "SettlementManager"),

                Step("L1TxFilter",
                    "contracts/NeoHub.L1TxFilter/bin/sc/NeoHub.L1TxFilter.nef",
                    OwnerOnly()),

                Step("SettlementManager",
                    "contracts/NeoHub.SettlementManager/bin/sc/NeoHub.SettlementManager.nef",
                    OwnerAndDeps("ChainRegistry", "VerifierRegistry"),
                    "ChainRegistry", "VerifierRegistry"),

                Step("SharedBridge",
                    "contracts/NeoHub.SharedBridge/bin/sc/NeoHub.SharedBridge.nef",
                    OwnerAndDeps("SettlementManager", "TokenRegistry"),
                    "SettlementManager", "TokenRegistry"),

                Step("ForcedInclusion",
                    "contracts/NeoHub.ForcedInclusion/bin/sc/NeoHub.ForcedInclusion.nef",
                    OwnerAndDep("SettlementManager"),
                    "SettlementManager"),

                // SequencerBond holds the bonded GAS that backs each sequencer + pays out
                // slash awards. Initial slashers list is just GovernanceController so the
                // bond contract can deploy first without a cycle. After OptimisticChallenge
                // is up the operator calls SequencerBond.RegisterSlasher(optChallenge) to
                // enable Phase-3 challenge slashing.
                Step("SequencerBond",
                    "contracts/NeoHub.SequencerBond/bin/sc/NeoHub.SequencerBond.nef",
                    BondDeployData(),
                    "GovernanceController"),

                // SequencerRegistry tracks the live committee + exit windows. Bonds are
                // checked against SequencerBond before a register goes through.
                Step("SequencerRegistry",
                    "contracts/NeoHub.SequencerRegistry/bin/sc/NeoHub.SequencerRegistry.nef",
                    OwnerAndDep("SequencerBond"),
                    "SequencerBond"),

                // OptimisticChallenge is the Phase 3 contract. Its window timer + slash
                // payout requires both SettlementManager (for batch lookups) + SequencerBond
                // (to deduct on a successful challenge).
                Step("OptimisticChallenge",
                    "contracts/NeoHub.OptimisticChallenge/bin/sc/NeoHub.OptimisticChallenge.nef",
                    OwnerAndDeps("SettlementManager", "SequencerBond"),
                    "SettlementManager", "SequencerBond"),

                // Version 4 is permissionless only after deployment binds this verifier to the exact
                // SettlementManager and an operator-selected non-zero replay domain, and
                // OptimisticChallenge registers the exact chain/semantic/domain profile. Legacy
                // v1/v2/v3 payloads remain diagnostic-only and always fail closed in Challenge().
                Step("RestrictedExecutionFraudVerifier",
                    "contracts/NeoHub.RestrictedExecutionFraudVerifier/bin/sc/NeoHub.RestrictedExecutionFraudVerifier.nef",
                    new JArray
                    {
                        "$step:SettlementManager",
                        "FRAUD_REPLAY_DOMAIN_REPLACE_ME",
                    },
                    "SettlementManager"),

                // ─── External-bridge stack (doc.md §11.3) ────────────────
                // The cross-foreign-chain bridge contracts. Independent of
                // SettlementManager / SequencerBond — the verifier is a
                // committee, not a per-batch settlement gate. Order:
                //   MpcCommitteeVerifier (no deps; verifier impl)
                //   ExternalBridgeRegistry (no deps; routes externalChainId
                //                            → verifier hash)
                //   ExternalBridgeEscrow (depends on Registry; locks NEP-17
                //                         outbound + dispatches inbound)
                //   ExternalBridgeBond (no deps; committee bonding)
                // After deploy: operator runs RegisterCommittee on the
                // verifier + RegisterVerifier on the registry per supported
                // foreign chain (use neo-external-bridge committee-blob /
                // deploy-bundle to assemble the calls).

                Step("MpcCommitteeVerifier",
                    "contracts/NeoHub.MpcCommitteeVerifier/bin/sc/NeoHub.MpcCommitteeVerifier.nef",
                    OwnerOnly()),

                Step("ExternalBridgeRegistry",
                    "contracts/NeoHub.ExternalBridgeRegistry/bin/sc/NeoHub.ExternalBridgeRegistry.nef",
                    OwnerOnly()),

                // ExternalBridgeEscrow's _deploy takes (owner, registry, neoChainId).
                // The registry hash is resolved via $step:ExternalBridgeRegistry; the
                // target L2 chain id is an explicit operator value because the escrow
                // permanently binds inbound signatures to that domain at deployment.
                Step("ExternalBridgeEscrow",
                    "contracts/NeoHub.ExternalBridgeEscrow/bin/sc/NeoHub.ExternalBridgeEscrow.nef",
                    new JArray
                    {
                        "OWNER_REPLACE_ME",
                        "$step:ExternalBridgeRegistry",
                        "L2_CHAIN_ID_REPLACE_ME",
                    },
                    "ExternalBridgeRegistry"),

                Step("L2PayoutAdapter",
                    "contracts/NeoHub.L2PayoutAdapter/bin/sc/NeoHub.L2PayoutAdapter.nef",
                    new JArray
                    {
                        "OWNER_REPLACE_ME",
                        "$step:ExternalBridgeEscrow",
                        "L2_CHAIN_ID_REPLACE_ME",
                        "L2_PAYOUT_RELAY_ACCOUNT_REPLACE_ME",
                    },
                    "ExternalBridgeEscrow"),

                // ExternalBridgeBond mirrors SequencerBond — owner + bondAsset
                // (canonical GAS hash on the target L1; operator-substituted
                // exactly like SequencerBond's BOND_ASSET_REPLACE_ME). The
                // slasher set defaults to empty; the deploy bundle's
                // post-deploy hint reminds the operator to call
                // ExternalBridgeBond.RegisterSlasher(MpcCommitteeFraudVerifier)
                // once that contract is deployed below.
                Step("ExternalBridgeBond",
                    "contracts/NeoHub.ExternalBridgeBond/bin/sc/NeoHub.ExternalBridgeBond.nef",
                    new JArray
                    {
                        "OWNER_REPLACE_ME",
                        "BOND_ASSET_REPLACE_ME",
                    }),

                // MpcCommitteeFraudVerifier (Phase C) — slashes equivocating
                // committee members. Depends on the verifier contract (to
                // read committee + per-signer member binding) AND the bond
                // contract (to call Slash). Deploy AFTER both. The operator
                // also has to:
                //   1. Call ExternalBridgeBond.RegisterSlasher(this) so
                //      Slash calls from this contract are accepted.
                //   2. Use MpcCommitteeVerifier.RegisterCommitteeWithMembers
                //      (NOT plain RegisterCommittee) when wiring foreign
                //      chains — the binding is required to identify which
                //      member to slash.
                Step("MpcCommitteeFraudVerifier",
                    "contracts/NeoHub.MpcCommitteeFraudVerifier/bin/sc/NeoHub.MpcCommitteeFraudVerifier.nef",
                    new JArray
                    {
                        "OWNER_REPLACE_ME",
                        "$step:MpcCommitteeVerifier",
                        "$step:ExternalBridgeBond",
                    },
                    "MpcCommitteeVerifier", "ExternalBridgeBond"),
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

    /// <summary>
    /// Reject an optimistic deployment plan unless it contains the exact executable v4
    /// verifier shape supported by the production runner.
    /// </summary>
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
    ///   <item><description><c>ExternalBridgeEscrow</c> governance, versioned payout-route, direct-liquidity, and irreversible-lock wiring required to close the verified inbound asset path.</description></item>
    ///   <item><description>Fraud-verifier wiring only for the exact chain-scoped restricted executable v4 profile. Structural v1/v2/v3 contracts are reported as advisory-only and are never registered for state-changing challenges.</description></item>
    /// </list>
    /// </remarks>
    public static IEnumerable<string> PostDeployActions(DeployBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        RequireExecutableOptimisticFraudProfile(bundle);
        var bond = bundle.Invocations.FirstOrDefault(i => i.Name == "SequencerBond");
        var oc = bundle.Invocations.FirstOrDefault(i => i.Name == "OptimisticChallenge");
        var chainReg = bundle.Invocations.FirstOrDefault(i => i.Name == "ChainRegistry");
        var verifierReg = bundle.Invocations.FirstOrDefault(i => i.Name == "VerifierRegistry");
        var contractZkVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "ContractZkVerifier");
        var sp1Groth16Verifier = bundle.Invocations.FirstOrDefault(i => i.Name == "Sp1Groth16Verifier");
        var gc = bundle.Invocations.FirstOrDefault(i => i.Name == "GovernanceController");
        var sm = bundle.Invocations.FirstOrDefault(i => i.Name == "SettlementManager");
        var forcedInclusion = bundle.Invocations.FirstOrDefault(i => i.Name == "ForcedInclusion");
        var sharedBridge = bundle.Invocations.FirstOrDefault(i => i.Name == "SharedBridge");
        var emergencyManager = bundle.Invocations.FirstOrDefault(i => i.Name == "EmergencyManager");
        var daRegistry = bundle.Invocations.FirstOrDefault(i => i.Name == "DARegistry");
        var daValidator = bundle.Invocations.FirstOrDefault(i => i.Name == "DAValidator");
        var messageRouter = bundle.Invocations.FirstOrDefault(i => i.Name == "MessageRouter");
        var l1TxFilter = bundle.Invocations.FirstOrDefault(i => i.Name == "L1TxFilter");
        var rexFraudVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "RestrictedExecutionFraudVerifier");
        var mpcVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "MpcCommitteeVerifier");
        var extRegistry = bundle.Invocations.FirstOrDefault(i => i.Name == "ExternalBridgeRegistry");
        var extEscrow = bundle.Invocations.FirstOrDefault(i => i.Name == "ExternalBridgeEscrow");
        var l2PayoutAdapter = bundle.Invocations.FirstOrDefault(i => i.Name == "L2PayoutAdapter");
        var extBond = bundle.Invocations.FirstOrDefault(i => i.Name == "ExternalBridgeBond");
        var fraudVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "MpcCommitteeFraudVerifier");

        if (bond is not null && oc is not null)
        {
            yield return $"SequencerBond.RegisterSlasher({oc.Name})  # enable Phase-3 challenge slashing (broken cycle: bond→challenge dep is post-deploy)";
        }
        if (bond is not null && forcedInclusion is not null)
        {
            yield return $"{bond.Name}.RegisterSlasher({forcedInclusion.Name})  # enable §15.4 forced-inclusion censorship slashing";
        }
        if (chainReg is not null && forcedInclusion is not null)
        {
            yield return $"{chainReg.Name}.RegisterPauser({forcedInclusion.Name})  # let proven censorship pause the affected L2 chain before more batches finalize";
            yield return $"{forcedInclusion.Name}.SetChainRegistry({chainReg.Name})  # wire §15.4 censorship reports to ChainRegistry.PauseChain";
        }
        if (forcedInclusion is not null && bond is not null)
        {
            yield return $"{forcedInclusion.Name}.SetSequencerBond({bond.Name})  # wire §15.4 censorship reports to SequencerBond.Slash";
            yield return $"{forcedInclusion.Name}.SetCensorshipSlashAmount(1000000)  # production default: slash 1.0 GAS per overdue forced-inclusion entry";
            yield return $"{forcedInclusion.Name}.SetGasToken(GAS_CONTRACT_HASH)  # production requires a real NEP-17 GAS token for forced-inclusion spam control";
            yield return $"{forcedInclusion.Name}.SetFeeRecipient(FEE_RECIPIENT)  # production requires a non-zero accountable fee recipient";
            yield return $"{forcedInclusion.Name}.SetFee(100000)  # production default: charge 0.001 GAS per forced-inclusion entry after token + recipient are wired";
        }
        if (sharedBridge is not null && emergencyManager is not null)
        {
            yield return $"{sharedBridge.Name}.SetEmergencyManager({emergencyManager.Name})  # enable paused-only emergency withdrawal payouts";
        }
        if (chainReg is not null && gc is not null)
        {
            yield return $"ChainRegistry.SetGovernanceController({gc.Name})  # enable §16.1 3-phase admission policy (RegisterChainPublic depends on this wiring)";
        }
        if (verifierReg is not null && gc is not null)
        {
            yield return $"VerifierRegistry.SetGovernanceController({gc.Name})  # enable §16 council-veto path (RegisterVerifierViaProposal depends on this wiring)";
        }
        if (contractZkVerifier is not null && sp1Groth16Verifier is not null)
        {
            yield return $"{contractZkVerifier.Name}.RegisterVerificationKey(ProofSystem.Sp1=1, PROGRAM_VKEY_REPLACE_ME, allowed=true)  # allow the audited SP1 program vkey emitted by the production prover";
            yield return $"{contractZkVerifier.Name}.RegisterProofVerifier(ProofSystem.Sp1=1, {sp1Groth16Verifier.Name}, allowed=true)  # route SP1 proof math to the pinned in-repo Groth16 verifier";
            yield return $"{contractZkVerifier.Name}.DisableEnvelopeOnlyPermanently(ProofSystem.Sp1=1)  # irreversible production gate: SP1 batches can never fall back to envelope-only acceptance";
            yield return $"{contractZkVerifier.Name}.LockProofSystemConfiguration(ProofSystem.Sp1=1, PROGRAM_VKEY_REPLACE_ME)  # freeze the exact SP1 vkey and terminal verifier; upgrades deploy a new versioned router";
        }
        if (verifierReg is not null && contractZkVerifier is not null)
        {
            yield return $"{verifierReg.Name}.RegisterVerifier(ProofType.Zk=3, {contractZkVerifier.Name})  # route ZK settlement commitments to the contract-deployed verifier router";
            yield return $"{verifierReg.Name}.LockGovernance()  # irreversible production gate: freeze the GovernanceController and disable direct owner verifier replacement; future routes require an exact payload-bound timelocked proposal";
        }
        if (rexFraudVerifier is not null && oc is not null)
        {
            yield return $"{oc.Name}.RegisterPermissionlessFraudProfile(L2_CHAIN_ID_REPLACE_ME, {rexFraudVerifier.Name}, {rexFraudVerifier.Name}.GetExecutorSemanticId(), FRAUD_REPLAY_DOMAIN_REPLACE_ME)  # atomically approve and bind the only supported state-changing executable v4 profile";
            yield return "# Security boundary: v1/v2/v3 fraud payloads are advisory only and fail closed for state changes even with governance or owner witness; general NeoVM optimistic execution remains unsupported.";
        }

        // ─── External-bridge wiring ──────────────────────────────────────
        // The MPC verifier + registry both have governance-mediated
        // upgrade paths (RegisterCommitteeViaProposal /
        // UpgradeVerifierViaProposal). Same wiring shape as
        // VerifierRegistry — point them at the GovernanceController so
        // those proposal-gated calls can consult IsApprovedAndTimelocked.
        if (mpcVerifier is not null && gc is not null)
        {
            yield return $"{mpcVerifier.Name}.SetGovernanceController({gc.Name})  # enable governance-mediated committee upgrade (RegisterCommitteeViaProposal depends on this)";
        }
        if (extRegistry is not null && gc is not null)
        {
            yield return $"{extRegistry.Name}.SetGovernanceController({gc.Name})  # enable governance-mediated verifier upgrade (UpgradeVerifierViaProposal depends on this)";
        }
        if (extEscrow is not null && gc is not null)
        {
            yield return $"{extEscrow.Name}.SetGovernanceController({gc.Name})  # enable exact payload-bound, timelocked registry and payout-route upgrades before the irreversible production lock";
        }
        if (mpcVerifier is not null && extRegistry is not null)
        {
            // Informational — operators must wire each supported foreign
            // chain at deploy time using the neo-external-bridge CLI to
            // generate the committee blob + the dual-side deploy bundle.
            // Without this step the registry has no verifier registered
            // and ExternalBridgeEscrow.Receive reverts with "no verifier
            // registered for externalChainId" on first inbound message.
            yield return $"# Per supported foreign chain (e.g. Eth=0xE0000001, Sepolia=0xE0000002): run `neo-external-bridge committee-blob` + `neo-external-bridge deploy-bundle` to register the committee on {mpcVerifier.Name} and bind it to {extRegistry.Name} via RegisterVerifier(externalChainId, mpcVerifier.Hash, bridgeKindMpc=1).";
        }
        if (extEscrow is not null && l2PayoutAdapter is not null)
        {
            yield return $"{extEscrow.Name}.SetAssetRoute(EXTERNAL_CHAIN_ID, FOREIGN_ASSET, NEO_ASSET, {l2PayoutAdapter.Name})  # bind each foreign asset to the concrete immutable payout-v1 queue after verifying payoutVersion()==1, UpdateCounter==0, adapter neoChainId=non-zero L2_CHAIN_ID_REPLACE_ME; use payoutAdapter=0 only for neoChainId=0 direct-release routes; target credit is enqueue/confirm/ack, not a synchronous cross-chain commit";
            yield return $"# Neo L1 direct-release routes only: call {extEscrow.Name}.FundLiquidity(EXTERNAL_CHAIN_ID, NEO_ASSET, AMOUNT) before accepting inbound value; non-zero L2 adapter routes own their target-chain credit/mint accounting.";
        }
        if (extEscrow is not null && gc is not null)
        {
            yield return $"{extEscrow.Name}.LockGovernance()  # irreversible production gate: disable direct owner registry/route replacement; future changes require SetRegistryViaProposal or ConfigureAssetRouteViaProposal with exact bound bytes";
        }
        if (fraudVerifier is not null && extBond is not null)
        {
            // Phase-C wiring step (replaces the deferred reminder from
            // earlier iterations). The fraud verifier IS a contract now —
            // operator MUST register it as a slasher or proven equivocations
            // can't actually take the bond.
            yield return $"{extBond.Name}.RegisterSlasher({fraudVerifier.Name})  # Phase-C: lets MpcCommitteeFraudVerifier.Slash() take the equivocator's bond after proving equivocation cryptographically";
        }
        else if (extEscrow is not null && extBond is not null)
        {
            // No fraud verifier in the bundle — bond is owner-only-slashable
            // (devnet path). Surface so the operator knows the security model.
            yield return $"# Note: {extBond.Name} is owner-only-slashable in this bundle (no MpcCommitteeFraudVerifier). For trust-minimized slashing of equivocating committee members, deploy MpcCommitteeFraudVerifier and call {extBond.Name}.RegisterSlasher(MpcCommitteeFraudVerifier).";
        }
        if (mpcVerifier is not null && fraudVerifier is not null)
        {
            // The fraud verifier reads per-signer member bindings to know
            // whose bond to slash. Without RegisterCommitteeWithMembers, it
            // refuses to slash even on valid equivocation proof.
            yield return $"# Per supported foreign chain: use {mpcVerifier.Name}.RegisterCommitteeWithMembers (NOT plain RegisterCommittee) so {fraudVerifier.Name} can identify which bond holder to slash on proven equivocation.";
        }
        if (sm is not null && daRegistry is not null)
        {
            yield return $"{sm.Name}.SetDARegistry({daRegistry.Name})  # record every accepted batch's DA commitment + active DAMode";
        }
        if (sm is not null && daValidator is not null)
        {
            yield return $"{sm.Name}.SetDAValidator({daValidator.Name})  # enforce DA validation before FinalizeBatch, including DAC attestation checks";
        }
        if (sm is not null && oc is not null)
        {
            yield return $"{sm.Name}.SetOptimisticChallenge({oc.Name})  # complete the SettlementManager -> OptimisticChallenge cycle after both contracts exist";
        }
        if (sm is not null && messageRouter is not null)
        {
            yield return $"{sm.Name}.SetMessageRouter({messageRouter.Name})  # make Gateway publication validate finalized L1 constituents and enter MessageRouter with the canonical contract witness atomically";
        }
        if (messageRouter is not null && l1TxFilter is not null)
        {
            yield return $"# Per L2 chain: call {messageRouter.Name}.SetL1TxFilter(<chainId>, {l1TxFilter.Name}) to enable sender/receiver/message-type filtering before L1->L2 enqueue.";
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

    private static JArray GovernanceControllerDeployData()
    {
        return new JArray
        {
            "OWNER_REPLACE_ME",
            new JArray
            {
                "GOVERNANCE_COUNCIL_MEMBER_REPLACE_ME",
            },
            1,
            3600,
        };
    }
}
