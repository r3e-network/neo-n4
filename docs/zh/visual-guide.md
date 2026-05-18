# 可视化导览

这页把 Neo N4 的系统架构、模块分布、桥接流程、部署管线、数据结构、信任边界和验证矩阵整理成一组 SVG 图。图内标签采用英文技术名，正文用中文解释，方便和代码、CLI、合约名直接对应。

## 阅读顺序

- 先看全局：**System context**。
- 找代码位置：**Code module map**、**Source to artifact map**。
- 理解桥：**Deposit flow**、**Withdrawal flow**、**External bridge data flow**。
- 准备上线：**Deployment pipeline**、**Operator runbook flow**。
- 做审计：**Core data structures**、**Trust boundary map**、**Verification matrix**。

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

## 重新生成

架构变化后，从仓库根目录运行：

```powershell
docs/figures/visual-guide/generate.ps1
```
