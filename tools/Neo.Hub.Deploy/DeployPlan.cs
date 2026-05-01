using Neo.Json;

namespace Neo.Hub.Deploy;

/// <summary>
/// Declarative description of what NeoHub (and optionally L2-native) contracts to deploy and
/// in what order, plus the <c>_deploy</c> parameters each contract expects.
/// </summary>
/// <remarks>
/// The deploy tool reads one of these from JSON, validates dependencies, then emits a deploy
/// bundle that a wallet-equipped runner consumes. Decoupling planning from signing keeps the
/// tool testable and avoids forcing a particular wallet dependency on the planner.
/// </remarks>
public sealed record DeployPlan
{
    /// <summary>The currently-supported plan format version. Bump together with FromJson's check.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Plan format version (currently <see cref="CurrentVersion"/>).</summary>
    public required int Version { get; init; }

    /// <summary>Human-readable network identifier (e.g. "neo-n3-testnet").</summary>
    public required string Network { get; init; }

    /// <summary>Contracts in deploy order.</summary>
    public required IReadOnlyList<DeployStep> Steps { get; init; }

    /// <summary>Encode to JSON.</summary>
    public string ToJson()
    {
        var obj = new JObject();
        obj["version"] = Version;
        obj["network"] = Network;
        var arr = new JArray();
        foreach (var s in Steps) arr.Add(s.ToJson());
        obj["steps"] = arr;
        return obj.ToString();
    }

    /// <summary>Parse from JSON.</summary>
    public static DeployPlan FromJson(string json)
    {
        var obj = (JObject)(JToken.Parse(json) ?? throw new ArgumentException("empty plan"));
        var version = (int)obj["version"]!.AsNumber();
        if (version != CurrentVersion)
            throw new InvalidDataException($"unsupported deploy-plan version {version}; expected {CurrentVersion}");
        var network = obj["network"]!.AsString();
        var stepsArr = (JArray)obj["steps"]!;
        var steps = new DeployStep[stepsArr.Count];
        for (var i = 0; i < stepsArr.Count; i++)
            steps[i] = DeployStep.FromJson((JObject)stepsArr[i]!);
        return new DeployPlan { Version = version, Network = network, Steps = steps };
    }
}

/// <summary>One contract-deploy step.</summary>
public sealed record DeployStep
{
    /// <summary>Symbolic name (used to reference this step from later steps' deploy data).</summary>
    public required string Name { get; init; }

    /// <summary>Path to the compiled <c>.nef</c> artifact (relative to plan file).</summary>
    public required string NefPath { get; init; }

    /// <summary>Path to the matching <c>.manifest.json</c>.</summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    /// Deploy parameters as a JSON array. Strings of the form <c>$step:&lt;name&gt;</c> are
    /// resolved to the post-deploy contract hash of the named step before sending.
    /// </summary>
    public required JArray DeployData { get; init; }

    /// <summary>Symbolic dependencies — names of steps that must complete first.</summary>
    public required IReadOnlyList<string> DependsOn { get; init; }

    /// <summary>Encode to JSON.</summary>
    public JObject ToJson()
    {
        var obj = new JObject();
        obj["name"] = Name;
        obj["nefPath"] = NefPath;
        obj["manifestPath"] = ManifestPath;
        obj["deployData"] = DeployData;
        var deps = new JArray();
        foreach (var d in DependsOn) deps.Add(d);
        obj["dependsOn"] = deps;
        return obj;
    }

    /// <summary>Parse from JSON.</summary>
    public static DeployStep FromJson(JObject obj)
    {
        var deps = (JArray?)obj["dependsOn"];
        var depList = deps is null ? Array.Empty<string>() : deps.Select(d => d!.AsString()).ToArray();
        return new DeployStep
        {
            Name = obj["name"]!.AsString(),
            NefPath = obj["nefPath"]!.AsString(),
            ManifestPath = obj["manifestPath"]!.AsString(),
            DeployData = (JArray)obj["deployData"]!,
            DependsOn = depList,
        };
    }
}
