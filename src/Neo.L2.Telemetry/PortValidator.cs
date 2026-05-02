namespace Neo.L2.Telemetry;

/// <summary>
/// Shared TCP port-range validator for any caller that's about to construct an
/// <see cref="System.Net.IPEndPoint"/> from operator-supplied input.
/// </summary>
/// <remarks>
/// Lives next to <see cref="MetricsHttpServer"/> rather than in any plugin so CLI tools
/// (e.g. <c>neo-l2-devnet --metrics-port</c>) can reuse it without taking a plugin
/// dependency. Without parse-time validation, an out-of-range port propagates to
/// <see cref="System.Net.IPEndPoint"/> construction; the resulting
/// <see cref="System.ArgumentOutOfRangeException"/> deep in the wiring path doesn't
/// tell the operator which input was bad.
/// </remarks>
public static class PortValidator
{
    /// <summary>
    /// Throw <see cref="System.IO.InvalidDataException"/> with a clear message naming the
    /// offending value if <paramref name="port"/> is outside <c>[0, 65535]</c>. Returns
    /// the port on success so callers can chain via assignment.
    /// </summary>
    public static int Validate(int port, string contextLabel = "Port")
    {
        if (port < 0 || port > 65535)
            throw new System.IO.InvalidDataException(
                $"{contextLabel} {port} out of range — must be 0 (any free) or 1..65535");
        return port;
    }
}
