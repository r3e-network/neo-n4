# Neo N4 快速启动检查清单

**日期**: 2026-09-16  
**目标**: Week 1-2 完成监控基础设施  

---

## ✅ Day 1（今天）

### 上午
- [ ] 项目启动会议（1小时）
  - [ ] 确认团队成员和职责
  - [ ] 确认开发环境
  - [ ] 确认沟通渠道

- [ ] 创建监控目录结构
```bash
mkdir -p monitoring/{prometheus,grafana,alertmanager}
```

- [ ] 复制prometheus.yml配置（见执行计划）

### 下午
- [ ] 启动Docker Compose
```bash
cd monitoring
docker-compose up -d
```

- [ ] 验证Prometheus运行
  - 访问: http://localhost:9090
  - 检查: Targets页面

- [ ] 验证Grafana运行
  - 访问: http://localhost:3000
  - 登录: admin/admin
  - 修改密码

---

## ✅ Day 2-3

### PrometheusMetrics.cs实现
- [ ] 创建文件: src/Neo.L2.Telemetry/PrometheusMetrics.cs
- [ ] 定义所有指标（见执行计划）
- [ ] 添加Prometheus.NET NuGet包
```bash
dotnet add src/Neo.L2.Telemetry package prometheus-net.AspNetCore
```

### 代码集成
- [ ] 修改L2BatchPlugin.cs添加埋点
- [ ] 修改Sp1BatchProofProver.cs添加埋点
- [ ] 本地测试指标收集

---

## ✅ Day 4-5

### 验证指标
- [ ] 运行本地Sequencer
- [ ] 发送测试交易
- [ ] 在Prometheus查询指标
```promql
neo4_batches_sealed_total
neo4_batch_tx_count
```

### Code Review
- [ ] 创建PR
- [ ] 至少1人review
- [ ] 合并到main

---

## ✅ Day 6-7

### Grafana仪表盘
- [ ] 导入neo4-overview.json
- [ ] 配置Prometheus数据源
- [ ] 验证所有面板显示数据

### 文档
- [ ] 更新README.md监控章节
- [ ] 编写docs/monitoring/README.md

---

## ✅ Day 8-10

### 告警配置
- [ ] 配置alertmanager.yml
- [ ] 添加alerts.yml规则
- [ ] 配置Slack Webhook
- [ ] 触发测试告警验证

---

## ✅ Day 11-14

### Gas模型
- [ ] 实现SimpleGasPriceOracle.cs
- [ ] 编写单元测试
- [ ] 集成到Sequencer
- [ ] 文档: docs/economics/GAS_PRICING.md

### Week 2 评审准备
- [ ] 准备演示
- [ ] 准备数据
- [ ] 准备下一步计划

---

## 🚨 每日检查

**每天下班前**:
- [ ] 提交代码
- [ ] 更新Slack进度
- [ ] 检查CI/CD状态
- [ ] 准备明天工作

**遇到阻塞立即升级**

---

## 📞 联系方式

- Slack: #neo4-dev
- 紧急: [项目经理手机]
- 技术问题: @tech-lead

---

**打印此清单，每天勾选完成项**
