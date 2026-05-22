# neo-execution-core Technical Learning Guide

This guide explains `neo-execution-core` as a Neo N4 technical unit. It is written for architecture learning: what the unit is responsible for, which assumptions make it correct, how data moves, how state changes, how evidence is checked, and where it plugs into the wider Neo N4 stack.

## Technical Contract

| Aspect | Meaning |
| --- | --- |
| Layer | N4 batch execution core |
| Purpose | Backend-neutral L2 batch transition primitives shared by fast execution and proof generation. |
| Inputs | L2 batch <br> previous state root <br> execution parameters |
| Responsibilities | Validate batch shape <br> Apply deterministic transition <br> Commit new state root |
| Outputs | execution trace <br> new state root <br> public proof inputs |
| Consumers | neo-zkvm-guest <br> neo-zkvm-host <br> gateway services |

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

`neo-execution-core` receives L2 batch | previous state root | execution parameters and owns this boundary: Validate batch shape | Apply deterministic transition | Commit new state root. It emits execution trace | new state root | public proof inputs, which are consumed by neo-zkvm-guest | neo-zkvm-host | gateway services.

Layering rule: each layer carries one clear technical responsibility.

## Workflow

1. Accept batch
2. Normalize inputs
3. Run transition
4. Hash outputs
5. Publish evidence

Failure path: input violates constraints or output cannot be verified.

## Data Flow

1. transactions
2. execution core
3. trace + commitments
4. proof/public output

Commitment signal: input digest, transition digest, and output digest.

## State, Proof, and Trust

- State transition: explicit rules turn accepted input into checkable output.
- Finality: consumer accepts the output.
- Trust model: trust only data that crossed validation boundaries.
- Validation boundary: all inputs cross explicit validation boundaries.
- Replay and ordering: input and output digests bind the same context.

## Integration and Operation

- NeoFS DA: NeoFS stores batch data, witness or trace summaries, and retrievable evidence.
- Proof system: The proof system compresses L2 execution claims into verifiable evidence.
- Gateway/API: Gateway handles user routing, queries, submission, and health aggregation.
- Bridge and heterogeneous chains: Bridge rules unify L1-L2, L2-L2, and heterogeneous-chain messages and assets.
- Observable evidence: input digest, output digest, state change, and rejection reason.

Regenerate these technical diagrams from the Neo N4 repository root with:

```powershell
python tools/docs/generate_crate_visual_docs.py
```
