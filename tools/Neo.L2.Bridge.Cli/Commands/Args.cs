using System;
using System.Numerics;

namespace Neo.L2.Bridge.Cli.Commands;

/// <summary>
/// Shared argument-parsing helpers. Every subcommand surfaces parse errors via
/// <see cref="Console.Error"/> + returns <see langword="null"/> so the caller can
/// fail with the right exit code without scattering try/catch.
/// </summary>
internal static class Args
{
    public static string? Get(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    public static string? RequireString(string[] args, string name)
    {
        var v = Get(args, name);
        if (v is null) Console.Error.WriteLine($"❌ missing {name}");
        return v;
    }

    public static UInt160? RequireUInt160(string[] args, string name)
    {
        var raw = RequireString(args, name);
        if (raw is null) return null;
        try { return UInt160.Parse(raw); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {name} '{raw}' is not a valid UInt160: {ex.Message}");
            return null;
        }
    }

    public static UInt256? RequireUInt256(string[] args, string name)
    {
        var raw = RequireString(args, name);
        if (raw is null) return null;
        try { return UInt256.Parse(raw); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {name} '{raw}' is not a valid UInt256: {ex.Message}");
            return null;
        }
    }

    public static uint? RequireUInt(string[] args, string name)
    {
        var raw = RequireString(args, name);
        if (raw is null) return null;
        if (!uint.TryParse(raw, out var v))
        {
            Console.Error.WriteLine($"❌ {name} '{raw}' is not a valid uint");
            return null;
        }
        return v;
    }

    public static ulong? RequireULong(string[] args, string name)
    {
        var raw = RequireString(args, name);
        if (raw is null) return null;
        if (!ulong.TryParse(raw, out var v))
        {
            Console.Error.WriteLine($"❌ {name} '{raw}' is not a valid ulong");
            return null;
        }
        return v;
    }

    public static BigInteger? RequireBigInteger(string[] args, string name)
    {
        var raw = RequireString(args, name);
        if (raw is null) return null;
        if (!BigInteger.TryParse(raw, out var v))
        {
            Console.Error.WriteLine($"❌ {name} '{raw}' is not a valid integer");
            return null;
        }
        if (v < 0)
        {
            Console.Error.WriteLine($"❌ {name} must be non-negative, got {v}");
            return null;
        }
        return v;
    }
}
