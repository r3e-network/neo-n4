using System;

namespace Neo.Stack.Cli.Commands;

internal static class ArgUtil
{
    public static string Get(string[] args, string name, string defaultValue)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return defaultValue;
    }

    public static bool HasFlag(string[] args, string name)
    {
        foreach (var a in args)
            if (a == name) return true;
        return false;
    }
}
