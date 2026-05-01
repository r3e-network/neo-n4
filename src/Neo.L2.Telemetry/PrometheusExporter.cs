using System.Globalization;
using System.Text;

namespace Neo.L2.Telemetry;

/// <summary>
/// Renders a <see cref="MetricsSnapshot"/> as Prometheus exposition format
/// (<see href="https://prometheus.io/docs/instrumenting/exposition_formats/"/>). Produces
/// counters as <c>_total</c>, gauges as plain values, and histograms as
/// <c>_count</c> + <c>_sum</c> + <c>_max</c> aggregates (no quantile buckets — this is the
/// 80% case for an in-process snapshot exporter; production deployments wire OpenTelemetry
/// for full bucketed histograms).
/// </summary>
/// <remarks>
/// Metric names are mapped 1:1 except <c>.</c> → <c>_</c> (Prometheus disallows dots).
/// Tags become labels: <c>l2.proving.generated{kind=Multisig}</c> →
/// <c>l2_proving_generated_total{kind="Multisig"} N</c>.
/// </remarks>
public static class PrometheusExporter
{
    private const string ContentTypeValue = "text/plain; version=0.0.4; charset=utf-8";

    /// <summary>HTTP <c>Content-Type</c> header value for Prometheus exposition format.</summary>
    public static string ContentType => ContentTypeValue;

    /// <summary>Render <paramref name="snapshot"/> as Prometheus text. Each metric has a HELP + TYPE preamble.</summary>
    public static string Format(MetricsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sb = new StringBuilder(2048);

        // Group by base name so HELP/TYPE only appears once per family.
        WriteFamilies(sb, snapshot.Counters, "counter", static (k, v) => v.ToString(CultureInfo.InvariantCulture), nameSuffix: "_total");
        WriteFamilies(sb, snapshot.Gauges, "gauge", static (k, v) => v.ToString("G17", CultureInfo.InvariantCulture));

        foreach (var (baseName, entries) in GroupHistograms(snapshot.Histograms))
        {
            var promBase = ToPromName(baseName);
            sb.Append("# HELP ").Append(promBase).Append(' ').Append(MetricCatalog.GetHelp(baseName)).Append('\n');
            sb.Append("# TYPE ").Append(promBase).Append(" summary\n");
            foreach (var (key, values) in entries)
            {
                var (_, labels) = SplitNameAndLabels(key);
                var count = values.Count;
                double sum = 0, max = 0;
                for (var i = 0; i < count; i++)
                {
                    sum += values[i];
                    if (values[i] > max) max = values[i];
                }
                AppendLine(sb, promBase + "_count", labels, count.ToString(CultureInfo.InvariantCulture));
                AppendLine(sb, promBase + "_sum", labels, sum.ToString("G17", CultureInfo.InvariantCulture));
                AppendLine(sb, promBase + "_max", labels, max.ToString("G17", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static void WriteFamilies<TVal>(
        StringBuilder sb,
        IReadOnlyDictionary<string, TVal> entries,
        string promType,
        Func<string, TVal, string> formatValue,
        string nameSuffix = "")
    {
        var byBase = new Dictionary<string, List<KeyValuePair<string, TVal>>>(StringComparer.Ordinal);
        foreach (var kv in entries)
        {
            var (baseName, _) = SplitNameAndLabels(kv.Key);
            if (!byBase.TryGetValue(baseName, out var list))
                byBase[baseName] = list = new List<KeyValuePair<string, TVal>>();
            list.Add(kv);
        }
        foreach (var baseName in byBase.Keys.OrderBy(s => s, StringComparer.Ordinal))
        {
            var promBase = ToPromName(baseName) + nameSuffix;
            sb.Append("# HELP ").Append(promBase).Append(' ').Append(MetricCatalog.GetHelp(baseName)).Append('\n');
            sb.Append("# TYPE ").Append(promBase).Append(' ').Append(promType).Append('\n');
            foreach (var kv in byBase[baseName].OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var (_, labels) = SplitNameAndLabels(kv.Key);
                AppendLine(sb, promBase, labels, formatValue(kv.Key, kv.Value));
            }
        }
    }

    private static IEnumerable<(string BaseName, IReadOnlyList<KeyValuePair<string, IReadOnlyList<double>>> Entries)>
        GroupHistograms(IReadOnlyDictionary<string, IReadOnlyList<double>> entries)
    {
        var byBase = new Dictionary<string, List<KeyValuePair<string, IReadOnlyList<double>>>>(StringComparer.Ordinal);
        foreach (var kv in entries)
        {
            var (baseName, _) = SplitNameAndLabels(kv.Key);
            if (!byBase.TryGetValue(baseName, out var list))
                byBase[baseName] = list = new List<KeyValuePair<string, IReadOnlyList<double>>>();
            list.Add(kv);
        }
        foreach (var baseName in byBase.Keys.OrderBy(s => s, StringComparer.Ordinal))
            yield return (baseName, byBase[baseName].OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray());
    }

    private static (string BaseName, string Labels) SplitNameAndLabels(string key)
    {
        var brace = key.IndexOf('{');
        if (brace < 0) return (key, string.Empty);
        return (key[..brace], key[brace..]); // labels include the surrounding braces
    }

    private static string ToPromName(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(c == '.' || c == '-' ? '_' : c);
        }
        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, string name, string labels, string value)
    {
        sb.Append(name);
        if (labels.Length > 0)
        {
            // Convert internal {k=v,k2=v2} → Prometheus {k="v",k2="v2"} with proper escaping.
            sb.Append('{');
            var inner = labels.AsSpan(1, labels.Length - 2); // strip { and }
            var first = true;
            foreach (var pair in SplitOnComma(inner))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(pair[..eq]).Append("=\"");
                AppendEscapedLabelValue(sb, pair.AsSpan(eq + 1));
                sb.Append('"');
            }
            sb.Append('}');
        }
        sb.Append(' ').Append(value).Append('\n');
    }

    /// <summary>
    /// Escape a Prometheus label value per the exposition spec:
    /// <list type="bullet">
    ///   <item><description><c>\</c> → <c>\\</c></description></item>
    ///   <item><description><c>"</c> → <c>\"</c></description></item>
    ///   <item><description>newline → <c>\n</c></description></item>
    /// </list>
    /// </summary>
    private static void AppendEscapedLabelValue(StringBuilder sb, ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append('\\').Append('\\'); break;
                case '"': sb.Append('\\').Append('"'); break;
                case '\n': sb.Append('\\').Append('n'); break;
                default: sb.Append(c); break;
            }
        }
    }

    private static IEnumerable<string> SplitOnComma(ReadOnlySpan<char> s)
    {
        // Cannot yield from a method with ROS<char> param; materialize.
        var owned = s.ToString();
        return owned.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }
}
