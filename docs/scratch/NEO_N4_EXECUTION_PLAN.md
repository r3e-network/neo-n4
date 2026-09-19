# Neo N4 执行计划：从今天开始

**启动日期**: 2026年09月16日  
**目标**: 3-4个月内完成生产就绪  
**状态**: 🚀 Ready to Execute  

---

## 🎯 Phase 1: 立即启动（Week 1-2，2026-09-16 开始）

### Day 1-3: 监控基础设施搭建

#### Task 1.1: 部署Prometheus + Grafana
```bash
# 创建监控目录
mkdir -p monitoring/{prometheus,grafana,alertmanager}

# prometheus.yml
cat > monitoring/prometheus/prometheus.yml <<EOF
global:
  scrape_interval: 15s
  evaluation_interval: 15s

alerting:
  alertmanagers:
    - static_configs:
        - targets: ['alertmanager:9093']

scrape_configs:
  - job_name: 'neo-n4-sequencer'
    static_configs:
      - targets: ['sequencer:9090']
    metrics_path: '/metrics'
    
  - job_name: 'neo-n4-prover'
    static_configs:
      - targets: ['prover:9091']
      
  - job_name: 'neo-n4-da-publisher'
    static_configs:
      - targets: ['da-publisher:9092']
      
  - job_name: 'neo-n4-settlement'
    static_configs:
      - targets: ['settlement:9093']
EOF

# docker-compose.yml
cat > monitoring/docker-compose.yml <<EOF
version: '3.8'
services:
  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus:/etc/prometheus
      - prometheus-data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      
  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
    volumes:
      - grafana-data:/var/lib/grafana
      - ./grafana/dashboards:/etc/grafana/provisioning/dashboards
      
  alertmanager:
    image: prom/alertmanager:latest
    ports:
      - "9093:9093"
    volumes:
      - ./alertmanager:/etc/alertmanager

volumes:
  prometheus-data:
  grafana-data:
EOF

# 启动监控栈
cd monitoring && docker-compose up -d
```

**负责人**: DevOps工程师  
**交付**: Prometheus + Grafana运行，可访问 http://localhost:3000  
**检查点**: Day 3晚，验证指标收集正常

---

#### Task 1.2: 实现Prometheus指标埋点

创建文件：`src/Neo.L2.Telemetry/PrometheusMetrics.cs`

```csharp
using Prometheus;

namespace Neo.L2.Telemetry;

/// <summary>
/// Prometheus指标定义 - 所有L2组件共享
/// </summary>
public static class Neo4Metrics
{
    // === Batch相关指标 ===
    
    /// <summary>批次密封总数</summary>
    public static readonly Counter BatchesSealed = Metrics
        .CreateCounter("neo4_batches_sealed_total", 
            "Total number of batches sealed",
            new CounterConfiguration { LabelNames = new[] { "chain_id" } });
    
    /// <summary>批次中的交易数</summary>
    public static readonly Histogram BatchTransactionCount = Metrics
        .CreateHistogram("neo4_batch_tx_count", 
            "Number of transactions per batch",
            new HistogramConfiguration { 
                LabelNames = new[] { "chain_id" },
                Buckets = new[] { 10, 50, 100, 500, 1000, 5000 }
            });
    
    /// <summary>批次密封延迟（秒）</summary>
    public static readonly Histogram BatchSealLatency = Metrics
        .CreateHistogram("neo4_batch_seal_latency_seconds", 
            "Time from first tx to batch seal",
            new HistogramConfiguration { 
                LabelNames = new[] { "chain_id" },
                Buckets = new[] { 1, 5, 10, 30, 60, 300, 600 }
            });
    
    // === 证明相关指标 ===
    
    /// <summary>证明生成总数</summary>
    public static readonly Counter ProofsGenerated = Metrics
        .CreateCounter("neo4_proofs_generated_total", 
            "Total number of proofs generated",
            new CounterConfiguration { LabelNames = new[] { "chain_id", "proof_type" } });
    
    /// <summary>证明生成失败数</summary>
    public static readonly Counter ProofGenerationFailures = Metrics
        .CreateCounter("neo4_proof_generation_failures_total", 
            "Total number of proof generation failures",
            new CounterConfiguration { LabelNames = new[] { "chain_id", "reason" } });
    
    /// <summary>证明生成时间（秒）</summary>
    public static readonly Histogram ProofGenerationDuration = Metrics
        .CreateHistogram("neo4_proof_generation_seconds", 
            "Time to generate a proof",
            new HistogramConfiguration { 
                LabelNames = new[] { "chain_id", "proof_type" },
                Buckets = new[] { 10, 30, 60, 120, 300, 600, 1800 }
            });
    
    /// <summary>证明队列深度</summary>
    public static readonly Gauge ProofQueueDepth = Metrics
        .CreateGauge("neo4_proof_queue_depth", 
            "Number of batches waiting for proof",
            new GaugeConfiguration { LabelNames = new[] { "chain_id" } });
    
    // === N3结算相关指标 ===
    
    /// <summary>N3结算提交总数</summary>
    public static readonly Counter N3SettlementsSubmitted = Metrics
        .CreateCounter("neo4_n3_settlements_submitted_total", 
            "Total number of settlements submitted to N3",
            new CounterConfiguration { LabelNames = new[] { "chain_id" } });
    
    /// <summary>N3结算失败数</summary>
    public static readonly Counter N3SettlementFailures = Metrics
        .CreateCounter("neo4_n3_settlement_failures_total", 
            "Total number of N3 settlement failures",
            new CounterConfiguration { LabelNames = new[] { "chain_id", "reason" } });
    
    /// <summary>N3结算确认延迟</summary>
    public static readonly Histogram N3SettlementConfirmationLatency = Metrics
        .CreateHistogram("neo4_n3_settlement_confirmation_seconds", 
            "Time from submission to N3 confirmation",
            new HistogramConfiguration { 
                LabelNames = new[] { "chain_id" },
                Buckets = new[] { 15, 30, 60, 120, 300, 600 }
            });
    
    // === DA相关指标 ===
    
    /// <summary>DA发布总数</summary>
    public static readonly Counter DaPublished = Metrics
        .CreateCounter("neo4_da_published_total", 
            "Total number of DA publications",
            new CounterConfiguration { LabelNames = new[] { "chain_id", "da_mode" } });
    
    /// <summary>DA发布失败数</summary>
    public static readonly Counter DaPublishFailures = Metrics
        .CreateCounter("neo4_da_publish_failures_total", 
            "Total number of DA publish failures",
            new CounterConfiguration { LabelNames = new[] { "chain_id", "da_mode", "reason" } });
    
    /// <summary>DA数据大小（字节）</summary>
    public static readonly Histogram DaDataSize = Metrics
        .CreateHistogram("neo4_da_data_bytes", 
            "Size of data published to DA",
            new HistogramConfiguration { 
                LabelNames = new[] { "chain_id", "da_mode" },
                Buckets = new[] { 1024, 10240, 102400, 1048576, 10485760 }
            });
    
    // === 系统健康指标 ===
    
    /// <summary>当前状态根</summary>
    public static readonly Gauge CurrentStateRoot = Metrics
        .CreateGauge("neo4_current_state_root_block", 
            "Current state root block number",
            new GaugeConfiguration { LabelNames = new[] { "chain_id" } });
    
    /// <summary>委员会节点状态</summary>
    public static readonly Gauge CommitteeNodeHealth = Metrics
        .CreateGauge("neo4_committee_node_health", 
            "Committee node health (1=healthy, 0=unhealthy)",
            new GaugeConfiguration { LabelNames = new[] { "node_id", "node_address" } });
}
```

**负责人**: 核心开发工程师  
**交付**: 指标定义完成，集成到现有代码  
**检查点**: Day 3，代码review通过

---

#### Task 1.3: 集成指标到现有组件

修改文件：`src/Neo.Plugins.L2Batch/L2BatchPlugin.cs`

```csharp
// 在OnBatchSealed事件处理中添加
private void OnBatchSealed(object? sender, SealedBatch batch)
{
    // 🔥 添加Prometheus指标
    Neo4Metrics.BatchesSealed.WithLabels(batch.ChainId.ToString()).Inc();
    Neo4Metrics.BatchTransactionCount
        .WithLabels(batch.ChainId.ToString())
        .Observe(batch.TransactionCount);
    
    var sealLatency = (DateTime.UtcNow - batch.FirstTransactionTime).TotalSeconds;
    Neo4Metrics.BatchSealLatency
        .WithLabels(batch.ChainId.ToString())
        .Observe(sealLatency);
    
    // 原有逻辑...
}
```

修改文件：`src/Neo.L2.Proving/RiscVZk/Sp1BatchProofProver.cs`

```csharp
public async ValueTask<ProofResult> ProveAsync(
    ProofRequest request,
    CancellationToken cancellationToken = default)
{
    var stopwatch = Stopwatch.StartNew();
    
    try
    {
        // 原有证明逻辑...
        var result = await GenerateProofInternalAsync(request, cancellationToken);
        
        // 🔥 记录成功指标
        Neo4Metrics.ProofsGenerated
            .WithLabels(request.ChainId.ToString(), "zk")
            .Inc();
        Neo4Metrics.ProofGenerationDuration
            .WithLabels(request.ChainId.ToString(), "zk")
            .Observe(stopwatch.Elapsed.TotalSeconds);
        
        return result;
    }
    catch (Exception ex)
    {
        // 🔥 记录失败指标
        Neo4Metrics.ProofGenerationFailures
            .WithLabels(request.ChainId.ToString(), ex.GetType().Name)
            .Inc();
        throw;
    }
}
```

**负责人**: 核心开发工程师  
**交付**: 所有关键路径埋点完成  
**检查点**: Day 5，指标在Prometheus中可见

---

### Day 4-7: Grafana仪表盘创建

#### Task 1.4: 创建核心监控仪表盘

创建文件：`monitoring/grafana/dashboards/neo4-overview.json`

```json
{
  "dashboard": {
    "title": "Neo N4 - System Overview",
    "panels": [
      {
        "title": "Batch Throughput",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(neo4_batches_sealed_total[5m])",
            "legendFormat": "Chain {{chain_id}}"
          }
        ]
      },
      {
        "title": "Batch Seal Latency (p50, p95, p99)",
        "type": "graph",
        "targets": [
          {
            "expr": "histogram_quantile(0.50, rate(neo4_batch_seal_latency_seconds_bucket[5m]))",
            "legendFormat": "p50"
          },
          {
            "expr": "histogram_quantile(0.95, rate(neo4_batch_seal_latency_seconds_bucket[5m]))",
            "legendFormat": "p95"
          },
          {
            "expr": "histogram_quantile(0.99, rate(neo4_batch_seal_latency_seconds_bucket[5m]))",
            "legendFormat": "p99"
          }
        ]
      },
      {
        "title": "Proof Generation Duration",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(neo4_proof_generation_seconds_sum[5m]) / rate(neo4_proof_generation_seconds_count[5m])",
            "legendFormat": "Avg Duration"
          }
        ]
      },
      {
        "title": "Proof Queue Depth",
        "type": "graph",
        "targets": [
          {
            "expr": "neo4_proof_queue_depth",
            "legendFormat": "Chain {{chain_id}}"
          }
        ]
      },
      {
        "title": "N3 Settlement Success Rate",
        "type": "stat",
        "targets": [
          {
            "expr": "rate(neo4_n3_settlements_submitted_total[5m]) / (rate(neo4_n3_settlements_submitted_total[5m]) + rate(neo4_n3_settlement_failures_total[5m]))",
            "legendFormat": "Success Rate"
          }
        ]
      },
      {
        "title": "Committee Node Health",
        "type": "stat",
        "targets": [
          {
            "expr": "neo4_committee_node_health",
            "legendFormat": "Node {{node_id}}"
          }
        ]
      }
    ]
  }
}
```

**负责人**: DevOps工程师  
**交付**: 可视化仪表盘，实时更新  
**检查点**: Day 7，团队review仪表盘

---

### Day 8-10: 告警规则配置

#### Task 1.5: 配置Alertmanager

创建文件：`monitoring/alertmanager/alertmanager.yml`

```yaml
global:
  slack_api_url: 'YOUR_SLACK_WEBHOOK_URL'

route:
  group_by: ['alertname', 'chain_id']
  group_wait: 10s
  group_interval: 10s
  repeat_interval: 1h
  receiver: 'neo4-alerts'

receivers:
  - name: 'neo4-alerts'
    slack_configs:
      - channel: '#neo4-alerts'
        title: '🚨 Neo N4 Alert'
        text: '{{ range .Alerts }}{{ .Annotations.summary }}\n{{ end }}'
```

创建文件：`monitoring/prometheus/alerts.yml`

```yaml
groups:
  - name: neo4_critical
    rules:
      - alert: BatchSealDelayHigh
        expr: histogram_quantile(0.95, rate(neo4_batch_seal_latency_seconds_bucket[5m])) > 600
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "Batch sealing delayed > 10 minutes (p95)"
          description: "Chain {{ $labels.chain_id }} batch seal p95 latency is {{ $value }}s"
          
      - alert: ProofGenerationFailed
        expr: rate(neo4_proof_generation_failures_total[5m]) > 0.05
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "Proof generation failure rate > 5%"
          description: "Chain {{ $labels.chain_id }} proof failures: {{ $value }} per second"
          
      - alert: N3SettlementFailing
        expr: rate(neo4_n3_settlement_failures_total[5m]) > 0.01
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "N3 settlement failures detected"
          description: "Chain {{ $labels.chain_id }} failing to settle on N3"
          
      - alert: CommitteeNodeDown
        expr: neo4_committee_node_health == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "Committee node is down"
          description: "Node {{ $labels.node_id }} at {{ $labels.node_address }} is unhealthy"
          
      - alert: ProofQueueBacklog
        expr: neo4_proof_queue_depth > 10
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Proof queue backlog building up"
          description: "Chain {{ $labels.chain_id }} has {{ $value }} batches waiting for proof"
```

**负责人**: DevOps工程师  
**交付**: 告警规则生效，Slack通知正常  
**检查点**: Day 10，触发测试告警验证

---

### Day 11-14: 简化Gas模型实现

#### Task 1.6: 实现SimpleGasPriceOracle

创建文件：`src/Neo.L2.Economics/SimpleGasPriceOracle.cs`

```csharp
using Neo.L2.Abstractions;

namespace Neo.L2.Economics;

/// <summary>
/// 简化的Gas价格预言机 - 基于N3委员会设定的固定基础费
/// </summary>
public sealed class SimpleGasPriceOracle : IGasPriceOracle
{
    private decimal _baseFeePerGas;
    private readonly decimal _l1DataFeeMultiplier;
    private readonly IN3DataCostProvider _n3DataCost;
    private readonly object _lock = new();
    
    public SimpleGasPriceOracle(
        IN3DataCostProvider n3DataCost,
        decimal initialBaseFee = 0.00000001m,
        decimal l1DataFeeMultiplier = 1.5m)
    {
        _n3DataCost = n3DataCost;
        _baseFeePerGas = initialBaseFee;
        _l1DataFeeMultiplier = l1DataFeeMultiplier;
    }
    
    public GasPrice GetCurrentGasPrice()
    {
        lock (_lock)
        {
            // 简化公式：固定基础费 + N3 DA成本
            var l1DataCost = EstimateN3DataCost();
            var total = _baseFeePerGas + (l1DataCost * _l1DataFeeMultiplier);
            
            return new GasPrice
            {
                BaseFee = _baseFeePerGas,
                L1DataFee = l1DataCost * _l1DataFeeMultiplier,
                PriorityFee = 0m, // 暂不支持优先级费用
                Total = total
            };
        }
    }
    
    /// <summary>
    /// 委员会治理接口：更新基础费（需要N3委员会多签）
    /// </summary>
    public void UpdateBaseFee(decimal newBaseFee)
    {
        if (newBaseFee <= 0)
            throw new ArgumentException("Base fee must be positive", nameof(newBaseFee));
        
        lock (_lock)
        {
            var oldFee = _baseFeePerGas;
            _baseFeePerGas = newBaseFee;
            
            Log.Information(
                "Gas base fee updated: {OldFee} → {NewFee} GAS",
                oldFee, newBaseFee);
        }
    }
    
    private decimal EstimateN3DataCost()
    {
        // 从N3获取当前calldata成本
        var n3GasPerByte = _n3DataCost.GetCostPerByte();
        
        // 假设平均交易200字节
        var avgTxSize = 200m;
        
        // 假设每batch 100笔交易
        var avgBatchSize = 100m;
        
        // 成本 = (N3 gas/byte) * (avg tx size) / (avg batch size)
        return (n3GasPerByte * avgTxSize) / avgBatchSize;
    }
}

public interface IGasPriceOracle
{
    GasPrice GetCurrentGasPrice();
    void UpdateBaseFee(decimal newBaseFee);
}

public record GasPrice
{
    public decimal BaseFee { get; init; }
    public decimal L1DataFee { get; init; }
    public decimal PriorityFee { get; init; }
    public decimal Total { get; init; }
}
```

**负责人**: 核心开发工程师  
**交付**: Gas定价模块可用  
**检查点**: Day 14，单元测试通过

---

## 📋 Week 1-2 检查清单

### ✅ 完成标准

- [ ] Prometheus运行正常，收集所有指标
- [ ] Grafana仪表盘显示实时数据
- [ ] 至少5个告警规则配置并测试
- [ ] SimpleGasPriceOracle通过单元测试
- [ ] 所有代码通过CI/CD
- [ ] 文档更新（README添加监控章节）

### 📊 Week 2 里程碑评审

**时间**: 2026年09月30日（周五）下午3:00  
**参与**: 全体团队  
**议程**:
1. 监控系统演示（15分钟）
2. 指标数据review（10分钟）
3. 告警测试结果（10分钟）
4. Gas模型讨论（15分钟）
5. Week 3-4计划确认（10分钟）

---

## 🚀 后续计划预览

### Week 3-6: Gateway聚合层
- Week 3: L2 Proof收集器
- Week 4: 递归证明聚合
- Week 5: N3结算集成
- Week 6: 集成测试

### Week 7-9: 跨L2消息协议
- Week 7: 消息ID和proof生成
- Week 8: 消费验证逻辑
- Week 9: 防重放和超时

### Week 10-14: 剩余P0功能
- Week 10-11: Forced Inclusion
- Week 12-13: 灾难恢复工具
- Week 14: P0功能集成测试

---

## 📞 沟通机制

### 日常
- **Daily Standup**: 每天上午10:00，15分钟
- **Slack频道**: #neo4-dev
- **代码Review**: 所有PR需至少1人review

### 每周
- **周五Review**: 每周五下午3:00，1小时
- **周报**: 每周五下班前提交进度

### 紧急
- **On-Call**: 24/7轮值（一旦上生产）
- **事故响应**: P0告警5分钟内响应

---

## 🛠️ 开发环境要求

### 必需工具
```bash
# .NET SDK
dotnet --version  # >= 8.0

# Docker
docker --version  # >= 24.0

# Git
git --version  # >= 2.40

# Node.js（SDK开发用）
node --version  # >= 20.0

# Rust（zkVM开发用）
rustc --version  # >= 1.75
cargo prove --version  # SP1
```

### 克隆仓库
```bash
git clone https://github.com/r3e-network/neo-n4.git
cd neo-n4
git checkout -b feature/monitoring-week1
```

### 本地开发
```bash
# 恢复依赖
dotnet restore

# 构建
dotnet build

# 运行测试
dotnet test

# 启动监控栈
cd monitoring && docker-compose up -d

# 本地运行Sequencer
cd src/Neo.Plugins.L2Batch
dotnet run
```

---

## 📦 交付物清单

### Week 1-2 交付物
1. **监控栈**
   - [ ] `monitoring/docker-compose.yml`
   - [ ] `monitoring/prometheus/prometheus.yml`
   - [ ] `monitoring/prometheus/alerts.yml`
   - [ ] `monitoring/grafana/dashboards/neo4-overview.json`
   - [ ] `monitoring/alertmanager/alertmanager.yml`

2. **代码**
   - [ ] `src/Neo.L2.Telemetry/PrometheusMetrics.cs`
   - [ ] `src/Neo.L2.Economics/SimpleGasPriceOracle.cs`
   - [ ] 修改: `src/Neo.Plugins.L2Batch/L2BatchPlugin.cs`
   - [ ] 修改: `src/Neo.L2.Proving/RiscVZk/Sp1BatchProofProver.cs`

3. **测试**
   - [ ] `tests/Neo.L2.Economics.Tests/SimpleGasPriceOracleTests.cs`
   - [ ] 集成测试脚本

4. **文档**
   - [ ] `docs/monitoring/README.md`
   - [ ] `docs/monitoring/ALERTS.md`
   - [ ] `docs/economics/GAS_PRICING.md`

---

## 🎯 成功指标

### Week 2 KPIs
- ✅ 监控栈正常运行时间 > 99%
- ✅ 指标收集延迟 < 30秒
- ✅ 告警误报率 < 5%
- ✅ Gas价格计算性能 < 1ms
- ✅ 代码覆盖率 > 80%

---

## ⚠️ 风险与缓解

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| Prometheus数据存储不足 | 中 | 中 | 配置数据保留策略（30天） |
| 告警风暴 | 中 | 高 | 配置告警聚合和抑制规则 |
| Gas模型过于简单 | 低 | 中 | 预留扩展接口，后续优化 |
| 团队对Prometheus不熟悉 | 高 | 低 | 提供培训材料和文档 |

---

## 📚 参考资料

- [Prometheus官方文档](https://prometheus.io/docs/)
- [Grafana文档](https://grafana.com/docs/)
- [Neo N3 Gas模型](https://docs.neo.org/docs/n3/foundation/gas)
- [SP1 Proving System](https://docs.succinct.xyz/)

---

**执行开始**: 2026年09月16日  
**Week 2评审**: 2026年09月30日  
**负责人**: [指定项目经理]  
**状态**: 🚀 READY TO START