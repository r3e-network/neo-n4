using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="ArgUtil"/> — the tiny CLI argument parser shared by every
/// neo-stack subcommand. Indirectly exercised by every UT_*Command test, but the
/// edge cases below need direct pinning so a refactor doesn't silently change
/// the semantics every command depends on (e.g. "first occurrence wins" or
/// "trailing flag without a value returns default").
/// </summary>
[TestClass]
public class UT_ArgUtil
{
    [TestMethod]
    public void Get_FlagWithValue_ReturnsValue()
    {
        var args = new[] { "--chain-id", "1099", "--template", "rollup" };
        Assert.AreEqual("1099", ArgUtil.Get(args, "--chain-id", "default"));
        Assert.AreEqual("rollup", ArgUtil.Get(args, "--template", "default"));
    }

    [TestMethod]
    public void Get_FlagMissing_ReturnsDefault()
    {
        var args = new[] { "--chain-id", "1099" };
        Assert.AreEqual("default", ArgUtil.Get(args, "--template", "default"));
    }

    [TestMethod]
    public void Get_EmptyArgs_ReturnsDefault()
    {
        Assert.AreEqual("fallback", ArgUtil.Get(System.Array.Empty<string>(), "--anything", "fallback"));
    }

    [TestMethod]
    public void Get_FlagAtEndWithoutValue_ReturnsDefault()
    {
        // ArgUtil.Get loops `i < args.Length - 1` — a trailing flag without a value
        // gets ignored (returns default), it doesn't throw. Pin so a future refactor
        // doesn't change this to throw or to return an empty string instead.
        var args = new[] { "--chain-id", "1099", "--template" }; // --template is trailing
        Assert.AreEqual("rollup", ArgUtil.Get(args, "--template", "rollup"));
    }

    [TestMethod]
    public void Get_DuplicateFlag_ReturnsFirstOccurrence()
    {
        // First-occurrence-wins semantics. An operator who sets --chain-id twice
        // gets the first value. Pin so a refactor that switches to last-wins doesn't
        // silently change script behavior.
        var args = new[] { "--chain-id", "1099", "--chain-id", "2099" };
        Assert.AreEqual("1099", ArgUtil.Get(args, "--chain-id", "default"));
    }

    [TestMethod]
    public void Get_EqualsSyntaxNotSupported_FallsBackToDefault()
    {
        // ArgUtil only handles `--flag value`, not `--flag=value`. Pin this so
        // an operator using `--chain-id=1099` knows their flag won't take effect
        // (and a future change to support equals-form is an explicit feature add,
        // not a silent semantic change).
        var args = new[] { "--chain-id=1099" };
        Assert.AreEqual("default", ArgUtil.Get(args, "--chain-id", "default"));
    }

    [TestMethod]
    public void HasFlag_Present_ReturnsTrue()
    {
        var args = new[] { "--with-tests", "--chain-id", "1099" };
        Assert.IsTrue(ArgUtil.HasFlag(args, "--with-tests"));
    }

    [TestMethod]
    public void HasFlag_Missing_ReturnsFalse()
    {
        var args = new[] { "--chain-id", "1099" };
        Assert.IsFalse(ArgUtil.HasFlag(args, "--with-tests"));
    }

    [TestMethod]
    public void HasFlag_EmptyArgs_ReturnsFalse()
    {
        Assert.IsFalse(ArgUtil.HasFlag(System.Array.Empty<string>(), "--anything"));
    }

    [TestMethod]
    public void HasFlag_AsValueOfAnotherFlag_StillReturnsTrue()
    {
        // HasFlag does a simple linear scan with no awareness of "is this a value
        // for the previous flag". That's intentional (cheap + matches operator
        // expectations: if --with-tests appears anywhere, it's "set"). Pin the
        // behavior so a future refactor that adds positional awareness is an
        // explicit decision.
        var args = new[] { "--name", "--with-tests" }; // --with-tests is technically the value of --name
        Assert.IsTrue(ArgUtil.HasFlag(args, "--with-tests"),
            "HasFlag scans tokens linearly without positional awareness");
    }
}
