# 可观测性

`Neo.L2.Telemetry` 库以规范形式从每个 L2 插件采集指标,按需快照、渲染为 Prometheus
exposition 文本、再用 HTTP 暴露出来。它是不依赖 `prometheus-net` 包的等价替代品。

## 数据栈

```
plugins emit ──> IL2Metrics ──> InMemoryMetrics ──> Snapshot()
                                                      │
                                                      ▼
                                              PrometheusExporter
                                                      │
                                                      ▼
                                            MetricsRequestHandler
                                                      │
                                                      ▼
                                              MetricsHttpServer
                                                      │
                                                      ▼
                                          GET http://node/metrics
```

每一层都是独立可替换的:`IL2Metrics` 默认是 `NoOpMetrics`;快照是冻结的
`MetricsSnapshot`;请求 handler 框架无关(可塞进 ASP.NET / Kestrel / RpcServer);
HTTP server 用裸 `TcpListener` —— 无第三方依赖。

## 本地试一把

```bash
# 跑一个 3 批次 devnet,自抓 /metrics + /healthz + /readyz 真实 HTTP。
dotnet run --project tools/Neo.L2.Devnet -- 3 --metrics-port 9090
```

末尾的 `───── live HTTP /metrics ─────` 段展示了完整往返:状态码、Content-Type、
body 长度、Prometheus exposition 的开头几行。Port 0 让 OS 自选空闲端口。

## 端点

HTTP handler 答应 5 条路径(其它都是 404):

| 路径 | 状态 | 说明 |
|------|------|------|
| `GET /metrics` | 200 | Prometheus exposition 文本 |
| `GET /healthz` | 200 | 进程存活 —— server 在响应就一律 200 |
| `GET /readyz` | 200 / 503 | 接线了的判定;不传判定时 **503**(fail closed) |
| `GET /healthprobe` | 200 / 503 | 紧凑运维健康 JSON;未接线时 **503** |
| `GET /operatorstatus` | 200 / 503 | 全量 operator status JSON;未接线时 **503** |

`/healthz` 和 `/readyz` 沿用 Kubernetes 命名约定。`/healthz` 用作 liveness(失败则
重启),`/readyz` 用作 readiness(失败则从负载均衡摘出)。LocalHost `StartMetricsHttp`
把 `/readyz` 接到 offline passport,把 `/healthprobe` 接到 `FormatHealthProbeJson`
(camelCase `LocalHostHealthProbeDocument`),把 `/operatorstatus` 接到
`FormatOperatorStatusJsonAsync`(`LocalHostOperatorStatusDocument`)。Gateway 在
`Open*` 传入 `metricsPlugin:` 后可通过 `StartMetricsHttp` 提供同样三个端点
(全量状态为 `FormatOperatorStatusJson`)。Metrics HTTP
健康(`BuildMetricsHttpHealthFailures` / `IsMetricsHttpHealthy`)在 metrics 启用时要求
`HasMetricsReadinessCheck` + `HasMetricsHealthProbe` + `HasMetricsOperatorStatus`。
body 带标志、HTTP 仍 200,便于 `curl | jq`;不声称 L1 settle / prove-batch(funded 门禁)。例如:

```csharp
var handler = new MetricsRequestHandler(metrics, readinessCheck: () =>
    settlementClient.LastCanonicalBatchAge < TimeSpan.FromMinutes(2));
```

## 节点接线

`Neo.Plugins.L2Metrics` 插件是组合根 —— 它持有共享的 sink 和 HTTP server。

```csharp
using Neo.Plugins.L2;

// 1. 组合根。
var metrics = new L2MetricsPlugin();

// 2. 其它插件用 metrics.Metrics 调用 WithMetrics()。
var batchPlugin = new L2BatchPlugin();
batchPlugin.WithMetrics(metrics.Metrics);

var settlementPlugin = new L2SettlementPlugin();
settlementPlugin.Wire(batchPlugin, prover, settlementClient);
settlementPlugin.WithMetrics(metrics.Metrics);

var daPlugin = new L2DAPlugin();
daPlugin.WithMetrics(metrics.Metrics);

var bridgePlugin = new L2BridgePlugin();
bridgePlugin.WithMetrics(metrics.Metrics);

// 3. 可选:把 /readyz 接到一个真实判定上。
metrics.WithReadinessCheck(() => settlementClient.LastFinalizedBatchAge < TimeSpan.FromMinutes(2));

// 4. 启动 HTTP server(在 Configure() 加载完配置后)。
metrics.Start();

// curl http://127.0.0.1:9090/metrics
```

插件配置(在 `Neo.Plugins.L2Metrics/config.json`):

```json
{
  "PluginConfiguration": {
    "Enabled": true,
    "BindAddress": "127.0.0.1",
    "Port": 9090
  }
}
```

## 指标目录

下面每条指标都在 `MetricCatalog.Descriptions` 有对应条目。新增一条指标必须同时
新增 `MetricNames` 常量和目录条目 —— 测试套件用反射强制这一点。

### Batch(`Neo.Plugins.L2Batch`)

- `l2.batch.sealed` — counter — 本地排序器封装的 L2 批次数
- `l2.batch.seal_latency_ms` — histogram — 每个批次封装耗时(墙钟毫秒)
- `l2.batch.tx_count` — gauge — 最近一个封装批次的交易数
- `l2.batch.subscriber_failures` — counter — 派发 `OnBatchSealed` 时各订阅者的失败数(一个有 bug 的监听者不会拖垮区块导入)

### Settlement(`Neo.Plugins.L2Settlement`)

- `l2.settlement.submitted` — counter — 成功提交到 NeoHub 的批次数
- `l2.settlement.submit_failures` — counter — 提交抛错被重新入队的次数
- `l2.settlement.submit_latency_ms` — histogram — SubmitBatch 往返耗时(墙钟毫秒)

### Proving(由 Settlement 插件发出)

- `l2.proving.generated` — counter — `kind` — 为封装批次产生的证明数
- `l2.proving.latency_ms` — histogram — `kind` — 每个证明的产生耗时(墙钟毫秒)
- `l2.proving.rejected` — counter — — — 提交前被本地验证器拒绝的证明数

### Bridge(`Neo.L2.Bridge.{Deposit,Withdrawal}Processor`)

- `l2.bridge.deposits` — counter — 从 L1 桥 inbox 处理的充值数
- `l2.bridge.deposits_rejected` — counter — 校验拒绝的充值(重放 / 未知资产 / 失活映射)
- `l2.bridge.withdrawals` — counter — 入 L2 outbox 的提款数
- `l2.bridge.withdrawals_rejected` — counter — 校验拒绝的提款(未知资产 / 重复 nonce / 非正金额)

### DA(`Neo.Plugins.L2DA`)

`MetricsEmittingDAWriter` 装饰器套在每个 `IDAWriter` 外面,任何 DA 后备都自动参与
统计。

- `l2.da.published` — counter — `mode` — 成功发布的 DA payload 数
- `l2.da.publish_latency_ms` — histogram — `mode` — 每次 DA 发布耗时(墙钟毫秒)
- `l2.da.publish_failures` — counter — `mode` — 抛错的 DA 发布数

### RPC(`Neo.Plugins.L2Rpc.L2RpcMethods`)

- `l2.rpc.calls` — counter — `method` — L2 RPC 方法调用数
- `l2.rpc.latency_ms` — histogram — `method` — 每次 L2 RPC 调用耗时(墙钟毫秒)
- `l2.rpc.failures` — counter — `method` — 抛错的 L2 RPC 调用数

### Gateway(`Neo.Plugins.L2Gateway.BinaryTreeAggregator`)

- `l2.gateway.aggregations` — counter — 本地 gateway 完成的聚合次数
- `l2.gateway.batches_aggregated` — counter — 折入聚合的成员批次数(每次 +N)
- `l2.gateway.aggregation_rounds` — histogram — 两两归约的轮数(= log₂ N)
- `l2.gateway.aggregation_latency_ms` — histogram — 每次聚合的耗时(墙钟毫秒)

### 强制纳入 / 审查 / 挑战

- `l2.forced_inclusion.observed` — counter — 本节点观察到的强制纳入条目数
- `l2.censorship.reports` — counter — 检测器发出的审查报告数
- `l2.challenge.fraud_proofs` — counter — 编排器发出的欺诈证明数
- `l2.challenge.bisection_rounds` — histogram — 解决每次欺诈纠纷所用的二分轮数

### Sequencer(`Neo.L2.Sequencer.InMemorySequencerCommitteeProvider`)

- `l2.sequencer.registered` — counter — 加入委员会的排序器数
- `l2.sequencer.exits_started` — counter — 进入退出窗口的次数
- `l2.sequencer.exits_finalized` — counter — 窗口到期后被永久移除的次数
- `l2.sequencer.committee_size` — gauge — 当前委员会成员数(变更后)

### Messaging(`Neo.L2.Messaging.L2Outbox`)

- `l2.messaging.emitted` — counter — 本地 L2 发出的跨链消息数(按目标链打标签)

### Audit

- `l2.audit.runs` — counter — 链审计器运行次数
- `l2.audit.failures` — counter — 审计未通过的发现数

## Prometheus 渲染

名称按 1:1 映射 ——`.` 和 `-` 改写为 `_`。Counter 加 `_total` 后缀、`counter` 类型。
Gauge 保持原样、`gauge` 类型。Histogram 渲染为 `summary`,带 `_count` / `_sum` /
`_max` 聚合 —— 进程内 exporter 不带 quantile bucket。需要 quantile bucket 的话,接
OpenTelemetry exporter 到 `IL2Metrics` 上即可。

```
# HELP l2_batch_sealed_total Number of L2 batches sealed by the local sequencer
# TYPE l2_batch_sealed_total counter
l2_batch_sealed_total 4

# HELP l2_proving_generated_total Proofs generated for sealed batches, tagged by proof kind
# TYPE l2_proving_generated_total counter
l2_proving_generated_total{kind="Multisig"} 4

# HELP l2_batch_seal_latency_ms Wall-clock milliseconds spent sealing each batch
# TYPE l2_batch_seal_latency_ms summary
l2_batch_seal_latency_ms_count 4
l2_batch_seal_latency_ms_sum 8.45
l2_batch_seal_latency_ms_max 3.21
```

## 添加新指标

1. 在 `MetricNames` 加常量(例如 `public const string FooBar = "l2.foo.bar";`)。
2. 在 `MetricCatalog.Descriptions` 加描述。
   `Neo.L2.Telemetry.UnitTests` 里基于反射的完整性测试会在你忘了第 2 步时让构建失败。
3. 在相关组件里通过 `_metrics.IncrementCounter / RecordHistogram / SetGauge` 发出。
4. 用 `InMemoryMetrics` 写一条测试,断言数值能流出来。

## 关掉可观测性

默认 `IL2Metrics` 是 `NoOpMetrics.Instance` —— 发出指标只是一次空指针追逐,不分配
内存、不抢锁。没经 `WithMetrics()` 接线的插件用的是 no-op sink,部署时把 metrics
栈拿掉零成本。
