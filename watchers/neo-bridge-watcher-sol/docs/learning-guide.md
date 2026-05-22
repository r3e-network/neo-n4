# neo-bridge-watcher-sol Technical Learning Guide

This guide explains `neo-bridge-watcher-sol` as a Neo N4 technical unit. It is written for architecture learning: what the unit is responsible for, which assumptions make it correct, how data moves, how state changes, how evidence is checked, and where it plugs into the wider Neo N4 stack.

## Technical Contract

| Aspect | Meaning |
| --- | --- |
| Layer | Cross-chain watcher |
| Purpose | Observes SOL bridge events and turns them into normalized Neo N4 relay messages. |
| Inputs | SOL RPC/log stream <br> bridge contract events <br> checkpoint cursor |
| Responsibilities | Filter bridge events <br> Normalize payloads <br> Protect replay/cursor state |
| Outputs | relay job <br> audit log <br> health metric |
| Consumers | gateway <br> shared bridge <br> operator dashboard |

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

`neo-bridge-watcher-sol` receives SOL RPC/log stream | bridge contract events | checkpoint cursor and owns this boundary: Filter bridge events | Normalize payloads | Protect replay/cursor state. It emits relay job | audit log | health metric, which are consumed by gateway | shared bridge | operator dashboard.

Layering rule: watching, message normalization, asset state, and final verification are separated.

## Workflow

1. Poll source chain
2. Decode logs
3. Validate confirmations
4. Emit relay job
5. Persist cursor

Failure path: insufficient confirmations, nonce replay, message mismatch, or invalid asset state.

## Data Flow

1. source log
2. watcher
3. normalized event
4. bridge message

Commitment signal: message hash, nonce, source height, and asset action.

## State, Proof, and Trust

- State transition: events become normalized messages and then asset/state actions.
- Finality: message is consumed and nonce is marked used.
- Trust model: trust confirmation and verification rules, not one watcher or RPC endpoint.
- Validation boundary: event, confirmations, message hash, nonce, and asset state all pass.
- Replay and ordering: nonce, source height, and message hash provide ordering and replay protection.

## Integration and Operation

- NeoFS DA: NeoFS stores batch data, witness or trace summaries, and retrievable evidence.
- Proof system: The proof system compresses L2 execution claims into verifiable evidence.
- Gateway/API: Gateway handles user routing, queries, submission, and health aggregation.
- Bridge and heterogeneous chains: Bridge rules unify L1-L2, L2-L2, and heterogeneous-chain messages and assets.
- Observable evidence: source event, confirmations, message hash, cursor, submit hash, and final state.

Regenerate these technical diagrams from the Neo N4 repository root with:

```powershell
python tools/docs/generate_crate_visual_docs.py
```
