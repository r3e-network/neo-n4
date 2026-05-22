# neo-bridge-watcher-sol

<!-- N4-CRATE-VISUAL-GUIDE:START -->

## Crate Visual Learning Guide

These diagrams are local to this crate. They explain `neo-bridge-watcher-sol` as an independent unit: where it sits in the Neo N4 stack, which boundary it owns, how its internal workflow runs, and how data moves through it.

| View | Diagram | Source |
| --- | --- | --- |
| Position in Neo N4 | ![Position](docs/figures/position.svg) | [Mermaid](docs/figures/position.mmd) |
| Technical principles | ![Principles](docs/figures/principles.svg) | [Mermaid](docs/figures/principles.mmd) |
| Architecture | ![Architecture](docs/figures/architecture.svg) | [Mermaid](docs/figures/architecture.mmd) |
| Workflow | ![Workflow](docs/figures/workflow.svg) | [Mermaid](docs/figures/workflow.mmd) |
| Dataflow | ![Dataflow](docs/figures/dataflow.svg) | [Mermaid](docs/figures/dataflow.mmd) |

### Role in Neo N4

- **Layer:** Cross-chain watcher
- **Purpose:** Observes SOL bridge events and turns them into normalized Neo N4 relay messages.
- **Primary inputs:** SOL RPC/log stream, bridge contract events, checkpoint cursor
- **Primary outputs:** relay job, audit log, health metric
- **Downstream consumers:** gateway, shared bridge, operator dashboard

### Boundary and Responsibilities

- **Owns:** Filter bridge events, Normalize payloads, Protect replay/cursor state
- **Consumes:** SOL RPC/log stream, bridge contract events, checkpoint cursor
- **Produces:** relay job, audit log, health metric
- **Used by:** gateway, shared bridge, operator dashboard

### Learning Path

1. Start with the position diagram to understand why this crate exists and who calls it.
2. Read the technical principles diagram to identify the invariants and responsibility boundary.
3. Use the architecture diagram to connect public inputs, internal components, dependencies, and outputs.
4. Follow the workflow and dataflow diagrams before reading source files or tests.

<!-- N4-CRATE-VISUAL-GUIDE:END -->
