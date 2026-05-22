# neo-zkvm-host Technical Learning Guide

This guide explains `neo-zkvm-host` as a Neo N4 technical unit. It is written for architecture learning: what the unit is responsible for, which assumptions make it correct, how data moves, how state changes, how evidence is checked, and where it plugs into the wider Neo N4 stack.

## Technical Contract

| Aspect | Meaning |
| --- | --- |
| Layer | N4 zk host |
| Purpose | Host-side SP1 prover orchestration for creating and checking L2 batch proofs. |
| Inputs | L2 batch <br> guest ELF <br> prover configuration |
| Responsibilities | Prepare SP1 stdin <br> Run prover <br> Verify proof envelope |
| Outputs | proof bytes <br> verification report <br> state commitment |
| Consumers | bridge relayer <br> L1 verifier adapter <br> devnet scripts |

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

`neo-zkvm-host` receives L2 batch | guest ELF | prover configuration and owns this boundary: Prepare SP1 stdin | Run prover | Verify proof envelope. It emits proof bytes | verification report | state commitment, which are consumed by bridge relayer | L1 verifier adapter | devnet scripts.

Layering rule: guest proves computation, host orchestrates, L1 verifies compact results.

## Workflow

1. Load ELF
2. Encode input
3. Prove
4. Verify locally
5. Export report

Failure path: proving fails, local verification fails, public output mismatches, or verifier rejects.

## Data Flow

1. batch data
2. SP1 host
3. proof + vk
4. onchain verifier input

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
