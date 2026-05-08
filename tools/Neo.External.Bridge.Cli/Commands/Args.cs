using System;

namespace Neo.External.Bridge.Cli.Commands;

/// <summary>
/// Local copy of the argument-parsing helpers used by sister CLIs
/// (<c>Neo.L2.Bridge.Cli.Commands.Args</c>). Same shape, copied rather
/// than cross-referenced so this CLI doesn't need
/// <c>InternalsVisibleTo</c> from the Bridge CLI's csproj.
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
}
