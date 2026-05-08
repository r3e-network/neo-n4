using System;
using System.Numerics;

namespace Neo.L2.Faucet.Cli;

/// <summary>
/// Drip-policy decision: rate limit (cooldown between drips per recipient) +
/// per-request amount cap + lifetime per-recipient cap.
/// </summary>
/// <remarks>
/// Production faucets MUST cap per recipient — without a lifetime cap a
/// well-timed bot can slowly drain the faucet across the cooldown window.
/// </remarks>
public sealed class FaucetPolicy
{
    /// <summary>Minimum seconds between successive drips to the same recipient.</summary>
    public required uint CooldownSeconds { get; init; }

    /// <summary>Maximum amount per individual drip (in token's smallest unit, e.g. datoshi).</summary>
    public required BigInteger MaxPerDrip { get; init; }

    /// <summary>Maximum cumulative amount per recipient (lifetime).</summary>
    public required BigInteger MaxPerRecipientLifetime { get; init; }

    /// <summary>Default policy: 1h cooldown, 1 GAS per drip, 100 GAS lifetime per recipient.</summary>
    public static FaucetPolicy Default { get; } = new()
    {
        CooldownSeconds = 3600,
        MaxPerDrip = 100_000_000UL, // 1 GAS = 10^8 datoshi
        MaxPerRecipientLifetime = 100UL * 100_000_000UL, // 100 GAS
    };

    /// <summary>
    /// Decide whether a recipient can drip <paramref name="amount"/> at
    /// <paramref name="nowUnixSeconds"/>, given their <paramref name="entry"/>
    /// (null for a never-before-dripped recipient).
    /// </summary>
    public DripDecision Decide(BigInteger amount, ulong nowUnixSeconds, FaucetEntry? entry)
    {
        if (amount <= 0)
            return DripDecision.Reject($"amount must be positive, got {amount}");
        if (amount > MaxPerDrip)
            return DripDecision.Reject($"amount {amount} exceeds per-drip cap {MaxPerDrip}");
        if (entry is not null)
        {
            var elapsed = nowUnixSeconds - entry.LastDripUnixSeconds;
            if (elapsed < CooldownSeconds)
            {
                var remaining = CooldownSeconds - elapsed;
                return DripDecision.Reject(
                    $"cooldown active: last drip was {elapsed}s ago, must wait another {remaining}s");
            }
            var newTotal = entry.TotalDripped + amount;
            if (newTotal > MaxPerRecipientLifetime)
                return DripDecision.Reject(
                    $"lifetime cap exceeded: {entry.TotalDripped} + {amount} > {MaxPerRecipientLifetime}");
        }
        return DripDecision.Allow;
    }
}

/// <summary>Result of <see cref="FaucetPolicy.Decide"/>.</summary>
public sealed record DripDecision
{
    /// <summary>True if the drip is allowed; false otherwise.</summary>
    public required bool Allowed { get; init; }

    /// <summary>If <see cref="Allowed"/> is false, the human-readable rejection reason.</summary>
    public string? RejectReason { get; init; }

    /// <summary>Singleton allow.</summary>
    public static DripDecision Allow { get; } = new() { Allowed = true };

    /// <summary>Construct a rejection.</summary>
    public static DripDecision Reject(string reason)
        => new() { Allowed = false, RejectReason = reason };
}
