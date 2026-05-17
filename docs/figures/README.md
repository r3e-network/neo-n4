# Figures

Hand-tuned SVG figures used across `README.md`, `WHITEPAPER.md`, and
`docs/architecture-walkthrough.md`. Vector graphics — render identically
across browsers, fonts, and zoom levels.

| Figure | File | Used in |
| ------ | ---- | ------- |
| **1** — Architecture overview | [`architecture.svg`](architecture.svg) | [`README.md`](../../README.md#architecture-at-a-glance) · [`WHITEPAPER.md`](../../WHITEPAPER.md) · [`architecture-walkthrough.md`](../architecture-walkthrough.md#layered-diagram) |
| **2** — Transaction lifecycle | [`tx-lifecycle.svg`](tx-lifecycle.svg) | [`architecture-walkthrough.md`](../architecture-walkthrough.md#walk-1-a-transactions-life-on-an-l2-chain) (Walk #1) |
| **3** — Multi-L2 proof aggregation | [`proof-aggregation.svg`](proof-aggregation.svg) | [`architecture-walkthrough.md`](../architecture-walkthrough.md#walk-3-multi-l2-proof-aggregation-phase-5) (Walk #3) |
| **4** — Forced inclusion + slashing | [`forced-inclusion.svg`](forced-inclusion.svg) | [`architecture-walkthrough.md`](../architecture-walkthrough.md#walk-2-anti-censorship-via-forced-inclusion) (Walk #2) |
| **5** — Telemetry pipeline | [`telemetry-pipeline.svg`](telemetry-pipeline.svg) | [`architecture-walkthrough.md`](../architecture-walkthrough.md#walk-4-telemetry--emit-snapshot-scrape) (Walk #4) |
| **6** — Trust spectrum (security levels) | [`trust-spectrum.svg`](trust-spectrum.svg) | [`security-model.md`](../security-model.md#per-chain-security-labels) |

## Visual guide pack

The `visual-guide/` directory contains generated SVGs used by
[`visual-guide.md`](../visual-guide.md). They cover system context, module maps,
bridge flows, deployment pipeline, data structures, trust boundaries, watcher
state, verification matrix, and operator runbook views. Regenerate with:

```powershell
docs/figures/visual-guide/generate.ps1
```

## Visual conventions

All figures share a common shape language so they read as a single set:

- **Layer / band colors** — L1 (indigo `#eff3ff` / `#7587c1`), Gateway (purple
  `#f5edff` / `#9072c6`), L2 (green `#ecf5ee` / `#6fa37b`), adversarial /
  detector flow (amber-red `#fff7ed` / `#d97706` and `#fee2e2` / `#dc2626`).
- **Components** — rounded rectangles (`rx=6`), 1.1–1.4px borders, subtle drop
  shadow (`feDropShadow dx=0 dy=1 stdDeviation=0.9 flood-opacity=0.07`).
- **Edges** — solid arrows for forward calls, dashed arrows for return values
  or external/optional links, consistent arrowhead marker.
- **Labels** — italic edge labels in `#525a64`, bold component titles, italic
  attribution tags in `#6e7781`.
- **Captions** — single-sentence figure caption at the bottom in `#57606a`.

## Editing

The figures are hand-written SVG (no Inkscape / Figma export). To tweak
positioning or copy, edit the `<svg>` directly — every `x`/`y` is explicit.
Re-rendering not required; GitHub serves them as-is.
