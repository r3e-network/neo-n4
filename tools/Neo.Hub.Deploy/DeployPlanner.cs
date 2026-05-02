using Neo.Json;

namespace Neo.Hub.Deploy;

/// <summary>
/// Plans a NeoHub deployment: validates the plan, topologically sorts steps by
/// <see cref="DeployStep.DependsOn"/>, and emits a <see cref="DeployBundle"/> with all
/// <c>$step:&lt;name&gt;</c> placeholders resolved against the post-deploy contract hashes
/// supplied by the caller.
/// </summary>
/// <remarks>
/// The caller is responsible for actually computing each step's contract hash (by inspecting
/// the .nef sender + nonce) and feeding it back via <see cref="HashResolver"/>. For test/devnet
/// flows that hash can be deterministic; for real L1 deploys it depends on the signer key + nonce.
/// </remarks>
public static class DeployPlanner
{
    /// <summary>Caller-supplied resolution of a step name to its post-deploy contract hash.</summary>
    public delegate UInt160 HashResolver(string stepName);

    /// <summary>
    /// Topologically sort + resolve placeholders. Throws on cycle or unknown reference.
    /// </summary>
    public static DeployBundle Plan(DeployPlan plan, HashResolver hashResolver)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(hashResolver);

        var sorted = TopologicalSort(plan.Steps);
        var seen = new HashSet<string>();
        var invocations = new List<DeployInvocation>(sorted.Count);

        foreach (var step in sorted)
        {
            foreach (var dep in step.DependsOn)
                if (!seen.Contains(dep))
                    throw new InvalidOperationException($"step '{step.Name}' depends on '{dep}' but '{dep}' was not deployed first");

            invocations.Add(new DeployInvocation
            {
                Name = step.Name,
                NefPath = step.NefPath,
                ManifestPath = step.ManifestPath,
                ResolvedDeployData = ResolvePlaceholders(step.DeployData, seen, hashResolver),
            });
            seen.Add(step.Name);
        }

        return new DeployBundle
        {
            Version = 1,
            Network = plan.Network,
            Invocations = invocations,
        };
    }

    /// <summary>
    /// Recursively walk a JSON token tree replacing every <c>$step:&lt;name&gt;</c> string
    /// with the corresponding contract hash (formatted as Hash160).
    /// </summary>
    private static JArray ResolvePlaceholders(JArray input, HashSet<string> deployed, HashResolver resolver)
    {
        var output = new JArray();
        foreach (var token in input) output.Add(ResolveToken(token, deployed, resolver));
        return output;
    }

    private static JToken? ResolveToken(JToken? token, HashSet<string> deployed, HashResolver resolver)
    {
        switch (token)
        {
            case null:
                return null;
            case JString s when s.AsString().StartsWith("$step:"):
                var name = s.AsString()[6..];
                if (!deployed.Contains(name))
                    throw new InvalidOperationException($"placeholder $step:{name} references step that has not been deployed yet");
                return new JString(resolver(name).ToString());
            case JArray arr:
                var newArr = new JArray();
                foreach (var t in arr) newArr.Add(ResolveToken(t, deployed, resolver));
                return newArr;
            case JObject obj:
                var newObj = new JObject();
                foreach (var (k, v) in obj.Properties) newObj[k] = ResolveToken(v, deployed, resolver);
                return newObj;
            default:
                return token;
        }
    }

    private static IReadOnlyList<DeployStep> TopologicalSort(IReadOnlyList<DeployStep> steps)
    {
        // Build the name index manually so we can surface clear errors for empty / duplicate
        // names. ToDictionary would throw a generic "An item with the same key has already
        // been added" message that doesn't tell the operator which step was duplicated.
        var byName = new Dictionary<string, DeployStep>();
        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
                throw new InvalidOperationException("deploy step name must not be empty or whitespace");
            if (!byName.TryAdd(step.Name, step))
                throw new InvalidOperationException($"duplicate deploy step name '{step.Name}'");
        }
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        var output = new List<DeployStep>(steps.Count);

        foreach (var step in steps) Visit(step, byName, visited, visiting, output);
        return output;
    }

    private static void Visit(
        DeployStep step,
        IReadOnlyDictionary<string, DeployStep> byName,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<DeployStep> output)
    {
        if (visited.Contains(step.Name)) return;
        if (!visiting.Add(step.Name))
            throw new InvalidOperationException($"dependency cycle detected through step '{step.Name}'");

        foreach (var dep in step.DependsOn)
        {
            if (!byName.TryGetValue(dep, out var depStep))
                throw new InvalidOperationException($"step '{step.Name}' depends on unknown step '{dep}'");
            Visit(depStep, byName, visited, visiting, output);
        }

        visiting.Remove(step.Name);
        visited.Add(step.Name);
        output.Add(step);
    }
}
