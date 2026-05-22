# Interactive Neo N4 Math Lab

Open the interactive tutorial:

[Launch the Neo N4 Math Lab](./interactive-math/index.html)

The Math Lab is a local-first dynamic tutorial for the mathematical ideas behind
the NeoVM -> NeoVM2/RISC-V -> zkVM stack. It is written for readers who are not
cryptography specialists but need to understand what Neo N4 proves and why the
proof boundary is sound.

It covers:

- finite-field arithmetic and modular equality;
- hash commitments, Merkle roots, and inclusion paths;
- NeoVM stack-machine transition semantics;
- NeoVM2/RISC-V register, memory, and pc-cycle execution;
- execution traces and arithmetization;
- zkVM proof verification and public inputs;
- proof aggregation and L1 settlement policy;
- NeoFS DA, bridge accounting, and replay-resistant N4 security checks.

The page is intentionally static: no build step, no external network dependency,
and no wallet connection. The mathematical model lives in
`docs/interactive-math/mathModel.js` and is covered by
`node --test tests/interactive-math/math-model.test.mjs`.

<iframe src="./interactive-math/index.html" title="Neo N4 Math Lab" style="width: 100%; min-height: 860px; border: 1px solid #d6e1dc; border-radius: 8px;"></iframe>
