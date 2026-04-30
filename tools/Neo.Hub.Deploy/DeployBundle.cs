using Neo.Json;

namespace Neo.Hub.Deploy;

/// <summary>
/// Deploy bundle: a topologically sorted, dependency-resolved sequence of concrete deploy
/// invocations. The output of <see cref="DeployPlanner"/>'s plan stage; the input that a
/// wallet-equipped runner consumes to actually sign + submit.
/// </summary>
public sealed record DeployBundle
{
    /// <summary>Bundle format version.</summary>
    public required int Version { get; init; }

    /// <summary>Network identifier (matches the source plan).</summary>
    public required string Network { get; init; }

    /// <summary>Per-step concrete invocation (deploy data resolved to actual byte payloads).</summary>
    public required IReadOnlyList<DeployInvocation> Invocations { get; init; }

    /// <summary>Encode to JSON.</summary>
    public string ToJson()
    {
        var obj = new JObject();
        obj["version"] = Version;
        obj["network"] = Network;
        var arr = new JArray();
        foreach (var i in Invocations) arr.Add(i.ToJson());
        obj["invocations"] = arr;
        return obj.ToString();
    }
}

/// <summary>One concrete invocation — fully resolved, ready to sign + send.</summary>
public sealed record DeployInvocation
{
    /// <summary>Symbolic step name from the plan.</summary>
    public required string Name { get; init; }

    /// <summary>Filesystem path to the .nef. The signer reads + base64-encodes for the script.</summary>
    public required string NefPath { get; init; }

    /// <summary>Filesystem path to the manifest.</summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    /// Resolved deploy data — every <c>$step:&lt;name&gt;</c> placeholder has been substituted
    /// with the actual contract hash from the bundle's prior invocations.
    /// </summary>
    public required JArray ResolvedDeployData { get; init; }

    /// <summary>Encode to JSON.</summary>
    public JObject ToJson()
    {
        var obj = new JObject();
        obj["name"] = Name;
        obj["nefPath"] = NefPath;
        obj["manifestPath"] = ManifestPath;
        obj["resolvedDeployData"] = ResolvedDeployData;
        return obj;
    }
}
