using Neo.Cryptography.ECC;
using Neo.Extensions.VM;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;

namespace Neo.L2.Sequencer;

/// <summary>Builds the governance transaction that installs the L2 dBFT validator set.</summary>
/// <remarks>
/// See doc.md §7.1. The resulting script calls the native L2 system configuration contract;
/// the contract revalidates the count against consensus <c>ValidatorsCount</c> and requires
/// the configured owner witness.
/// </remarks>
public static class SequencerCommitteeTransactionBuilder
{
    /// <summary>Maximum validator count accepted by the N4 dBFT integration.</summary>
    public const int MaxValidatorCount = 64;

    /// <summary>Returns a unique, canonical secp256r1 validator sequence.</summary>
    public static IReadOnlyList<ECPoint> Normalize(
        IEnumerable<ECPoint> validators,
        int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(validators);
        if (expectedCount is < 1 or > MaxValidatorCount)
            throw new ArgumentOutOfRangeException(
                nameof(expectedCount),
                $"Expected validator count must be between 1 and {MaxValidatorCount}.");

        var canonical = validators.ToArray();
        if (canonical.Length != expectedCount)
            throw new ArgumentException(
                $"Validator count {canonical.Length} does not match expected count {expectedCount}.",
                nameof(validators));
        if (canonical.Any(static validator => validator is null))
            throw new ArgumentException("Validator keys cannot contain null.", nameof(validators));
        if (canonical.Any(static validator => validator.IsInfinity))
            throw new ArgumentException("The point at infinity is not a valid validator key.", nameof(validators));

        Array.Sort(canonical);
        if (canonical.Distinct().Count() != canonical.Length)
            throw new ArgumentException("Duplicate validator keys are not allowed.", nameof(validators));
        return canonical;
    }

    /// <summary>Builds a canonical invocation of native <c>setSequencerValidators</c>.</summary>
    public static byte[] BuildSetValidatorsScript(
        IEnumerable<ECPoint> validators,
        int expectedCount)
    {
        var canonical = Normalize(validators, expectedCount);
        var parameter = new ContractParameter(ContractParameterType.Array)
        {
            Value = canonical
                .Select(static validator => new ContractParameter(ContractParameterType.PublicKey)
                {
                    Value = validator
                })
                .ToList()
        };

        using var builder = new ScriptBuilder();
        builder.EmitDynamicCall(
            NativeContract.L2SystemConfig.Hash,
            "setSequencerValidators",
            parameter);
        return builder.ToArray();
    }
}
