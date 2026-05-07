using System;
using System.IO;
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
