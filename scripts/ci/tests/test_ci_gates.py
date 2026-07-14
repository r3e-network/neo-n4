from __future__ import annotations

import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


CI_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = CI_ROOT.parents[1]
SP1_DOCKER_IMAGE = (
    "ghcr.io/succinctlabs/sp1@sha256:"
    "14d3c46eff7492f87e429bfbf618e3d33499ba7515b15c36eeb1bcaebc9f7b7f"
)
SP1_GNARK_IMAGE = (
    "ghcr.io/succinctlabs/sp1-gnark@sha256:"
    "be8555f1ad90870acd8c6ec7fd3ba0b1a2133ea9cddf25e130665aa651129e54"
)


def load_module(name: str):
    specification = importlib.util.spec_from_file_location(name, CI_ROOT / f"{name}.py")
    if specification is None or specification.loader is None:
        raise RuntimeError(f"cannot load {name}")
    module = importlib.util.module_from_spec(specification)
    sys.modules[name] = module
    specification.loader.exec_module(module)
    return module


dotnet_gate = load_module("run_dotnet_filtered_tests")
cargo_gate = load_module("run_cargo_ignored_tests")


class DotnetFilteredTestGateTests(unittest.TestCase):
    def test_trx_requires_every_expected_test_to_pass(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            trx = Path(temporary_directory) / "results.trx"
            trx.write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                '<ResultSummary><Counters total="3" executed="3" passed="3" failed="0" '
                'notExecuted="0" /></ResultSummary></TestRun>',
                encoding="utf-8",
            )
            counters = dotnet_gate.aggregate_trx_counters([trx])

        self.assertEqual([], dotnet_gate.validation_errors(counters, 3))

    def test_zero_selected_tests_fail_closed(self) -> None:
        counters = dotnet_gate.TestCounters(0, 0, 0, 0, 0)

        errors = dotnet_gate.validation_errors(counters, 1)

        self.assertTrue(any("selected tests" in error for error in errors))

    def test_selection_below_minimum_fails_closed(self) -> None:
        counters = dotnet_gate.TestCounters(2, 2, 2, 0, 0)

        errors = dotnet_gate.validation_errors(counters, 3)

        self.assertTrue(any("at least 3 selected tests" in error for error in errors))

    def test_selection_above_minimum_remains_valid(self) -> None:
        counters = dotnet_gate.TestCounters(4, 4, 4, 0, 0)

        errors = dotnet_gate.validation_errors(counters, 3)

        self.assertEqual([], errors)

    def test_skipped_test_fails_closed(self) -> None:
        counters = dotnet_gate.TestCounters(3, 2, 2, 0, 1)

        errors = dotnet_gate.validation_errors(counters, 3)

        self.assertTrue(any("skipped tests" in error for error in errors))

    def test_missing_trx_fails_closed(self) -> None:
        with self.assertRaisesRegex(ValueError, "did not produce a TRX result"):
            dotnet_gate.aggregate_trx_counters([])

    def test_malformed_trx_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            trx = Path(temporary_directory) / "results.trx"
            trx.write_text("<TestRun>", encoding="utf-8")

            with self.assertRaises(dotnet_gate.ElementTree.ParseError):
                dotnet_gate.parse_trx_counters(trx)

    def test_missing_counters_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            trx = Path(temporary_directory) / "results.trx"
            trx.write_text("<TestRun><ResultSummary /></TestRun>", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "exactly one TRX Counters"):
                dotnet_gate.parse_trx_counters(trx)

    def test_inconsistent_execution_count_fails_closed(self) -> None:
        counters = dotnet_gate.TestCounters(3, 2, 3, 0, 0)

        errors = dotnet_gate.validation_errors(counters, 3)

        self.assertTrue(any("selected tests to execute" in error for error in errors))


class CargoIgnoredTestGateTests(unittest.TestCase):
    def test_exact_ignored_test_selection_passes(self) -> None:
        output = "prove: test\nrejects_tamper: test\n\n2 tests, 0 benchmarks\n"

        discovered = cargo_gate.parse_listed_tests(output)

        self.assertEqual([], cargo_gate.selection_errors(discovered, {"prove", "rejects_tamper"}))

    def test_empty_ignored_test_selection_fails_closed(self) -> None:
        errors = cargo_gate.selection_errors(set(), {"prove"})

        self.assertTrue(any("zero tests" in error for error in errors))

    def test_unexpected_ignored_test_requires_gate_update(self) -> None:
        errors = cargo_gate.selection_errors({"prove", "new_gate"}, {"prove"})

        self.assertTrue(any("unexpected ignored tests" in error for error in errors))

    def test_missing_ignored_test_fails_closed(self) -> None:
        errors = cargo_gate.selection_errors({"prove"}, {"prove", "rejects_tamper"})

        self.assertTrue(any("missing ignored tests" in error for error in errors))


class CargoProveWrapperTests(unittest.TestCase):
    def run_wrapper(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as temporary_directory:
            fake_cargo = Path(temporary_directory) / "cargo"
            fake_cargo.write_text(
                "#!/usr/bin/env bash\nprintf '%s\\n' \"$@\"\n",
                encoding="utf-8",
            )
            fake_cargo.chmod(0o755)
            environment = os.environ.copy()
            environment["PATH"] = f"{temporary_directory}:{environment['PATH']}"
            return subprocess.run(
                [str(CI_ROOT / "cargo-prove-locked.sh"), *arguments],
                check=False,
                capture_output=True,
                text=True,
                env=environment,
            )

    def test_rejects_native_build(self) -> None:
        completed = self.run_wrapper("prove", "build")

        self.assertEqual(2, completed.returncode)
        self.assertIn("requires the reproducible --docker", completed.stderr)

    def test_appends_locked_once(self) -> None:
        completed = self.run_wrapper("prove", "build", "--docker")

        self.assertEqual(0, completed.returncode)
        self.assertEqual(["prove", "build", "--docker", "--locked"], completed.stdout.splitlines())

    def test_preserves_explicit_locked(self) -> None:
        completed = self.run_wrapper("prove", "build", "--docker", "--locked")

        self.assertEqual(0, completed.returncode)
        self.assertEqual(["prove", "build", "--docker", "--locked"], completed.stdout.splitlines())

    def test_production_build_surfaces_share_immutable_image(self) -> None:
        shared_support = (REPO_ROOT / "bridge" / "sp1_build_support.rs").read_text(
            encoding="utf-8"
        )
        runtime_support = (REPO_ROOT / "bridge" / "sp1_runtime_support.rs").read_text(
            encoding="utf-8"
        )
        workflow = (REPO_ROOT / ".github" / "workflows" / "build.yml").read_text(
            encoding="utf-8"
        )
        private_network = (
            REPO_ROOT / "scripts" / "private-network" / "Test-PrivateNetwork.ps1"
        ).read_text(encoding="utf-8")
        gateway_guest_build = (
            REPO_ROOT / "bridge" / "neo-zkvm-gateway-guest" / "build.rs"
        ).read_text(encoding="utf-8")
        gateway_host_build = (
            REPO_ROOT / "bridge" / "neo-zkvm-gateway-host" / "build.rs"
        ).read_text(encoding="utf-8")
        batch_host_build = (
            REPO_ROOT / "bridge" / "neo-zkvm-host" / "build.rs"
        ).read_text(encoding="utf-8")

        for source in (shared_support, workflow, private_network):
            self.assertIn(SP1_DOCKER_IMAGE, source)
        for source in (runtime_support, workflow, private_network):
            self.assertIn(SP1_GNARK_IMAGE, source)
        self.assertIn("validate_gnark_backend", runtime_support)
        self.assertIn("aarch64 prover hosts must enable", runtime_support)

        self.assertIn(
            'command.args(["prove", "build", "--docker", "--locked"]);',
            shared_support,
        )
        self.assertIn("cargo prove build --docker --locked", workflow)
        self.assertIn("cargo prove build --docker --locked", private_network)
        prove_step = workflow.split(
            "- name: cargo prove build (reproducible guest ELF)", 1
        )[1].split("\n      - name:", 1)[0]
        self.assertIn("sudo chown --recursive --no-dereference", prove_step)
        self.assertLess(
            prove_step.index("cargo prove build --docker --locked"),
            prove_step.index("sudo chown --recursive --no-dereference"),
        )
        self.assertIn("cargo test real recursive Gateway proof", private_network)
        self.assertIn('include!("batch_vk_manifest.rs")', gateway_guest_build)
        self.assertNotIn('include!("vk_manifest.rs")', gateway_guest_build)
        self.assertIn("batch_vk_manifest.rs", gateway_host_build)
        self.assertIn("neo-zkvm-guest/vk_manifest.rs", gateway_host_build)
        self.assertIn("vk_manifest.rs", gateway_host_build)
        self.assertIn("neo-zkvm-guest/vk_manifest.rs", batch_host_build)
        self.assertIn('"CARGO_TARGET_DIR",', shared_support)
        for host_build in (batch_host_build, gateway_host_build):
            self.assertIn(
                "sp1_build_support::configure_reproducible_build",
                host_build,
            )
            self.assertIn(
                "sp1_build_support::sanitize_nested_build_environment",
                host_build,
            )
            self.assertIn(
                "sp1_build_support::isolate_loopback_docker_proxy",
                host_build,
            )
        self.assertIn('command.env("DOCKER_CONFIG"', shared_support)
        self.assertIn(
            "Verify production C# to native SP1 execution boundary", workflow
        )
        self.assertIn(
            "RealNativeExecutor_ExecutesBootstrappedNeoGenesisRetTransaction",
            workflow,
        )
        self.assertIn(
            'NEO_ZKVM_EXECUTOR="$GITHUB_WORKSPACE/target/release/neo-zkvm-executor"',
            workflow,
        )
        self.assertNotIn("run_real_sp1_proof", workflow)
        for step_name in (
            "cargo test (neo-zkvm-host, real proof)",
            "cargo test (neo-zkvm-gateway-host, real recursive proof)",
        ):
            marker = f"- name: {step_name}"
            self.assertIn(marker, workflow)
            step = workflow.split(marker, 1)[1].split("\n      - name:", 1)[0]
            self.assertNotRegex(step, r"(?m)^\s+if:")
        self.assertIn("prove_and_verify_real_zk_proof", workflow)
        self.assertIn(
            "proves_and_host_verifies_real_recursive_gateway_groth16",
            workflow,
        )
        for host_build in (batch_host_build, gateway_host_build):
            self.assertIn(
                "sp1_build_support::publish_verified_artifact",
                host_build,
            )


if __name__ == "__main__":
    unittest.main()
