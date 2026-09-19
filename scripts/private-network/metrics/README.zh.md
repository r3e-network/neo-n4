# Neo N4 监控与告警

节点指标由 `Neo.L2.Telemetry` 和 `Neo.Plugins.L2Metrics` 提供：
`IL2Metrics`、`InMemoryMetrics`、`PrometheusExporter`、`MetricsHttpServer`。
HTTP 表面包括 `/metrics`、`/healthz`、`/readyz`、`/healthprobe`、`/operatorstatus`。
本目录包含 Prometheus 采集配置、Grafana 仪表板和 Alertmanager 路由配置。
启动这些基础设施不等于节点已启动、指标已被采集或通知已经送达。

## 指标格式

指标目录见 `docs/telemetry.md`。点号转换为下划线，计数器附加 `_total`。
进程内直方图导出 `_count`、`_sum`、`_max` 摘要，没有 `_bucket`；
不要在这些序列上使用 `histogram_quantile`。

## 服务与启动

| 服务 | 镜像 | 端口 |
|---|---|---|
| Prometheus | `prom/prometheus:v2.55.0` | 9090 |
| Grafana | `grafana/grafana:11.3.0` | 3000 |
| Alertmanager | `prom/alertmanager:v0.27.0` | 9093 |

```bash
docker compose -f scripts/private-network/docker-compose.yml up -d
curl -s localhost:9090/-/healthy
curl -s localhost:3000/api/health
curl -s localhost:9093/-/healthy
```

Grafana 仪表板名为 `Neo N4 Overview`，由文件预配。修改 JSON 后按配置的
`updateIntervalSeconds` 重新加载；界面手工修改不是持久配置来源。
示例管理员凭据不得用于暴露到公共网络的部署。

## 通知与验证边界

`prometheus-alerts.yml` 定义告警。Prometheus 评估规则并转发告警给 Alertmanager；
`alertmanager.yml` 默认接收器为空操作，不会发送外部通知。
接入真实通知渠道时用受控配置或该接收器支持的秘密文件机制提供凭据，不要提交凭据。
不要假定 Alertmanager 会自动展开 YAML 内的环境变量。

Prometheus 自采集只提供 Prometheus 自身指标，不产生节点的 `l2_*` 指标。
在 Targets 页面确认节点采集成功，然后查询所需序列：

```bash
curl -s 'http://localhost:9090/api/v1/targets'
curl -s 'http://localhost:9090/api/v1/query?query=l2_settlement_poisoned'
curl -s 'http://localhost:9090/api/v1/rules?type=alert'
```

`/api/v1/rules` 读取当前规则状态，不强制评估或触发告警。
告警验证应使用隔离测试环境中的合成序列及 `promtool test rules` 测试夹具；
不要为了测试通知而破坏真实结算状态。通知链路需要另行验证接收器实际收到消息。
当前未提供实时部署、仪表板渲染或通知交付验收证据。

## 扩展

1. 在 `MetricNames` 和 `MetricCatalog.Descriptions` 同步登记指标，并在生产路径发出它。
2. 仪表板采用实际导出的名称及摘要语义。
3. 告警必须区分无业务流量与处理停滞；仅增加 `for` 不能避免空闲链误报。
