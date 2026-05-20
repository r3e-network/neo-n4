using System;

namespace Neo.Hub.Deploy;

internal static class ArgUtil
{
    public static string Get(string[] args, string name, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }

        return defaultValue;
    }

    public static bool HasFlag(string[] args, string name)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (var arg in args)
        {
            if (string.Equals(arg, name, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
