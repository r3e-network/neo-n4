# neo-zkvm-guest Technical Learning Guide

This guide explains `neo-zkvm-guest` as a Neo N4 technical unit. It is written for architecture learning: what the unit is responsible for, which assumptions make it correct, how data moves, how state changes, how evidence is checked, and where it plugs into the wider Neo N4 stack.

## Technical Contract

| Aspect | Meaning |
| --- | --- |
| Layer | N4 zk guest |
| Purpose | SP1 guest program that runs deterministic Neo L2 batch execution inside the proof circuit. |
| Inputs | public batch input <br> private witness <br> shared execution core |
| Responsibilities | Run verifiable transition <br> Emit public values <br> Reject nondeterminism |
| Outputs | SP1 public output <br> state root <br> execution digest |
| Consumers | neo-zkvm-host <br> NativeZkVerifier adapter <br> audit tooling |

## Diagram Set

| # | Diagram | What to learn |
| --- | --- | --- |
| 1 | [System Position](figures/position.svg) | where this crate sits in Neo N4. |
| 2 | [Technical Principles](figures/principles.svg) | the rules that make the design correct. |
| 3 | [Conceptual Architecture](figures/architecture.svg) | major technical blocks and boundaries. |
| 4 | [Workflow](figures/workflow.svg) | the ordered runtime process. |
| 5 | [Data Flow](figures/dataflow.svg) | how information, commitments, and evidence move. |
| 6 | [State Model](figures/state-model.svg) | state ownership, transitions, and finality. |
| 7 | [Proof and Evidence Flow](figures/proof-flow.svg) | how claims become verifiable evidence. |
| 8 | [Trust Boundaries](figures/trust-boundaries.svg) | what is trusted, checked, rejected, or observed. |
| 9 | [Integration Map](figures/integration-map.svg) | how this unit connects to the wider N4 stack. |
| 10 | [Runtime Lifecycle](figures/lifecycle.svg) | from configuration through execution, evidence, and operation. |

## Architecture Model

`neo-zkvm-guest` receives public batch input | private witness | shared execution core and owns this boundary: Run verifiable transition | Emit public values | Reject nondeterminism. It emits SP1 public output | state root | execution digest, which are consumed by neo-zkvm-host | NativeZkVerifier adapter | audit tooling.

Layering rule: guest proves computation, host orchestrates, L1 verifies compact results.

## Workflow

1. Deserialize input
2. Execute batch
3. Commit public values
4. Exit guest

Failure path: proving fails, local verification fails, public output mismatches, or verifier rejects.

## Data Flow

1. witness + batch
2. guest executor
3. public values
4. proof artifact

Commitment signal: state root, public values, verification key, and proof digest.

## State, Proof, and Trust

- State transition: guest execution is constrained by public values and verifier rules.
- Finality: verifier accepts proof and public output matches target state.
- Trust model: trust verification keys and verifiers, not prover runtime environments.
- Validation boundary: public input, proof envelope, verification key, and public output must match.
- Replay and ordering: proof binds batch range and state root to prevent cross-batch reuse.

## Integration and Operation

- NeoFS DA: NeoFS stores batch data, witness or trace summaries, and retrievable evidence.
- Proof system: The proof system compresses L2 execution claims into verifiable evidence.
- Gateway/API: Gateway handles user routing, queries, submission, and health aggregation.
- Bridge and heterogeneous chains: Bridge rules unify L1-L2, L2-L2, and heterogeneous-chain messages and assets.
- Observable evidence: proof id, public output, verification result, duration, and failure reason.

Regenerate these technical diagrams from the Neo N4 repository root with:

```powershell
python tools/docs/generate_crate_visual_docs.py
```
