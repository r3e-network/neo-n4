# neo-external-bridge-router Technical Learning Guide

This guide explains `neo-external-bridge-router` as a Neo N4 technical unit. It is written for architecture learning: what the unit is responsible for, which assumptions make it correct, how data moves, how state changes, how evidence is checked, and where it plugs into the wider Neo N4 stack.

## Technical Contract

| Aspect | Meaning |
| --- | --- |
| Layer | Foreign-chain bridge program |
| Purpose | Solana-side bridge router that represents Neo N4 cross-chain lock, mint, burn, and unlock flows. |
| Inputs | Solana instruction <br> token account state <br> bridge authority |
| Responsibilities | Validate route <br> Move escrowed assets <br> Emit bridge event |
| Outputs | bridge event <br> escrow mutation <br> relay evidence |
| Consumers | watcher-sol <br> gateway <br> shared bridge |

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

`neo-external-bridge-router` receives Solana instruction | token account state | bridge authority and owns this boundary: Validate route | Move escrowed assets | Emit bridge event. It emits bridge event | escrow mutation | relay evidence, which are consumed by watcher-sol | gateway | shared bridge.

Layering rule: watching, message normalization, asset state, and final verification are separated.

## Workflow

1. Receive instruction
2. Check accounts
3. Update escrow
4. Emit event
5. Watcher relays

Failure path: insufficient confirmations, nonce replay, message mismatch, or invalid asset state.

## Data Flow

1. instruction data
2. router program
3. event log
4. Neo N4 bridge message

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
