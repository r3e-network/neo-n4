using System;
using System.Numerics;
using Neo;
using Neo.L2.Bridge.Cli.Commands;
using Neo.L2.Persistence;

namespace Neo.L2.Faucet.Cli;

/// <summary>
/// <c>neo-l2-faucet</c> — production testnet faucet CLI. Per-recipient rate
/// limiting + amount caps + drip journaling, persisted to an
/// <see cref="IL2KeyValueStore"/> so limits survive faucet restarts.
/// </summary>
/// <remarks>
/// Subcommands:
/// <list type="bullet">
///   <item><c>drip</c> — emit a SharedBridge.Deposit invocation script (operator pastes into wallet).</item>
///   <item><c>status</c> — read a recipient's drip history.</item>
/// </list>
/// <para>
/// No built-in signer: production deployments use HSMs or hot wallets, the CLI
/// emits the canonical invocation hex the operator's wallet of choice signs and
/// submits. Same plan-printer pattern as <c>neo-bridge</c>.
/// </para>
/// </remarks>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        var sub = args[0];
        var rest = args[1..];
        try
        {
            return sub switch
            {
                "drip" => DripCommand.Run(rest),
                "status" => StatusCommand.Run(rest),
                _ => UsageError($"unknown subcommand '{sub}'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    internal static int UsageError(string message)
    {
        Console.Error.WriteLine($"❌ {message}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: neo-l2-faucet <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  drip     Emit a SharedBridge.Deposit script + record the drip in the journal.");
        Console.WriteLine("  status   Read a recipient's drip history (last drip, total, count).");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --journal <dir>           Persistent journal store directory (RocksDB).");
        Console.WriteLine("                            Omit for an in-memory journal (devnet/testing only).");
        Console.WriteLine("  --recipient <hex>         L2-side recipient address (UInt160).");
        Console.WriteLine();
        Console.WriteLine("drip-only options:");
        Console.WriteLine("  --bridge <hash>           SharedBridge contract hash on L1.");
        Console.WriteLine("  --asset <hash>            Asset to drip (UInt160; typically L1 GAS).");
        Console.WriteLine("  --target-chain <id>       Destination L2 chainId.");
        Console.WriteLine("  --amount <N>              Drip amount (datoshi; default 100000000 = 1 GAS).");
        Console.WriteLine("  --cooldown-seconds <N>    Per-recipient cooldown override (default 3600).");
        Console.WriteLine("  --max-per-drip <N>        Override the per-drip amount cap (default 1 GAS).");
        Console.WriteLine("  --max-lifetime <N>        Override the per-recipient lifetime cap (default 100 GAS).");
    }
}

/// <summary><c>neo-l2-faucet drip</c> — rate-checked deposit-script emitter.</summary>
internal static class DripCommand
{
    public static int Run(string[] args)
    {
        var bridge = ParseUInt160(args, "--bridge");
        var asset = ParseUInt160(args, "--asset");
        var recipient = ParseUInt160(args, "--recipient");
        var targetChain = ParseUInt(args, "--target-chain");
        var amountRaw = ArgUtil.Get(args, "--amount", "100000000"); // 1 GAS default
        if (bridge is null || asset is null || recipient is null || targetChain is null) return 1;
        if (!BigInteger.TryParse(amountRaw, out var amount))
        {
            Console.Error.WriteLine($"❌ --amount '{amountRaw}' is not a valid integer");
            return 1;
        }

        var policy = new FaucetPolicy
        {
            CooldownSeconds = uint.Parse(ArgUtil.Get(args, "--cooldown-seconds", FaucetPolicy.Default.CooldownSeconds.ToString())),
            MaxPerDrip = BigInteger.Parse(ArgUtil.Get(args, "--max-per-drip", FaucetPolicy.Default.MaxPerDrip.ToString())),
            MaxPerRecipientLifetime = BigInteger.Parse(ArgUtil.Get(args, "--max-lifetime", FaucetPolicy.Default.MaxPerRecipientLifetime.ToString())),
        };

        using var store = OpenJournalStore(args);
        var journal = new FaucetJournal(store);
        var nowUnix = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var existing = journal.Get(recipient);

        var decision = policy.Decide(amount, nowUnix, existing);
        if (!decision.Allowed)
        {
            Console.Error.WriteLine($"❌ drip rejected: {decision.RejectReason}");
            return 2;
        }

        // Build the canonical SharedBridge.Deposit invocation script. Same encoder
        // neo-bridge uses — guarantees byte-identical output for matching args.
        byte[] script;
        try
        {
            script = InvocationBuilder.BuildDeposit(bridge, asset, amount, targetChain.Value, recipient);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"❌ {ex.Message}");
            return 1;
        }

        // Record the drip BEFORE printing the script, so a Ctrl-C between print
        // and journal-write doesn't let the operator accidentally double-spend.
        journal.RecordDrip(recipient, nowUnix, amount);

        Console.WriteLine($"# faucet drip approved");
        Console.WriteLine($"#   recipient    = {recipient}");
        Console.WriteLine($"#   amount       = {amount}");
        Console.WriteLine($"#   targetChain  = {targetChain}");
        Console.WriteLine($"#   asset        = {asset}");
        if (existing is not null)
        {
            Console.WriteLine($"#   prior drips  = {existing.TotalCount} totaling {existing.TotalDripped}");
        }
        Console.WriteLine();
        Console.WriteLine("script (hex, paste into wallet to sign + broadcast):");
        Console.WriteLine(Convert.ToHexString(script).ToLowerInvariant());
        return 0;
    }

    private static IL2KeyValueStore OpenJournalStore(string[] args)
    {
        var dir = ArgUtil.Get(args, "--journal", "");
        return string.IsNullOrEmpty(dir)
            ? new InMemoryKeyValueStore()
            : new RocksDbKeyValueStore(dir);
    }

    private static UInt160? ParseUInt160(string[] args, string name)
    {
        var raw = ArgUtil.Get(args, name, "");
        if (string.IsNullOrEmpty(raw))
        {
            Console.Error.WriteLine($"❌ missing {name}");
            return null;
        }
        try { return UInt160.Parse(raw); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {name} '{raw}' is not a valid UInt160: {ex.Message}");
            return null;
        }
    }

    private static uint? ParseUInt(string[] args, string name)
    {
        var raw = ArgUtil.Get(args, name, "");
        if (string.IsNullOrEmpty(raw))
        {
            Console.Error.WriteLine($"❌ missing {name}");
            return null;
        }
        if (!uint.TryParse(raw, out var v))
        {
            Console.Error.WriteLine($"❌ {name} '{raw}' is not a valid uint");
            return null;
        }
        return v;
    }
}

/// <summary><c>neo-l2-faucet status</c> — read a recipient's drip history.</summary>
internal static class StatusCommand
{
    public static int Run(string[] args)
    {
        var recipientRaw = ArgUtil.Get(args, "--recipient", "");
        if (string.IsNullOrEmpty(recipientRaw))
        {
            Console.Error.WriteLine("❌ missing --recipient");
            return 1;
        }
        UInt160 recipient;
        try { recipient = UInt160.Parse(recipientRaw); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ --recipient invalid: {ex.Message}");
            return 1;
        }

        using var store = OpenJournalStore(args);
        var journal = new FaucetJournal(store);
        var entry = journal.Get(recipient);
        if (entry is null)
        {
            Console.WriteLine($"recipient {recipient}: never received a drip from this faucet");
            return 0;
        }
        Console.WriteLine($"recipient {recipient}");
        Console.WriteLine($"  lastDripUnix = {entry.LastDripUnixSeconds} ({DateTimeOffset.FromUnixTimeSeconds((long)entry.LastDripUnixSeconds):u})");
        Console.WriteLine($"  totalDripped = {entry.TotalDripped}");
        Console.WriteLine($"  totalCount   = {entry.TotalCount}");
        return 0;
    }

    private static IL2KeyValueStore OpenJournalStore(string[] args)
    {
        var dir = ArgUtil.Get(args, "--journal", "");
        return string.IsNullOrEmpty(dir)
            ? new InMemoryKeyValueStore()
            : new RocksDbKeyValueStore(dir);
    }
}

/// <summary>Local copy of the args-parsing helper (matches neo-bridge / neo-stack convention).</summary>
internal static class ArgUtil
{
    public static string Get(string[] args, string name, string defaultValue)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return defaultValue;
    }
}
