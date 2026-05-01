using Neo.L2.Telemetry;

namespace Neo.L2.Challenge;

/// <summary>
/// Interactive bisection game between sequencer and challenger over N transactions in a
/// disputed batch. Both sides have a per-tx checkpoint sequence (the state root after each
/// tx); they disagree on the final root. By querying midpoints in log(N) rounds, the game
/// converges to a single disputed tx index — the verifier contract then only re-executes
/// that one tx instead of all N.
/// </summary>
/// <remarks>
/// Standard optimistic-rollup design. See doc.md §17 (sequencer-fraud mitigations).
/// <para>
/// The game itself is a pure state machine — no on-chain or signature semantics — so this
/// class can be unit-tested deterministically. Production wires it to a settlement contract
/// that records each step's outcome and enforces deadlines per round.
/// </para>
/// </remarks>
public sealed class BisectionGame
{
    private readonly UInt256[] _challengerCheckpoints;
    private readonly UInt256[] _sequencerCheckpoints;
    private readonly IL2Metrics _metrics;
    private int _lo;
    private int _hi;
    private int _rounds;
    private bool _settled;
    private int _disputedIndex = -1;

    /// <summary>Number of transactions in the disputed batch.</summary>
    public int TxCount { get; }

    /// <summary>Lowest index where parties might still agree (inclusive).</summary>
    public int Lo => _lo;

    /// <summary>Highest index where parties might still disagree (inclusive).</summary>
    public int Hi => _hi;

    /// <summary>How many bisection rounds have completed.</summary>
    public int Rounds => _rounds;

    /// <summary>True once <see cref="DisputedIndex"/> is the single atomic disputed tx.</summary>
    public bool IsSettled => _settled;

    /// <summary>The atomic disputed tx index (only valid when <see cref="IsSettled"/> is true).</summary>
    public int DisputedIndex => _settled ? _disputedIndex : throw new InvalidOperationException("game not settled");

    /// <summary>
    /// Construct with the two parties' per-tx checkpoint sequences.
    /// <paramref name="challengerCheckpoints"/>[i] = challenger's claimed state root after applying
    /// txs[0..i] starting from the agreed pre-state. <paramref name="sequencerCheckpoints"/> is
    /// the same shape from the sequencer.
    /// </summary>
    /// <remarks>
    /// Length-N+1 arrays: index 0 is the agreed pre-state; index N is the disputed post-state.
    /// </remarks>
    public BisectionGame(UInt256[] challengerCheckpoints, UInt256[] sequencerCheckpoints, IL2Metrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(challengerCheckpoints);
        ArgumentNullException.ThrowIfNull(sequencerCheckpoints);
        if (challengerCheckpoints.Length != sequencerCheckpoints.Length)
            throw new ArgumentException("checkpoint arrays must have the same length");
        if (challengerCheckpoints.Length < 2)
            throw new ArgumentException("need at least preState + postState (length ≥ 2)");
        if (!challengerCheckpoints[0].Equals(sequencerCheckpoints[0]))
            throw new ArgumentException("preState (index 0) must agree before bisection starts");
        if (challengerCheckpoints[^1].Equals(sequencerCheckpoints[^1]))
            throw new ArgumentException("postState (last index) must disagree to start a game");

        _challengerCheckpoints = challengerCheckpoints;
        _sequencerCheckpoints = sequencerCheckpoints;
        _metrics = metrics ?? NoOpMetrics.Instance;
        TxCount = challengerCheckpoints.Length - 1;
        _lo = 0;                              // agree at this index
        _hi = TxCount;                         // disagree at this index

        TrySettle();
    }

    /// <summary>
    /// Run one bisection round non-interactively (challenger and sequencer play themselves
    /// from the input checkpoint arrays). Useful for off-chain pre-validation before posting
    /// the on-chain bisection rounds.
    /// </summary>
    /// <returns>True if another round can advance; false when settled.</returns>
    public bool RunRound()
    {
        if (_settled) return false;

        var mid = _lo + (_hi - _lo) / 2;
        if (mid == _lo)
        {
            // [_lo, _hi] is adjacent (hi = lo + 1) → no further midpoint, atomic dispute.
            _disputedIndex = _lo;
            _settled = true;
            return false;
        }

        var challengerAtMid = _challengerCheckpoints[mid];
        var sequencerAtMid = _sequencerCheckpoints[mid];
        if (challengerAtMid.Equals(sequencerAtMid))
        {
            // Agreement at mid → dispute is in [mid, hi].
            _lo = mid;
        }
        else
        {
            // Disagreement at mid → dispute is in [lo, mid].
            _hi = mid;
        }
        _rounds++;
        TrySettle();
        return !_settled;
    }

    /// <summary>Run all rounds until the game settles; returns the disputed tx index.</summary>
    public int RunToSettlement()
    {
        while (RunRound()) { }
        return DisputedIndex;
    }

    private void TrySettle()
    {
        if (_settled) return;
        if (_hi - _lo <= 1)
        {
            _disputedIndex = _lo;
            _settled = true;
            _metrics.RecordHistogram(MetricNames.BisectionRounds, _rounds);
        }
    }

    /// <summary>
    /// Maximum number of rounds for an N-tx game: <c>ceil(log2(N))</c>.
    /// </summary>
    public static int MaxRoundsFor(int txCount)
    {
        if (txCount <= 1) return 0;
        var n = txCount;
        var rounds = 0;
        while (n > 1)
        {
            n = (n + 1) / 2;
            rounds++;
        }
        return rounds;
    }
}
