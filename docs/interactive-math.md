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

The current visual model includes a Proof Journey theater that follows one
execution claim from NeoVM semantics to RISC-V cycles, trace constraints,
commitments, proof generation, and L1 verification. It also includes a finite
field clock, a Merkle proof path view, a trace-to-constraint matrix, and a
prover/verifier transcript so readers can connect each mathematical object to
its role in the N4 architecture.

The ZKP animation panel expands the proof step into a concrete teaching model:
witness values are placed on a finite-field evaluation domain, constraints are
shown as gates that must vanish on that domain, the vanishing polynomial
`Z_H(x)` explains why divisibility matters, and the quotient check
`C(zeta) = Q(zeta) * Z_H(zeta)` shows how a verifier can use a random
Fiat-Shamir challenge instead of replaying the full trace.

Most visual elements are interactive: click or keyboard-focus journey nodes,
finite-field points, NeoVM instructions, ZKP gates, and transcript steps to move
the lesson state and explanation focus.

The page is intentionally static: no build step, no external network dependency,
and no wallet connection. The mathematical model lives in
`docs/interactive-math/mathModel.js` and is covered by
`node --test tests/interactive-math/math-model.test.mjs`.

<iframe src="./interactive-math/index.html" title="Neo N4 Math Lab" style="width: 100%; min-height: 860px; border: 1px solid #d6e1dc; border-radius: 8px;"></iframe>
