using Neo.Json;

namespace Neo.Hub.Deploy;

/// <summary>
/// Generates the canonical default <see cref="DeployPlan"/> that matches the layout in
/// doc.md §3.2 and §13.1. The 15 core NeoHub contracts deploy in dependency order, plus
/// two stateless fraud-verifier reference contracts (<c>GovernanceFraudVerifier</c> for
/// v1/v2 structural verification and <c>RestrictedExecutionFraudVerifier</c> for
/// trustless v3 storage-proof verification). L2 native contracts are listed but
/// commented as "deploy on the L2", not the L1.
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

                Step("NativeZkVerifier",
                    "contracts/NeoHub.NativeZkVerifier/bin/sc/NeoHub.NativeZkVerifier.nef",
                    OwnerOnly()),

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

                Step("EmergencyManager",
                    "contracts/NeoHub.EmergencyManager/bin/sc/NeoHub.EmergencyManager.nef",
                    OwnerAndDeps("GovernanceController", "SettlementManager"),
                    "GovernanceController", "SettlementManager"),

                Step("GovernanceController",
                    "contracts/NeoHub.GovernanceController/bin/sc/NeoHub.GovernanceController.nef",
                    OwnerOnly()),

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

                // GovernanceFraudVerifier is the structural-verifier reference contract
                // operators in governance-arbitration mode pass as the fraudVerifier
                // argument to OptimisticChallenge.Challenge. It has no deploy-time deps —
                // verifies a static wire format (v1/v2). Skip this step entirely if the
                // chain ships its own (re-execution-capable) fraud verifier.
                Step("GovernanceFraudVerifier",
                    "contracts/NeoHub.GovernanceFraudVerifier/bin/sc/NeoHub.GovernanceFraudVerifier.nef",
                    new JArray()),

                // RestrictedExecutionFraudVerifier is the trustless v3 verifier — re-derives
                // pre/post Merkle roots from each storage proof's leaf-hash + siblings +
                // leafIndex and matches against the v1 header roots. Same stateless
                // shape as GovernanceFraudVerifier (no deploy args, no deps). Operators
                // running v3 fraud-proofs pass this contract's hash as the fraudVerifier
                // argument; operators running v1/v2 governance-arbitration use
                // GovernanceFraudVerifier instead. Both can be deployed simultaneously
                // — the OptimisticChallenge.Challenge caller picks which to invoke.
                Step("RestrictedExecutionFraudVerifier",
                    "contracts/NeoHub.RestrictedExecutionFraudVerifier/bin/sc/NeoHub.RestrictedExecutionFraudVerifier.nef",
                    new JArray()),

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

                // ExternalBridgeEscrow's _deploy takes (owner, registry).
                // The registry hash is resolved via $step:ExternalBridgeRegistry.
                Step("ExternalBridgeEscrow",
                    "contracts/NeoHub.ExternalBridgeEscrow/bin/sc/NeoHub.ExternalBridgeEscrow.nef",
                    OwnerAndDep("ExternalBridgeRegistry"),
                    "ExternalBridgeRegistry"),

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
    ///   <item><description>Informational note(s) for each fraud-verifier reference contract present in the bundle, so operators know which hash to pass as the <c>fraudVerifier</c> argument to <c>OptimisticChallenge.Challenge</c>: <c>GovernanceFraudVerifier</c> (v1/v2 governance arbitration) or <c>RestrictedExecutionFraudVerifier</c> (v3 trustless on-chain re-derivation).</description></item>
    /// </list>
    /// </remarks>
    public static IEnumerable<string> PostDeployActions(DeployBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var bond = bundle.Invocations.FirstOrDefault(i => i.Name == "SequencerBond");
        var oc = bundle.Invocations.FirstOrDefault(i => i.Name == "OptimisticChallenge");
        var chainReg = bundle.Invocations.FirstOrDefault(i => i.Name == "ChainRegistry");
        var verifierReg = bundle.Invocations.FirstOrDefault(i => i.Name == "VerifierRegistry");
        var nativeZkVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "NativeZkVerifier");
        var gc = bundle.Invocations.FirstOrDefault(i => i.Name == "GovernanceController");
        var sm = bundle.Invocations.FirstOrDefault(i => i.Name == "SettlementManager");
        var daRegistry = bundle.Invocations.FirstOrDefault(i => i.Name == "DARegistry");
        var daValidator = bundle.Invocations.FirstOrDefault(i => i.Name == "DAValidator");
        var messageRouter = bundle.Invocations.FirstOrDefault(i => i.Name == "MessageRouter");
        var l1TxFilter = bundle.Invocations.FirstOrDefault(i => i.Name == "L1TxFilter");
        var govFraudVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "GovernanceFraudVerifier");
        var rexFraudVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "RestrictedExecutionFraudVerifier");
        var mpcVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "MpcCommitteeVerifier");
        var extRegistry = bundle.Invocations.FirstOrDefault(i => i.Name == "ExternalBridgeRegistry");
        var extEscrow = bundle.Invocations.FirstOrDefault(i => i.Name == "ExternalBridgeEscrow");
        var extBond = bundle.Invocations.FirstOrDefault(i => i.Name == "ExternalBridgeBond");
        var fraudVerifier = bundle.Invocations.FirstOrDefault(i => i.Name == "MpcCommitteeFraudVerifier");

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
        if (nativeZkVerifier is not null)
        {
            yield return $"{nativeZkVerifier.Name}.SetNativeAccelerator(L1_NATIVE_ZK_VERIFIER_HASH)  # required for ProofType.Zk: heavy SNARK/STARK math runs through the L1 native accelerator, not ordinary NeoHub contract bytecode";
            yield return $"{nativeZkVerifier.Name}.RegisterVerificationKey(proofSystem, vkId, allowed=true)  # allow each production RISC-V/SP1/Risc0/Halo2/Axiom verification key before accepting ZK batches";
        }
        if (verifierReg is not null && nativeZkVerifier is not null)
        {
            yield return $"{verifierReg.Name}.RegisterVerifier(ProofType.Zk=3, {nativeZkVerifier.Name})  # route ZK settlement commitments to NativeZkVerifier; use RegisterVerifierViaProposal after governance is live";
        }
        if (govFraudVerifier is not null && oc is not null)
        {
            // Informational only — no on-chain wiring step. Challengers pass
            // GovernanceFraudVerifier.Hash as the fraudVerifier argument when calling
            // OptimisticChallenge.Challenge. Surface so operators know which hash to
            // hand to challengers (or to embed in their CLI / front-end).
            yield return $"# Note: for v1/v2 fraud proofs (governance arbitration), pass {govFraudVerifier.Name}.Hash as the `fraudVerifier` argument to OptimisticChallenge.Challenge.";
        }
        if (rexFraudVerifier is not null && oc is not null)
        {
            // Same informational shape as GovernanceFraudVerifier above. The two
            // verifiers are peers — operators pick which to invoke per-challenge
            // based on whether they're filing a v1/v2 (governance) or v3 (trustless)
            // FraudProofPayload. Both can be deployed simultaneously since
            // OptimisticChallenge.Challenge takes the verifier hash as a parameter.
            yield return $"# Note: for v3 fraud proofs (trustless storage-proof re-derivation), pass {rexFraudVerifier.Name}.Hash as the `fraudVerifier` argument to OptimisticChallenge.Challenge.";
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
}
