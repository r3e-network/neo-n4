namespace Neo.L2;

/// <summary>
/// Canonical VM identifiers used in <c>chain.config.json</c>.
/// </summary>
public static class L2ExecutionVms
{
    /// <summary>Canonical Neo N4 L2 execution target: NeoVM2 on the RISC-V-compatible runtime.</summary>
    public const string NeoVm2RiscV = "neovm2-riscv";

    /// <summary>Short alias accepted by older tooling for <see cref="NeoVm2RiscV"/>.</summary>
    public const string RiscV2 = "riscv2";

    /// <summary>Legacy NeoVM compatibility path for transitional tests and N3-era scripts.</summary>
    public const string LegacyNeoVm = "neovm";

    /// <summary>True when the supplied VM id is recognized by N4 tooling.</summary>
    public static bool IsKnown(string? vm) =>
        vm == NeoVm2RiscV || vm == RiscV2 || vm == LegacyNeoVm;

    /// <summary>True when the supplied VM id points at the legacy NeoVM compatibility path.</summary>
    public static bool IsLegacy(string? vm) => vm == LegacyNeoVm;
}
