# Visual guide / 可视化导览

这页把 Neo N4 的系统、模块、流程、数据结构和验证面压缩成一组可直接阅读的 SVG 图。适合给新贡献者、运维者、审计者和钱包/SDK 集成方快速建立上下文。

## How to read this page

- 想先理解“有哪些参与方”：看 **System context**。
- 想找代码：看 **Code module map** 和 **Source to artifact map**。
- 想理解桥：看 **Deposit flow**、**Withdrawal flow**、**External bridge data flow**。
- 想上线：看 **Deployment pipeline** 和 **Operator runbook flow**。
- 想审计：看 **Core data structures**、**Trust boundary map**、**Verification matrix**。

## System Context

![Neo N4 system context](figures/visual-guide/system-context.svg)

## Code Module Map

![Neo N4 code module map](figures/visual-guide/module-map.svg)

## L1 To L2 Deposit Flow

![L1 to L2 deposit flow](figures/visual-guide/deposit-flow.svg)

## L2 To L1 Withdrawal Flow

![L2 to L1 withdrawal flow](figures/visual-guide/withdrawal-flow.svg)

## Batch Settlement Lifecycle

![Batch settlement lifecycle](figures/visual-guide/batch-lifecycle.svg)

## External Bridge Data Flow

![External bridge data flow](figures/visual-guide/external-bridge-flow.svg)

## Production Deployment Pipeline

![Production deployment pipeline](figures/visual-guide/deployment-pipeline.svg)

## Core Data Structures

![Core data structures](figures/visual-guide/data-structures.svg)

## Trust Boundary Map

![Trust boundary map](figures/visual-guide/trust-boundaries-map.svg)

## Watcher Daemon State Machine

![Watcher daemon state machine](figures/visual-guide/watcher-state-machine.svg)

## Verification Matrix

![Verification matrix](figures/visual-guide/testing-matrix.svg)

## Operator Runbook Flow

![Operator runbook flow](figures/visual-guide/operator-runbook.svg)

## Source To Artifact Map

![Source to artifact map](figures/visual-guide/project-artifact-map.svg)

## Regenerating

The figures are generated from:

```powershell
docs/figures/visual-guide/generate.ps1
```

Run it from the repository root when the architecture changes.
