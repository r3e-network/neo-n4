#!/usr/bin/env bash
# Local RISC-V VM + neo-zkvm verification (no real SP1 prove required).
# Mirrors the non-funded gates exercised in CI:
#   - neo-execution-core / neo-zkvm-guest host tests
#   - neo-vm-rs + neo-riscv-host
#   - C# Neo.L2.Executor.RiscV RealNative (with built libneo_riscv_host)
#   - C# Sp1StatefulBatchExecutor with release neo-zkvm-executor
#
# Funded / env gates NOT covered here:
#   cargo prove --docker ELF rebuild, #[ignore] real-CPU prove, gateway recursive proof.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

echo "==> neo-execution-core tests"
cargo test -p neo-execution-core --release --locked

echo "==> neo-zkvm-guest host tests"
cargo test -p neo-zkvm-guest --release --locked

echo "==> neo-vm-rs tests"
cargo test --manifest-path external/neo-vm-rs/Cargo.toml --release --locked

echo "==> external/neo-zkvm cargo check"
cargo check --manifest-path external/neo-zkvm/Cargo.toml --locked

echo "==> neo-riscv-host build + tests"
cargo build --release --locked -p neo-riscv-host --manifest-path external/neo-riscv-vm/Cargo.toml
cargo test --release --locked -p neo-riscv-host --manifest-path external/neo-riscv-vm/Cargo.toml

echo "==> neo-zkvm-executor release binary"
cargo build --release --locked -p neo-zkvm-guest --bin neo-zkvm-executor
EXECUTOR="$ROOT/target/release/neo-zkvm-executor"
test -x "$EXECUTOR"

RISCV_LIB_DIR="$ROOT/external/neo-riscv-vm/target/release"
if [[ "$(uname -s)" == "Darwin" ]]; then
  RISCV_LIB="$RISCV_LIB_DIR/libneo_riscv_host.dylib"
else
  RISCV_LIB="$RISCV_LIB_DIR/libneo_riscv_host.so"
fi
test -f "$RISCV_LIB"

echo "==> C# RISC-V RealNative suite"
dotnet build tests/Neo.L2.Executor.RiscV.UnitTests/Neo.L2.Executor.RiscV.UnitTests.csproj \
  -c Release /p:NuGetAudit=false --nologo
RISCV_TEST_BIN="$ROOT/tests/Neo.L2.Executor.RiscV.UnitTests/bin/Release/net10.0"
cp "$RISCV_LIB" "$RISCV_TEST_BIN/"
export NEO_RISCV_NATIVE_TESTS=1
if [[ "$(uname -s)" == "Darwin" ]]; then
  export DYLD_LIBRARY_PATH="$RISCV_TEST_BIN${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
else
  export LD_LIBRARY_PATH="$RISCV_TEST_BIN${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi
dotnet test tests/Neo.L2.Executor.RiscV.UnitTests/Neo.L2.Executor.RiscV.UnitTests.csproj \
  -c Release /p:NuGetAudit=false --no-build --nologo

echo "==> C# Sp1Stateful + executor boundary"
export NEO_ZKVM_EXECUTOR="$EXECUTOR"
dotnet test tests/Neo.L2.Executor.UnitTests/Neo.L2.Executor.UnitTests.csproj \
  -c Release /p:NuGetAudit=false --nologo \
  --filter 'FullyQualifiedName~Sp1Stateful|FullyQualifiedName~RealNativeExecutor'

echo "==> C# Sp1/RiscV proving seams"
dotnet test tests/Neo.L2.Proving.UnitTests/Neo.L2.Proving.UnitTests.csproj \
  -c Release /p:NuGetAudit=false --nologo \
  --filter 'FullyQualifiedName~Sp1|FullyQualifiedName~RiscV'

echo "==> C# Sp1 settlement stack"
dotnet test tests/Neo.Plugins.L2Settlement.UnitTests/Neo.Plugins.L2Settlement.UnitTests.csproj \
  -c Release /p:NuGetAudit=false --nologo \
  --filter 'FullyQualifiedName~Sp1Settlement'

echo
echo "Local RISC-V + neo-zkvm gates: OK"
echo "  executor: $EXECUTOR"
echo "  sha256:   $(shasum -a 256 "$EXECUTOR" | awk '{print $1}')"
echo "  riscv:    $RISCV_LIB"
echo "Still funded/env: SP1 Docker prove, real-CPU #[ignore] prove, gateway recursive proof."
