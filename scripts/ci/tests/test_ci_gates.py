from __future__ import annotations

import importlib.util
import os
import shutil
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
gitlink_gate = load_module("verify_neo_core_gitlink")


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
    def test_real_proof_tests_run_serially(self) -> None:
        self.assertEqual(
            ["--ignored", "--nocapture", "--test-threads=1"],
            cargo_gate.test_harness_arguments(),
        )

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
        sp1_job = workflow.split("  sp1-host:", 1)[1].split("\n  rust-audit:", 1)[0]
        self.assertIn("timeout-minutes: 120", sp1_job)
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

    def test_private_network_operator_preflight_requires_reviewed_assets(self) -> None:
        private_network = (
            REPO_ROOT / "scripts" / "private-network" / "Test-PrivateNetwork.ps1"
        ).read_text(encoding="utf-8")
        harness_module = (
            REPO_ROOT / "scripts" / "private-network" / "PrivateNetworkHarness.psm1"
        ).read_text(encoding="utf-8")

        for parameter in (
            "NodeConfig",
            "BatcherNodeConfig",
            "SequencerNeoCli",
            "BatcherNeoCli",
            "Prover",
        ):
            self.assertIn(f'[string]${parameter} = ""', private_network)
        self.assertIn("[switch]$SkipOperatorPreflight", private_network)
        self.assertIn("validate reviewed operator assets", private_network)
        self.assertIn("Copy-OperatorDeployment", private_network)
        self.assertIn("New-SecureWorkingDirectory", private_network)
        self.assertIn("function Protect-SummaryText", private_network)
        self.assertIn('repoRoot = "<repo>"', private_network)
        self.assertIn('runDir = "<run>"', private_network)
        self.assertIn("Remove-Item -LiteralPath $WorkDir -Recurse -Force", private_network)
        self.assertNotIn("Join-Path $RunDir $Name", private_network)
        self.assertNotIn("Get-ChildItem -LiteralPath $sourceDirectory -Force |", harness_module)

        operator_preflight = private_network.split(
            "function Invoke-OperatorPreflight", 1
        )[1].split("function Invoke-DevnetRun", 1)[0]
        self.assertIn('"--node-config", $ReviewedNodeConfig', operator_preflight)
        self.assertIn(
            '"--batcher-node-config", $ReviewedBatcherNodeConfig', operator_preflight
        )
        self.assertIn('"--neo-cli", $sequencerNeoCli', operator_preflight)
        self.assertIn('"--neo-cli", $batcherNeoCli', operator_preflight)
        self.assertIn('"--prover", $ReviewedProver', operator_preflight)
        self.assertEqual(3, operator_preflight.count('"--dry-run"'))

    def test_neo_gitlink_must_be_published_on_official_core_branch(self) -> None:
        workflow = (REPO_ROOT / ".github" / "workflows" / "build.yml").read_text(
            encoding="utf-8"
        )
        marker = "- name: Verify Neo gitlink is published on the official core branch"
        self.assertIn(marker, workflow)
        step = workflow.split(marker, 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("python3 scripts/ci/verify_neo_core_gitlink.py", step)
        self.assertEqual(
            "https://github.com/r3e-network/neo.git",
            gitlink_gate.CANONICAL_REPOSITORY,
        )
        self.assertEqual("r3e/neo-n4-core", gitlink_gate.CANONICAL_BRANCH)

        def initialize_repository(path: Path, content: str) -> None:
            subprocess.run(
                ["git", "init", "--initial-branch", "r3e/neo-n4-core", str(path)],
                check=True,
                capture_output=True,
                text=True,
            )
            subprocess.run(
                ["git", "-C", str(path), "config", "user.name", "CI Test"],
                check=True,
            )
            subprocess.run(
                ["git", "-C", str(path), "config", "user.email", "ci@example.invalid"],
                check=True,
            )
            (path / "identity.txt").write_text(content, encoding="utf-8")
            subprocess.run(["git", "-C", str(path), "add", "identity.txt"], check=True)
            subprocess.run(
                ["git", "-C", str(path), "commit", "-m", content],
                check=True,
                capture_output=True,
                text=True,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            canonical = root / "canonical"
            attacker = root / "attacker"
            initialize_repository(canonical, "canonical")
            initialize_repository(attacker, "attacker")

            attacker_checkout = root / "attacker-checkout"
            subprocess.run(
                ["git", "clone", str(attacker), str(attacker_checkout)],
                check=True,
                capture_output=True,
                text=True,
            )
            with self.assertRaisesRegex(RuntimeError, "is not published on canonical"):
                gitlink_gate.verify_gitlink(
                    attacker_checkout,
                    canonical_repository=str(canonical),
                )

            canonical_checkout = root / "canonical-checkout"
            subprocess.run(
                ["git", "clone", str(canonical), str(canonical_checkout)],
                check=True,
                capture_output=True,
                text=True,
            )
            subprocess.run(
                [
                    "git",
                    "-C",
                    str(canonical_checkout),
                    "remote",
                    "set-url",
                    "origin",
                    str(attacker),
                ],
                check=True,
            )
            verified_head, canonical_head = gitlink_gate.verify_gitlink(
                canonical_checkout,
                canonical_repository=str(canonical),
            )
            self.assertEqual(verified_head, canonical_head)

    def test_private_network_staging_excludes_wallets_data_and_unreviewed_files(self) -> None:
        pwsh = shutil.which("pwsh")
        self.assertIsNotNone(pwsh, "PowerShell is required to verify the private-network harness")
        module = REPO_ROOT / "scripts" / "private-network" / "PrivateNetworkHarness.psm1"

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source = root / "source"
            plugin = source / "Plugins" / "DBFTPlugin"
            runtime = source / "runtimes" / "linux-x64" / "native"
            data = source / "data"
            for directory in (plugin, runtime, data):
                directory.mkdir(parents=True)

            executable = source / "neo-cli"
            executable.write_text("runtime", encoding="utf-8")
            (source / "Neo.dll").write_text("assembly", encoding="utf-8")
            (source / "neo-cli.deps.json").write_text("deps", encoding="utf-8")
            (source / "neo-cli.runtimeconfig.json").write_text("runtime", encoding="utf-8")
            (source / "config.json").write_text("source secret", encoding="utf-8")
            (source / "wallet.json").write_text("wallet secret", encoding="utf-8")
            (source / ".env").write_text("environment secret", encoding="utf-8")
            (source / ".hidden.dll").write_text("hidden root runtime", encoding="utf-8")
            (data / "chain.db3").write_text("node data", encoding="utf-8")
            (plugin / "DBFTPlugin.dll").write_text("plugin", encoding="utf-8")
            (plugin / "DBFTPlugin.json").write_text("reviewed plugin", encoding="utf-8")
            (plugin / "wallet.json").write_text("plugin wallet", encoding="utf-8")
            (plugin / ".hidden.dll").write_text("hidden plugin runtime", encoding="utf-8")
            hidden_plugin = plugin / ".private"
            hidden_plugin.mkdir()
            (hidden_plugin / "secret.dll").write_text("hidden plugin directory", encoding="utf-8")
            (runtime / "libneo.so").write_text("native runtime", encoding="utf-8")
            (runtime / "wallet.json").write_text("nested wallet", encoding="utf-8")
            (runtime / "trace.log").write_text("nested log", encoding="utf-8")
            (runtime / ".hidden.dll").write_text("hidden runtime", encoding="utf-8")
            hidden_runtime = source / "runtimes" / ".private" / "native"
            hidden_runtime.mkdir(parents=True)
            (hidden_runtime / "secret.dll").write_text("hidden directory", encoding="utf-8")
            reviewed_config = root / "reviewed-config.json"
            reviewed_config.write_text("reviewed node config", encoding="utf-8")
            destination = root / "destination"

            environment = os.environ.copy()
            environment.update(
                {
                    "NEO_N4_HARNESS_MODULE": str(module),
                    "NEO_N4_SOURCE_EXECUTABLE": str(executable),
                    "NEO_N4_REVIEWED_CONFIG": str(reviewed_config),
                    "NEO_N4_DESTINATION": str(destination),
                }
            )
            completed = subprocess.run(
                [
                    str(pwsh),
                    "-NoProfile",
                    "-Command",
                    "Import-Module $env:NEO_N4_HARNESS_MODULE -Force; "
                    "Copy-OperatorDeployment "
                    "-SourceExecutable $env:NEO_N4_SOURCE_EXECUTABLE "
                    "-ReviewedConfig $env:NEO_N4_REVIEWED_CONFIG "
                    "-DestinationDirectory $env:NEO_N4_DESTINATION "
                    "-RequiredPluginAssembly DBFTPlugin "
                    "-RequiredPluginConfig DBFTPlugin.json | Out-Null",
                ],
                check=False,
                capture_output=True,
                text=True,
                env=environment,
            )
            self.assertEqual(0, completed.returncode, completed.stderr)

            self.assertEqual(
                reviewed_config.read_bytes(), (destination / "config.json").read_bytes()
            )
            for expected in (
                destination / "neo-cli",
                destination / "Neo.dll",
                destination / "neo-cli.deps.json",
                destination / "neo-cli.runtimeconfig.json",
                destination / "Plugins" / "DBFTPlugin" / "DBFTPlugin.dll",
                destination / "Plugins" / "DBFTPlugin" / "DBFTPlugin.json",
                destination / "runtimes" / "linux-x64" / "native" / "libneo.so",
            ):
                self.assertTrue(expected.is_file(), f"approved runtime file missing: {expected}")
            for forbidden in (
                destination / "wallet.json",
                destination / ".env",
                destination / ".hidden.dll",
                destination / "data" / "chain.db3",
                destination / "Plugins" / "DBFTPlugin" / "wallet.json",
                destination / "Plugins" / "DBFTPlugin" / ".hidden.dll",
                destination / "Plugins" / "DBFTPlugin" / ".private" / "secret.dll",
                destination / "runtimes" / "linux-x64" / "native" / "wallet.json",
                destination / "runtimes" / "linux-x64" / "native" / "trace.log",
                destination / "runtimes" / "linux-x64" / "native" / ".hidden.dll",
                destination / "runtimes" / ".private" / "native" / "secret.dll",
            ):
                self.assertFalse(forbidden.exists(), f"sensitive file was staged: {forbidden}")

            reused_destination = root / "reused-destination"
            reused_destination.mkdir()
            stale_wallet = reused_destination / "wallet.json"
            stale_wallet.write_text("stale wallet", encoding="utf-8")
            environment["NEO_N4_DESTINATION"] = str(reused_destination)
            reused_result = subprocess.run(
                [
                    str(pwsh),
                    "-NoProfile",
                    "-Command",
                    "Import-Module $env:NEO_N4_HARNESS_MODULE -Force; "
                    "Copy-OperatorDeployment "
                    "-SourceExecutable $env:NEO_N4_SOURCE_EXECUTABLE "
                    "-ReviewedConfig $env:NEO_N4_REVIEWED_CONFIG "
                    "-DestinationDirectory $env:NEO_N4_DESTINATION "
                    "-RequiredPluginAssembly DBFTPlugin "
                    "-RequiredPluginConfig DBFTPlugin.json | Out-Null",
                ],
                check=False,
                capture_output=True,
                text=True,
                env=environment,
            )
            self.assertNotEqual(0, reused_result.returncode)
            self.assertIn("must not already exist", reused_result.stderr)
            self.assertEqual("stale wallet", stale_wallet.read_text(encoding="utf-8"))

            if os.name != "nt":
                linked_config = root / "linked-config.json"
                linked_config.symlink_to(reviewed_config)
                environment["NEO_N4_REVIEWED_CONFIG"] = str(linked_config)
                environment["NEO_N4_DESTINATION"] = str(root / "linked-config-destination")
                linked_config_result = subprocess.run(
                    [
                        str(pwsh),
                        "-NoProfile",
                        "-Command",
                        "Import-Module $env:NEO_N4_HARNESS_MODULE -Force; "
                        "Copy-OperatorDeployment "
                        "-SourceExecutable $env:NEO_N4_SOURCE_EXECUTABLE "
                        "-ReviewedConfig $env:NEO_N4_REVIEWED_CONFIG "
                        "-DestinationDirectory $env:NEO_N4_DESTINATION "
                        "-RequiredPluginAssembly DBFTPlugin "
                        "-RequiredPluginConfig DBFTPlugin.json | Out-Null",
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                    env=environment,
                )
                self.assertNotEqual(0, linked_config_result.returncode)
                self.assertIn("must not be a link", linked_config_result.stderr)

                linked_assembly = source / "linked.dll"
                linked_assembly.symlink_to(source / "Neo.dll")
                environment["NEO_N4_REVIEWED_CONFIG"] = str(reviewed_config)
                environment["NEO_N4_DESTINATION"] = str(root / "linked-runtime-destination")
                linked_runtime_result = subprocess.run(
                    [
                        str(pwsh),
                        "-NoProfile",
                        "-Command",
                        "Import-Module $env:NEO_N4_HARNESS_MODULE -Force; "
                        "Copy-OperatorDeployment "
                        "-SourceExecutable $env:NEO_N4_SOURCE_EXECUTABLE "
                        "-ReviewedConfig $env:NEO_N4_REVIEWED_CONFIG "
                        "-DestinationDirectory $env:NEO_N4_DESTINATION "
                        "-RequiredPluginAssembly DBFTPlugin "
                        "-RequiredPluginConfig DBFTPlugin.json | Out-Null",
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                    env=environment,
                )
                self.assertNotEqual(0, linked_runtime_result.returncode)
                self.assertIn("must not be a link", linked_runtime_result.stderr)

    def test_private_network_working_directory_is_owner_only(self) -> None:
        pwsh = shutil.which("pwsh")
        self.assertIsNotNone(pwsh, "PowerShell is required to verify the private-network harness")
        module = REPO_ROOT / "scripts" / "private-network" / "PrivateNetworkHarness.psm1"
        environment = os.environ.copy()
        environment["NEO_N4_HARNESS_MODULE"] = str(module)
        completed = subprocess.run(
            [
                str(pwsh),
                "-NoProfile",
                "-Command",
                "Import-Module $env:NEO_N4_HARNESS_MODULE -Force; "
                "New-SecureWorkingDirectory",
            ],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
        )
        self.assertEqual(0, completed.returncode, completed.stderr)
        working_directory = Path(completed.stdout.strip())
        try:
            self.assertTrue(working_directory.is_dir())
            if os.name != "nt":
                self.assertEqual(0, working_directory.stat().st_mode & 0o077)
        finally:
            shutil.rmtree(working_directory, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
