using System.Globalization;

namespace Neo.L2.Settlement.Rpc;

/// <summary>Canonical conversions between GAS and its smallest unit, datoshi.</summary>
/// <remarks>See doc.md §9 (token and GAS model).</remarks>
public static class NeoGas
{
    /// <summary>Number of datoshi in one GAS.</summary>
    public const long DatoshiPerGas = 100_000_000L;

    /// <summary>
    /// Parses a Neo RPC fee value. Integer strings are already datoshi; decimal strings
    /// are GAS values emitted by legacy RPC implementations.
    /// </summary>
    public static long ParseRpcValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!value.Contains('.', StringComparison.Ordinal))
        {
            var datoshi = long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (datoshi < 0) throw new FormatException("GAS value must not be negative");
            return datoshi;
        }

        var gas = decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (gas < 0) throw new FormatException("GAS value must not be negative");
        var scaled = checked(gas * DatoshiPerGas);
        if (scaled != decimal.Truncate(scaled))
            throw new FormatException($"GAS value '{value}' has more than 8 decimal places");
        return decimal.ToInt64(scaled);
    }
}
