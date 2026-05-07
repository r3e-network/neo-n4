using System;
using System.IO;
using System.Threading.Tasks;
using Neo.Hub.Deploy;
using Neo.Json;

namespace Neo.Hub.Deploy.UnitTests;

/// <summary>
/// Tests for <see cref="VerifyCommand"/> — the <c>neo-hub-deploy verify</c> path.
/// MVP version verifies that each plan step's <c>.nef</c> + <c>.manifest.json</c>
/// build artifacts exist on disk and propagates a non-zero exit code if any are
/// missing (so a CI script treats it as a hard fail).
/// </summary>
[TestClass]
public class UT_VerifyCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-verify-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task Verify_AllArtifactsPresent_ExitsZero()
    {
        // Build a minimal plan with two steps. Touch the corresponding nef +
        // manifest paths so File.Exists returns true. Verify should accept.
        var step1Nef = Path.Combine(_tempDir, "A.nef");
        var step1Manifest = Path.Combine(_tempDir, "A.manifest.json");
        var step2Nef = Path.Combine(_tempDir, "B.nef");
        var step2Manifest = Path.Combine(_tempDir, "B.manifest.json");
        File.WriteAllText(step1Nef, "");
        File.WriteAllText(step1Manifest, "");
        File.WriteAllText(step2Nef, "");
        File.WriteAllText(step2Manifest, "");

        var planPath = WritePlan(new[]
        {
            ("A", step1Nef, step1Manifest),
            ("B", step2Nef, step2Manifest),
        });

        var rc = await VerifyCommand.RunAsync(new[]
        {
            "--plan", planPath,
            "--rpc", "http://fake-rpc.test",
        });
        Assert.AreEqual(0, rc, "all-artifacts-present must exit 0");
    }

    [TestMethod]
    public async Task Verify_MissingNef_ExitsTwo()
    {
        // The bug this command originally had: missing artifacts silently exited 0,
        // so a CI script saw 'success' even when contracts weren't actually built.
        // Pin that the fix exits 2 instead.
        var step1Nef = Path.Combine(_tempDir, "A.nef");  // never created
        var step1Manifest = Path.Combine(_tempDir, "A.manifest.json");
        File.WriteAllText(step1Manifest, "");

        var planPath = WritePlan(new[]
        {
            ("A", step1Nef, step1Manifest),
        });

        var rc = await VerifyCommand.RunAsync(new[]
        {
            "--plan", planPath,
            "--rpc", "http://fake-rpc.test",
        });
        Assert.AreEqual(2, rc, "missing nef must exit 2");
    }

    [TestMethod]
    public async Task Verify_MissingManifest_ExitsTwo()
    {
        var step1Nef = Path.Combine(_tempDir, "A.nef");
        var step1Manifest = Path.Combine(_tempDir, "A.manifest.json");  // never created
        File.WriteAllText(step1Nef, "");

        var planPath = WritePlan(new[]
        {
            ("A", step1Nef, step1Manifest),
        });

        var rc = await VerifyCommand.RunAsync(new[]
        {
            "--plan", planPath,
            "--rpc", "http://fake-rpc.test",
        });
        Assert.AreEqual(2, rc, "missing manifest must exit 2");
    }

    [TestMethod]
    public async Task Verify_PartialMissing_ExitsTwo()
    {
        // 1 ok + 1 missing — must still exit 2 since the operator can't deploy
        // the missing one. Pin so a future refactor doesn't accidentally treat
        // 'most are ok' as success.
        var step1Nef = Path.Combine(_tempDir, "A.nef");
        var step1Manifest = Path.Combine(_tempDir, "A.manifest.json");
        File.WriteAllText(step1Nef, "");
        File.WriteAllText(step1Manifest, "");

        var step2Nef = Path.Combine(_tempDir, "B.nef");
        var step2Manifest = Path.Combine(_tempDir, "B.manifest.json");  // missing

        var planPath = WritePlan(new[]
        {
            ("A", step1Nef, step1Manifest),
            ("B", step2Nef, step2Manifest),
        });

        var rc = await VerifyCommand.RunAsync(new[]
        {
            "--plan", planPath,
            "--rpc", "http://fake-rpc.test",
        });
        Assert.AreEqual(2, rc, "partial-missing must exit 2");
    }

    [TestMethod]
    public async Task Verify_MissingRpcFlag_ExitsOne()
    {
        // --rpc is required even though MVP doesn't actually hit it. Without it,
        // the operator's intent is ambiguous (verify what?). Pin 1 (caller error)
        // distinct from 2 (failed verification).
        var planPath = WritePlan(Array.Empty<(string, string, string)>());
        var rc = await VerifyCommand.RunAsync(new[] { "--plan", planPath });
        Assert.AreEqual(1, rc, "missing --rpc must exit 1 (caller error)");
    }

    [TestMethod]
    public async Task Verify_MissingPlanFile_ExitsOne()
    {
        // Plan file doesn't exist on disk → operator gave a bad path. Pin 1
        // (caller error) distinct from 2 (verification fail).
        var rc = await VerifyCommand.RunAsync(new[]
        {
            "--plan", Path.Combine(_tempDir, "does-not-exist.json"),
            "--rpc", "http://fake-rpc.test",
        });
        Assert.AreEqual(1, rc, "missing plan file must exit 1");
    }

    [TestMethod]
    public async Task Verify_MalformedPlanJson_ExitsOne()
    {
        var planPath = Path.Combine(_tempDir, "garbage.json");
        File.WriteAllText(planPath, "{not valid json");

        var rc = await VerifyCommand.RunAsync(new[]
        {
            "--plan", planPath,
            "--rpc", "http://fake-rpc.test",
        });
        Assert.AreEqual(1, rc, "malformed plan must exit 1");
    }

    [TestMethod]
    public async Task Verify_NullArgs_Rejected()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await VerifyCommand.RunAsync(null!));
    }

    private string WritePlan((string name, string nef, string manifest)[] steps)
    {
        var stepArr = new JArray();
        foreach (var s in steps)
        {
            stepArr.Add(new JObject
            {
                ["name"] = s.name,
                ["nefPath"] = s.nef,
                ["manifestPath"] = s.manifest,
                ["dependsOn"] = new JArray(),
                ["deployData"] = new JArray(),
            });
        }
        var plan = new JObject
        {
            ["version"] = 1,
            ["network"] = "test",
            ["steps"] = stepArr,
        };
        var path = Path.Combine(_tempDir, $"plan-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, plan.ToString());
        return path;
    }
}
