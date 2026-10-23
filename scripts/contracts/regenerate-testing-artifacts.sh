#!/usr/bin/env bash
# Recompile the NeoHub contracts with the pinned devpack nccs and refresh the typed
# artifacts that tests/NeoHub.Contracts.VmTests executes.
#
# The VmTests do not compile contracts: they run the NEF + manifest embedded in
# tests/NeoHub.Contracts.VmTests/TestingArtifacts/NeoHub<Name>.artifacts.cs. Those files are
# NOT refreshed by `dotnet build`, so a contract source change is untested until this script
# runs. For each contract it:
#   1. runs nccs on contracts/NeoHub.<Name>/NeoHub.<Name>.csproj, writing the deployable
#      .nef / .manifest.json and the generated NeoHub.<Name>.artifacts.cs into
#      contracts/NeoHub.<Name>/bin/sc (the same directory the deploy tooling and the CI
#      fresh-manifest gates read);
#   2. copies the generated artifact into TestingArtifacts, renaming the generated class
#      `NeoHub.<Name>` (not a valid C# identifier) to `NeoHub<Name>`, and normalising to LF.
#
# Usage: scripts/contracts/regenerate-testing-artifacts.sh [Name ...]
#   Names default to all NeoHub contracts. Set NCCS to override the compiler path.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TESTING_ARTIFACTS="$ROOT/tests/NeoHub.Contracts.VmTests/TestingArtifacts"
# Every production NeoHub contract. `MultisigVerifier` was missing from this list, so its
# checked-in artifact was never refreshed and the contract had no VM test coverage.
ALL_CONTRACTS=(RollupHub SharedBridge GovernanceController ZkVerifier Sp1Groth16Verifier MultisigVerifier)

resolve_nccs() {
    if [ -n "${NCCS:-}" ]; then
        printf '%s\n' "$NCCS"
        return
    fi
    local dir="$ROOT/external/neo-devpack-dotnet/src/Neo.Compiler.CSharp/bin/Release/net10.0"
    local candidate
    for candidate in "$dir/nccs.exe" "$dir/nccs"; do
        if [ -f "$candidate" ]; then
            printf '%s\n' "$candidate"
            return
        fi
    done
    echo "nccs not found under $dir." >&2
    echo "Build the pinned compiler first:" >&2
    echo "  dotnet build external/neo-devpack-dotnet/src/Neo.Compiler.CSharp/Neo.Compiler.CSharp.csproj -c Release -p:NuGetAudit=false" >&2
    exit 1
}

NCCS_BIN="$(resolve_nccs)"
"$NCCS_BIN" --version

if [ "$#" -gt 0 ]; then
    CONTRACTS=("$@")
else
    CONTRACTS=("${ALL_CONTRACTS[@]}")
fi

for name in "${CONTRACTS[@]}"; do
    known=0
    for candidate in "${ALL_CONTRACTS[@]}"; do
        [ "$candidate" = "$name" ] && known=1
    done
    if [ "$known" -ne 1 ]; then
        echo "unknown contract '$name' (expected one of: ${ALL_CONTRACTS[*]})" >&2
        exit 1
    fi

    project_dir="$ROOT/contracts/NeoHub.$name"
    out_dir="$project_dir/bin/sc"
    echo "==> nccs NeoHub.$name"
    "$NCCS_BIN" "$project_dir/NeoHub.$name.csproj" --output "$out_dir" --generate-artifacts source

    for ext in nef manifest.json artifacts.cs; do
        if [ ! -f "$out_dir/NeoHub.$name.$ext" ]; then
            echo "nccs did not produce $out_dir/NeoHub.$name.$ext" >&2
            exit 1
        fi
    done

    raw="$out_dir/NeoHub.$name.artifacts.cs"
    dest="$TESTING_ARTIFACTS/NeoHub$name.artifacts.cs"
    tmp="$dest.tmp"
    # Strip a UTF-8 BOM and CRs, then give the generated class a valid identifier.
    sed -e '1s/^\xEF\xBB\xBF//' -e 's/\r$//' \
        -e "s/public abstract class NeoHub\\.$name(/public abstract class NeoHub$name(/" \
        "$raw" > "$tmp"
    if ! grep -q "public abstract class NeoHub$name(" "$tmp"; then
        rm -f "$tmp"
        echo "generated artifact for NeoHub.$name does not declare class NeoHub.$name" >&2
        exit 1
    fi
    mv "$tmp" "$dest"
    echo "    wrote ${dest#"$ROOT"/}"
done
