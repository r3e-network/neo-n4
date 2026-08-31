using System;
using System.IO;
using Neo.L2;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="ListTemplatesCommand"/> — the discoverability helper that prints
/// the four chain-config templates with their §16.2 dimensions + use-case descriptions.
/// Also exercises <see cref="TemplateCatalog"/>, the shared single source of truth for
/// templates consumed by create-chain / new-l2 / list-templates.
/// </summary>
[TestClass]
public class UT_ListTemplatesCommand
{
    [TestMethod]
    public void Catalog_HasExactlyFourTemplates_InOrder()
    {
        // Template count + ordering is API: doc.md §6 lists exactly 4 chain modes,
        // and the default `rollup` MUST be first (so `Resolve` falls back to it on
        // unknown names + so the table prints with the safe default at the top).
        var names = TemplateCatalog.All;
        Assert.AreEqual(4, names.Length, "must have exactly 4 templates");
        Assert.AreEqual("rollup", names[0].Name, "rollup must be first (default)");
        Assert.AreEqual("zk-rollup", names[1].Name);
        Assert.AreEqual("validium", names[2].Name);
        Assert.AreEqual("sidechain", names[3].Name);
    }

    [TestMethod]
    public void Catalog_EveryTemplateNameADeclaredChainMode()
    {
        // ChainMode is the one TemplateCatalog field no other test parses: proofType and
        // securityLevel strings are exercised by the two legality guards below, but nothing read
        // ChainMode, so an invented fifth value would ship in every `create-chain` config and only
        // surface as `validate` exiting 2 on a file the template itself printed.
        foreach (var t in TemplateCatalog.All)
        {
            Assert.IsTrue(Enum.TryParse<ChainMode>(t.ChainMode, out var mode),
                $"template '{t.Name}' names chainMode={t.ChainMode}, which is not a ChainMode member "
                + $"({string.Join(" / ", Enum.GetNames<ChainMode>())})");
            Assert.AreEqual(t.ChainMode, mode.ToString(),
                $"template '{t.Name}' chainMode must be spelled exactly as its enum member");
        }
    }

    [TestMethod]
    public void Catalog_EveryTemplateProofTypeIsLegalForItsSecurityLevel()
    {
        // A template is copied verbatim by the next operator who runs create-chain, so
        // a template whose proofType its own securityLevel forbids ships a chain that
        // faults at the first submitBatch. ProofRouting is the off-chain mirror of
        // SettlementManager.IsProofTypeCompatible (pinned pair-by-pair against the
        // contract by UT_SettlementManager_ProofRouting).
        foreach (var t in TemplateCatalog.All)
        {
            var level = Enum.Parse<SecurityLevel>(t.SecurityLevel);
            var proof = Enum.Parse<ProofType>(t.ProofType);
            Assert.IsTrue(ProofRouting.AcceptsProofType(level, proof),
                $"template '{t.Name}' pairs securityLevel={level} with proofType={proof}, "
                + $"which SettlementManager rejects (accepted: {string.Join(" / ", ProofRouting.AcceptedProofTypes(level))})");
        }
    }

    [TestMethod]
    public void Catalog_NonSidechainTemplates_NameARouteTheDeployerRegisters()
    {
        // Neo.Hub.Deploy writes only the Zk verifier route and then locks
        // VerifierRegistry one-way, so any other proofType needs an operator-supplied
        // route registered before that lock. Sidechain is the single template allowed
        // to name an unserved route (committee attestation is its whole point); its
        // caveat is documented in samples/README.md and pinned as a `validate` warning
        // by UT_ValidateChainConfigCommand.
        foreach (var t in TemplateCatalog.All)
        {
            if (t.Name == "sidechain") continue;
            var proof = Enum.Parse<ProofType>(t.ProofType);
            Assert.IsTrue(ProofRouting.HasProductionVerifierRoute(proof),
                $"template '{t.Name}' defaults to proofType={proof}, which has no verifier route "
                + "in the bundle Neo.Hub.Deploy locks — the chain could not settle as created");
        }
    }

    [TestMethod]
    public void LaunchingGuide_TemplateTable_MatchesTheCatalog()
    {
        // `docs/launching-an-l2.md` is the operator walkthrough, and its Templates table is a hand-typed
        // copy of TemplateCatalog. H18 found both copies (this file and its Chinese mirror) still
        // advertising rollup = L1 DA + Optimistic and sidechain = External + None after the code said
        // otherwise — the exact "two tools disagree about the default posture" shape, one level up.
        AssertDocTableMatchesCatalog("docs/launching-an-l2.md", "Template");
    }

    [TestMethod]
    public void LaunchingGuide_ChineseTemplateTable_MatchesTheCatalog()
    {
        AssertDocTableMatchesCatalog("docs/zh/launching-an-l2.md", "模板");
    }

    private static void AssertDocTableMatchesCatalog(string relativeDocPath, string headerCell)
    {
        var path = Path.Combine(Neo.L2.TestInfra.RepoRoot.Directory, relativeDocPath);
        Assert.IsTrue(File.Exists(path), $"doc missing: {path}");

        var rows = new List<string[]>();
        var inTable = false;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (!inTable)
            {
                inTable = trimmed.StartsWith('|') && FirstCell(trimmed) == headerCell;
                continue;
            }
            if (!trimmed.StartsWith('|')) break;
            var cells = trimmed.Split('|')[1..^1].Select(c => c.Trim()).ToArray();
            if (cells.All(c => c.All(ch => ch is '-' or ':'))) continue; // |---|---| separator row
            rows.Add(cells);
        }

        Assert.IsTrue(inTable && rows.Count > 0,
            $"{relativeDocPath}: no table found whose header cell is \"{headerCell}\"");
        Assert.AreEqual(
            string.Join(", ", TemplateCatalog.All.Select(t => t.Name)),
            string.Join(", ", rows.Select(r => r[0].Trim('`'))),
            $"{relativeDocPath}: the documented template list must be the catalog's, in order");

        foreach (var (row, template) in rows.Zip(TemplateCatalog.All))
        {
            Assert.AreEqual(6, row.Length, $"{relativeDocPath}: row for {template.Name} is malformed: {string.Join(" | ", row)}");
            var name = row[0].Trim('`');
            Assert.AreEqual(template.Name, name, $"{relativeDocPath}: row order drifted");
            Assert.AreEqual(template.ChainMode, row[1], $"{name}: chainMode column disagrees with TemplateCatalog");
            Assert.AreEqual(template.DaMode, row[2], $"{name}: daMode column disagrees with TemplateCatalog");
            Assert.AreEqual(template.ProofType, row[3], $"{name}: proofType column disagrees with TemplateCatalog");
            Assert.AreEqual(template.SecurityLevel, row[4], $"{name}: SecurityLevel column disagrees with TemplateCatalog");
            Assert.AreEqual(template.ExitModel, row[5], $"{name}: Exit column disagrees with TemplateCatalog");
        }
    }

    private static string FirstCell(string trimmedRow) =>
        trimmedRow.Split('|')[1].Trim();

    [TestMethod]
    public void Catalog_Resolve_ReturnsExactTemplate()
    {
        var validium = TemplateCatalog.Resolve("validium");
        Assert.AreEqual("validium", validium.Name);
        Assert.AreEqual("Validium", validium.SecurityLevel);
        Assert.AreEqual("NeoFS", validium.DaMode);
        Assert.IsTrue(validium.GatewayEnabled, "validium template must enable gateway");
    }

    [TestMethod]
    public void Catalog_Resolve_UnknownName_FallsBackToDefault()
    {
        // Defensive default. CreateChainCommand passes user input through Resolve;
        // an unknown name should produce the default `rollup` rather than an
        // exception or null record.
        var fallback = TemplateCatalog.Resolve("not-a-real-template");
        Assert.AreEqual("rollup", fallback.Name);
    }

    [TestMethod]
    public void Catalog_IsKnown_DistinguishesValidFromInvalid()
    {
        Assert.IsTrue(TemplateCatalog.IsKnown("rollup"));
        Assert.IsTrue(TemplateCatalog.IsKnown("zk-rollup"));
        Assert.IsTrue(TemplateCatalog.IsKnown("validium"));
        Assert.IsTrue(TemplateCatalog.IsKnown("sidechain"));
        Assert.IsFalse(TemplateCatalog.IsKnown("Rollup"), "case-sensitive: capitalized variants are not valid");
        Assert.IsFalse(TemplateCatalog.IsKnown("not-a-real-template"));
        Assert.IsFalse(TemplateCatalog.IsKnown(""));
    }

    [TestMethod]
    public void Catalog_ValidNames_ListsAllInOrder()
    {
        Assert.AreEqual("rollup, zk-rollup, validium, sidechain", TemplateCatalog.ValidNames);
    }

    [TestMethod]
    public void ListTemplates_NoArgs_PrintsAllTemplates_AndExitsZero()
    {
        var (rc, output) = CaptureOutput(() => ListTemplatesCommand.Run(Array.Empty<string>()));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "rollup");
        StringAssert.Contains(output, "zk-rollup");
        StringAssert.Contains(output, "validium");
        StringAssert.Contains(output, "sidechain");
        StringAssert.Contains(output, "Default template");
    }

    [TestMethod]
    public void ListTemplates_WithTemplate_PrintsFullDetails()
    {
        var (rc, output) = CaptureOutput(() => ListTemplatesCommand.Run(new[] { "--template", "validium" }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "Template: validium");
        StringAssert.Contains(output, "chainMode      = L2ValidiumMode");
        StringAssert.Contains(output, "daMode         = NeoFS");
        StringAssert.Contains(output, "Use case:");
        StringAssert.Contains(output, "DEX");  // validium's use-case mentions DEX
        StringAssert.Contains(output, "neo-stack new-l2");  // sample command
        StringAssert.Contains(output, "--template validium");
    }

    [TestMethod]
    public void ListTemplates_UnknownTemplate_RejectsWithExit1()
    {
        var (rc, _, stderr) = CaptureBoth(() => ListTemplatesCommand.Run(new[] { "--template", "not-a-real-template" }));
        Assert.AreEqual(1, rc);
        StringAssert.Contains(stderr, "not recognized");
        StringAssert.Contains(stderr, "rollup");  // valid names listed for the operator
        StringAssert.Contains(stderr, "validium");
    }

    [TestMethod]
    public void ListTemplates_PerTemplateDetail_RoundTripsThroughEverySupportedName()
    {
        // Pin that every template name in TemplateCatalog.All can be passed through
        // ListTemplates --template <name> without rejection. Catches a regression
        // where the catalog gains a name but ListTemplatesCommand's filter doesn't
        // recognize it.
        foreach (var t in TemplateCatalog.All)
        {
            var (rc, output) = CaptureOutput(() => ListTemplatesCommand.Run(new[] { "--template", t.Name }));
            Assert.AreEqual(0, rc, $"template '{t.Name}' must be accepted");
            StringAssert.Contains(output, $"Template: {t.Name}");
        }
    }

    // ---- Helpers ----

    private static (int rc, string output) CaptureOutput(Func<int> run)
    {
        var origOut = Console.Out;
        try
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            var rc = run();
            return (rc, sw.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    private static (int rc, string stdout, string stderr) CaptureBoth(Func<int> run)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        try
        {
            var swOut = new StringWriter();
            var swErr = new StringWriter();
            Console.SetOut(swOut);
            Console.SetError(swErr);
            var rc = run();
            return (rc, swOut.ToString(), swErr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
