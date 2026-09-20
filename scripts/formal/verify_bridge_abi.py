"""Spec-correspondence model: doc.md §10/§11 bridge method surface vs the deployed manifests.

doc.md §10/§11 declares one logical bridge method list; the implementation splits it across
the L1-side SharedBridge contract and the L2-side native bridge (external/neo
L2NativeContracts.cs). This model parses the deployed SharedBridge manifest from
TestingArtifacts, anchors the L2-side methods by source anchor, and pins the full
correspondence table — including the documented renames (isMessageConsumed →
isL2ToL1MessageConsumed), the merge (routeMessage + enqueueL1ToL2Message → sendMessage), and
the documented superset (finalizeWithdrawal's 4 doc params expanded to 9..12 with the V5
leaf-hash binding). A handwritten abstraction, NOT verification of C#/NeoVM.
See bridge-abi-model.md.
"""
import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
SHARED_BRIDGE_ARTIFACTS = ROOT / "tests/NeoHub.Contracts.VmTests/TestingArtifacts/NeoHubSharedBridge.artifacts.cs"
SPEC = ROOT / "doc.md"
L2_BRIDGE = ROOT / "external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs"

# doc.md §10/§11 declared method -> (implemented location, implemented name, implemented arity
# or None for long-tail existence check). Locations: "shared-bridge" (parsed from the deployed
# manifest) or "l2-native-bridge" (source-anchored).
BRIDGE_SURFACE = {
    "registerMapping": ("shared-bridge", "registerMapping", 1),
    "getL2Asset": ("both", "getL2Asset", 2),
    "getL1Asset": ("l2-native-bridge", "GetL1Asset", None),
    "deposit": ("shared-bridge", "deposit", 4),
    "finalizeWithdrawal": ("shared-bridge", "finalizeWithdrawal", 9),  # documented superset; At/WithProof/Emergency variants 10/12/12
    "publishMessageRoots": ("shared-bridge", "publishMessageRoots", 4),
    "sendMessage": ("shared-bridge", "sendMessage", 3),  # merged: routeMessage + enqueueL1ToL2Message
    "isL2ToL1MessageConsumed": ("shared-bridge", "isL2ToL1MessageConsumed", 1),  # renamed: isMessageConsumed
}
# The exact documented renames/merges, pinned so the drift history is not lost.
RENAME_MERGE_TABLE = {
    "isMessageConsumed": "isL2ToL1MessageConsumed",
    "routeMessage + enqueueL1ToL2Message": "sendMessage",
}


def manifest_from_artifacts(path=SHARED_BRIDGE_ARTIFACTS):
    text = Path(path).read_text(encoding="utf-8-sig")
    match = re.search(r'ContractManifest\.Parse\(@"(.*?)"\);', text, re.S)
    if match is None:
        raise ValueError("SharedBridge manifest not found in TestingArtifacts")
    return json.loads(match.group(1).replace('""', '"'))


def l2_bridge_anchors_present(path=L2_BRIDGE):
    text = Path(path).read_text(encoding="utf-8-sig")
    anchors = [
        "public UInt160 GetL1Asset(IReadOnlyStore snapshot, UInt160 l2Asset)",
        "public UInt160 GetL2Asset(IReadOnlyStore snapshot, UInt160 l1Asset)",
    ]
    return all(a in text for a in anchors)


def obligations(manifest):
    methods = {m["name"]: len(m.get("parameters", [])) for m in manifest["abi"]["methods"]}

    # Every doc-declared bridge method is implemented at its documented location with the
    # documented arity.
    for doc_name, (location, impl_name, arity) in BRIDGE_SURFACE.items():
        if location in ("shared-bridge", "both"):
            if impl_name not in methods:
                yield f"bridge_method_implemented_{doc_name}", False, True
                continue
            if arity is not None:
                yield f"bridge_method_implemented_{doc_name}", methods[impl_name] == arity, True
            else:
                yield f"bridge_method_implemented_{doc_name}", True, True
        else:
            # L2-native-bridge methods are source-anchored (separately, below).
            yield f"bridge_method_implemented_{doc_name}", True, True

    # The L2-side methods are anchored in the L2 native bridge source.
    yield "l2_native_bridge_anchors_present", l2_bridge_anchors_present(), True

    # The renames/merges are pinned: the implemented name exists and the doc-era name does not
    # (so a future re-introduction of the stale name is caught as drift).
    yield "renamed_message_consumed_pinned", (
        "isL2ToL1MessageConsumed" in methods and "isMessageConsumed" not in methods), True
    yield "merged_send_message_pinned", (
        "sendMessage" in methods and "routeMessage" not in methods
        and "enqueueL1ToL2Message" not in methods), True

    # The finalizeWithdrawal superset family: all four variants exist.
    for variant in ("finalizeWithdrawal", "finalizeWithdrawalAt",
                    "finalizeWithdrawalWithProof", "emergencyFinalizeWithdrawalWithProof"):
        yield f"finalize_variant_present_{variant}", variant in methods, True

    # Deposit arity: 4 (asset, amount, targetChainId, l2Recipient) — count matches doc; the
    # parameter ORDER differs (doc put targetChainId first) and is recorded in doc.md §10.
    yield "deposit_arity_matches_doc", methods.get("deposit") == 4, True


def solve(name, fact, expected):
    return {"name": name, "expected": expected, "actual": fact, "passed": fact == expected}


def run(artifacts=SHARED_BRIDGE_ARTIFACTS, spec=SPEC):
    report = {"schema": "neo-n4/bridge-abi-model/v1", "wholeSystemVerified": False,
              "scope": "spec-to-implementation correspondence of doc.md §10/§11 bridge method surface",
              "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten correspondence: doc.md §10/§11 method list vs deployed manifests + L2 source anchors",
                  "the doc surface spans two contracts: L1 SharedBridge and the L2 native bridge",
                  "finalizeWithdrawal's doc arity (4) was deliberately superseded (9..12) by the V5 leaf-hash binding",
                  "deposit parameter ORDER differs from doc (asset-first vs targetChainId-first); arity matches",
                  "routeMessage and enqueueL1ToL2Message were merged into sendMessage",
                  "isMessageConsumed was renamed isL2ToL1MessageConsumed",
                  "cryptographic identity of checkpoints/leaf hashes is a separate trust"],
              "source": str(artifacts), "spec": str(spec),
              "scriptSha256": None}
    try:
        import hashlib
        report["scriptSha256"] = hashlib.sha256(
            Path(__file__).read_text(encoding="utf-8-sig").replace("\r\n", "\n").encode()).hexdigest()
        report["specSha256"] = hashlib.sha256(
            Path(spec).read_text(encoding="utf-8-sig").replace("\r\n", "\n").encode()).hexdigest()
        manifest = manifest_from_artifacts(artifacts)
        report["obligations"] = [solve(*o) for o in obligations(manifest)]
        if report["obligations"] and all(o["passed"] for o in report["obligations"]):
            report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    report["exitCode"] = 0 if report["status"] == "passed" else 1
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("bridge-abi-result.json"))
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