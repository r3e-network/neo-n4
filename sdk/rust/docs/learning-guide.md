# neo-n4-sdk Technical Learning Guide

This guide explains `neo-n4-sdk` as a Neo N4 technical unit. It is written for architecture learning: what the unit is responsible for, which assumptions make it correct, how data moves, how state changes, how evidence is checked, and where it plugs into the wider Neo N4 stack.

## Technical Contract

| Aspect | Meaning |
| --- | --- |
| Layer | Developer SDK |
| Purpose | Rust client SDK for building tools and services that talk to Neo N4 APIs. |
| Inputs | developer app <br> gateway endpoint <br> wallet/config |
| Responsibilities | Encode API requests <br> Handle bridge/proof models <br> Return typed results |
| Outputs | typed client result <br> transaction request <br> query response |
| Consumers | apps <br> operators <br> integration tests |

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

`neo-n4-sdk` receives developer app | gateway endpoint | wallet/config and owns this boundary: Encode API requests | Handle bridge/proof models | Return typed results. It emits typed client result | transaction request | query response, which are consumed by apps | operators | integration tests.

Layering rule: interface, encoding, network submission, and diagnostics are separated.

## Workflow

1. Create client
2. Build request
3. Sign or query
4. Submit to gateway
5. Decode response

Failure path: invalid config, signing-policy failure, network rejection, or output validation failure.

## Data Flow

1. app intent
2. SDK model
3. RPC payload
4. Neo N4 service response

Commitment signal: request digest, network profile, and output artifact hash.

## State, Proof, and Trust

- State transition: requests, configs, packages, or reports advance through checked stages.
- Finality: target service accepts request and returns traceable result.
- Trust model: trust validation and signing policy, not unchecked user input.
- Validation boundary: config, params, signing, network, and output format validate.
- Replay and ordering: request id, network id, and signing domain prevent duplicate or cross-network use.

## Integration and Operation

- NeoFS DA: NeoFS stores batch data, witness or trace summaries, and retrievable evidence.
- Proof system: The proof system compresses L2 execution claims into verifiable evidence.
- Gateway/API: Gateway handles user routing, queries, submission, and health aggregation.
- Bridge and heterogeneous chains: Bridge rules unify L1-L2, L2-L2, and heterogeneous-chain messages and assets.
- Observable evidence: command, config digest, network, output artifacts, and diagnostics.

Regenerate these technical diagrams from the Neo N4 repository root with:

```powershell
python tools/docs/generate_crate_visual_docs.py
```
