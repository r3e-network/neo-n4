# neo-external-bridge-router

<!-- N4-CRATE-VISUAL-GUIDE:START -->

## Crate Visual Learning Guide

These diagrams are local to this crate. They explain `neo-external-bridge-router` as an independent unit: where it sits in the Neo N4 stack, which boundary it owns, how its internal workflow runs, and how data moves through it.

| View | Diagram | Source |
| --- | --- | --- |
| Position in Neo N4 | ![Position](docs/figures/position.svg) | [Mermaid](docs/figures/position.mmd) |
| Technical principles | ![Principles](docs/figures/principles.svg) | [Mermaid](docs/figures/principles.mmd) |
| Architecture | ![Architecture](docs/figures/architecture.svg) | [Mermaid](docs/figures/architecture.mmd) |
| Workflow | ![Workflow](docs/figures/workflow.svg) | [Mermaid](docs/figures/workflow.mmd) |
| Dataflow | ![Dataflow](docs/figures/dataflow.svg) | [Mermaid](docs/figures/dataflow.mmd) |

### Role in Neo N4

- **Layer:** Foreign-chain bridge program
- **Purpose:** Solana-side bridge router that represents Neo N4 cross-chain lock, mint, burn, and unlock flows.
- **Primary inputs:** Solana instruction, token account state, bridge authority
- **Primary outputs:** bridge event, escrow mutation, relay evidence
- **Downstream consumers:** watcher-sol, gateway, shared bridge

### Boundary and Responsibilities

- **Owns:** Validate route, Move escrowed assets, Emit bridge event
- **Consumes:** Solana instruction, token account state, bridge authority
- **Produces:** bridge event, escrow mutation, relay evidence
- **Used by:** watcher-sol, gateway, shared bridge

### Learning Path

1. Start with the position diagram to understand why this crate exists and who calls it.
2. Read the technical principles diagram to identify the invariants and responsibility boundary.
3. Use the architecture diagram to connect public inputs, internal components, dependencies, and outputs.
4. Follow the workflow and dataflow diagrams before reading source files or tests.

<!-- N4-CRATE-VISUAL-GUIDE:END -->
