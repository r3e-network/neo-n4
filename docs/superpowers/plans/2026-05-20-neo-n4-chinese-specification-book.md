# Neo N4 Chinese Specification Book Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a reader-friendly Chinese Neo N4 specification book that teaches the design, architecture, implementation, operation, and verification model from first principles through code.

**Architecture:** Keep the book in `docs/zh/specification/` so it is a first-class Chinese documentation artifact. The book is a learning spine that links to existing deep-dive docs instead of duplicating every existing page; each chapter includes diagrams, tables, definitions, implementation pointers, and small code excerpts.

**Tech Stack:** mdBook Markdown, Mermaid diagrams, existing SVG/PNG figures, NeoHub deployable contracts, r3e Neo core fork, NeoVM2/RISC-V execution, NeoFS DA, SP1 zkVM, .NET 10, Rust, TypeScript SDK.

---

### Task 1: Add the Book Structure

**Files:**
- Create: `docs/zh/specification/README.md`
- Create: `docs/zh/specification/01-system-model.md`
- Create: `docs/zh/specification/02-architecture.md`
- Create: `docs/zh/specification/03-protocol-data.md`
- Create: `docs/zh/specification/04-implementation.md`
- Create: `docs/zh/specification/05-operations.md`
- Create: `docs/zh/specification/06-security-testing.md`
- Create: `docs/zh/specification/07-reading-path.md`

- [ ] Add a preface, learning map, system definitions, and chapter index.
- [ ] Add a system model chapter explaining L1, NeoHub, Gateway, L2, DA, prover, bridge, and watchers.
- [ ] Add an architecture chapter with images and layered diagrams.
- [ ] Add a protocol/data chapter covering core records and flows.
- [ ] Add an implementation chapter mapping source folders to responsibilities.
- [ ] Add an operations chapter covering local devnet, testnet deployment, and operator workflow.
- [ ] Add a security/testing chapter covering trust boundaries, audit gates, and test coverage.
- [ ] Add a reading path chapter for operators, implementers, auditors, and app developers.

### Task 2: Connect the Book to mdBook

**Files:**
- Modify: `docs/SUMMARY.md`
- Modify: `docs/zh/SUMMARY.md`
- Modify: `docs/zh/architecture-atlas.md`

- [ ] Add the Chinese specification book to both mdBook navigation files.
- [ ] Add the specification book to the Chinese architecture atlas as the recommended long-form path.

### Task 3: Validate Documentation

**Files:**
- Test: `docs/**/*.md`
- Test: `tests/Neo.Hub.Deploy.UnitTests/UT_ProductionGapClosure.cs`

- [ ] Run `wsl bash -lc 'cd /mnt/d/Git/neo-n4 && mdbook build'`.
- [ ] Run `dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`.
- [ ] Run `git diff --check`.
