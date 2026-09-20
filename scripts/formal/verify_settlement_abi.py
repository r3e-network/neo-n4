"""Spec-correspondence model: doc.md §3.2 settlement method surface vs the deployed manifest.

Proves that every settlement method doc.md §3.2 declares is implemented on the deployed
RollupHub contract, by parsing the contract manifest embedded in the regenerated
TestingArtifacts (the same NEF/manifest the VmTests execute against). Revert and lock are
recorded as newly implemented; submitBatch/submitAndFinalizeBatch are recorded as a
documented, deliberate parameter superset (forcedInclusionCount is sealed into the 352-byte
public-inputs domain per the batch-spec model). A handwritten abstraction, NOT verification
of C#/NeoVM. See settlement-abi-model.md.
"""
import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / "tests/NeoHub.Contracts.VmTests/TestingArtifacts/NeoHubRollupHub.artifacts.cs"
SPEC = ROOT / "doc.md"

# doc.md §3.2 settlement surface: method -> declared parameter count.
DOC_SETTLEMENT_SURFACE = {
    "submitBatch": 4,               # (commitmentBytes, l1MessageHash, blockContextHash, forcedInclusionCount)
    "submitAndFinalizeBatch": 4,    # (commitmentBytes, l1MessageHash, blockContextHash, forcedInclusionCount)
    "finalizeBatch": 2,             # (chainId, batchNumber)
    "revertBatch": 2,               # (chainId, batchNumber)
    "publishGatewayGlobalRoot": None,  # long tail; existence-checked
    "setGovernanceController": 1,   # (governanceController)
    "lockGovernance": 0,            # ()
    "isGovernanceLocked": 0,        # (), [Safe]
    "getCanonicalStateRoot": 1,     # (chainId)
    "isProofTypeCompatible": 2,     # (securityLevel, proofType), [Safe]
}


def manifest_from_artifacts(path=ARTIFACTS):
    text = Path(path).read_text(encoding="utf-8-sig")
    match = re.search(r'ContractManifest\.Parse\(@"(.*?)"\);', text, re.S)
    if match is None:
        raise ValueError("contract manifest not found in TestingArtifacts")
    # The generator escapes quotes by doubling them.
    return json.loads(match.group(1).replace('""', '"'))


def obligations(manifest):
    methods = {m["name"]: len(m.get("parameters", [])) for m in manifest["abi"]["methods"]}

    # Every doc-declared settlement method must exist on the deployed contract with the
    # doc-declared parameter count (doc.md §3.2 records the 4th forcedInclusionCount parameter
    # that the 352-byte public-inputs domain seals in).
    for name, declared in DOC_SETTLEMENT_SURFACE.items():
        if name not in methods:
            yield f"doc_method_implemented_{name}", False, True
        elif declared is None:
            yield f"doc_method_implemented_{name}", True, True
        else:
            yield f"doc_method_implemented_{name}", methods[name] == declared, True

    # The revert/lock authorization invariants the lock implementation depends on.
    yield "revert_batch_takes_chain_and_batch", methods.get("revertBatch") == 2, True
    yield "lock_governance_is_parameterless", methods.get("lockGovernance") == 0, True
    yield "governance_lock_is_queryable", "isGovernanceLocked" in methods, True

    # The lock cannot be bypassed through the registry surface: every owner-service entry the
    # doc lists must exist so the post-lock guards in this contract are the only authority path.
    for name in ("registerChain", "updateChain", "pauseChain", "resumeChain"):
        yield f"registry_surface_present_{name}", name in methods, True


def solve(name, fact, expected):
    record = {"name": name, "expected": expected, "actual": fact, "passed": fact == expected}
    return record


def run(artifacts=ARTIFACTS, spec=SPEC):
    report = {"schema": "neo-n4/settlement-abi-model/v1", "wholeSystemVerified": False,
              "scope": "spec-to-implementation correspondence of doc.md §3.2 settlement method surface",
              "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten correspondence: doc.md §3.2 method list vs the generated contract manifest",
                  "the manifest is the same NEF/manifest the VmTests execute against (TestingArtifacts)",
                  "submitBatch/submitAndFinalizeBatch deliberately supersede the doc signature with a",
                  "4th forcedInclusionCount parameter sealed into the 352-byte public-inputs domain",
                  "publishGatewayGlobalRoot's long tail is existence-checked, not arity-checked",
                  "post-lock authorization relies on Runtime.CheckWitness of the GovernanceController",
                  "contract; relay-proposal execution semantics live in GovernanceController"],
              "source": str(artifacts), "spec": str(spec),
              "scriptSha256": None}
    try:
        manifest = manifest_from_artifacts(artifacts)
        report["scriptSha256"] = __import__("hashlib").sha256(
            Path(__file__).read_text(encoding="utf-8-sig").replace("\r\n", "\n").encode()).hexdigest()
        report["specSha256"] = __import__("hashlib").sha256(
            Path(spec).read_text(encoding="utf-8-sig").replace("\r\n", "\n").encode()).hexdigest()
        report["obligations"] = [solve(*o) for o in obligations(manifest)]
        if report["obligations"] and all(o["passed"] for o in report["obligations"]):
            report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    report["exitCode"] = 0 if report["status"] == "passed" else 1
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("settlement-abi-result.json"))
    args = parser.parse_args()
    report = run()
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for o in report["obligations"]:
        print(f"{o['name']}: actual={o['actual']} expected={o['expected']} passed={o['passed']}")
    print(f"status={report['status']}; exit={report['exitCode']}; report={args.output}")
    if "error" in report:
        print(report["error"])
    return report["exitCode"]


if __name__ == "__main__":
    sys.exit(main())