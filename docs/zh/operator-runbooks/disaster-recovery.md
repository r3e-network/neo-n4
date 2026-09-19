# Neo N4 灾难恢复运行手册

**版本：** 1.0
**最后更新：** 2026-09-14
**负责人：** 运维团队
**评审节奏：** 月度运营评审，季度完整演练

---

## 文档控制

| 版本 | 日期 | 作者 | 变更 |
|------|------|------|------|
| 1.0 | 2026-09-14 | [姓名] | 生产发布初始草稿 |
| ... | YYYY-MM-DD | [姓名] | 基于经验教训更新 |

---

## 应急联系人

| 角色 | 姓名 | 联系方式 | 可用性 |
|------|------|---------|--------|
| 值班工程师 | [姓名] | PagerDuty 轮值 | 24/7 |
| 安全理事会负责人 | [姓名] | 加密邮件 + Signal | 按需 |
| DevOps 升级 | [姓名] | Slack DM @ops-escalation | 工作时间 |
| 外部供应商支持 | R3E Network | 工单门户 (support@reborn.com) | 视 SLA 而定 |

**升级阈值：**
- **严重（立即呼叫）：** 资金风险、链活性受影响、安全漏洞
- **高（1 小时内）：** 性能下降 >50%、结算受阻
- **中（工作时间）：** 非财务问题、体验降级

---

## 快速参考决策树

### 链是否在出块？
```
├─ 是 → 继续监控
│   └─ 检查指标：l2.batch.sealed 计数是否稳定？
│       ├─ 是 → 正常运行
│       └─ 否 → 排查排序器健康状况
│
└─ 否 → 见下方场景 1 或 2
```

### 用户能否提现？
```
├─ 是 → 监控 withdrawalRoot 进展
│   └─ 若延迟 >30 分钟 → 呼叫值班
│
└─ 否 → 检查桥是否暂停（场景 3）
```

### 向 L1 结算是否正常？
```
├─ 是 → 系统健康
│
└─ 否 → 见场景 1：L1 结算失败
```

---

## 场景 1：L1 结算失败

**描述：** 由于网络拥塞、合约暂停或资金不足，批次无法提交到 L1 上的 NeoHub。

### 症状

- `l2.settlement.submit_failures` 计数递增（5 分钟内速率 >0）
- `l2.settlement.pending` 计量增长（>100 批次积压）
- 排序器持续出块但无 L1 确认
- 用户报告批次终局延迟（超出预期 SLA）

### 立即行动（0-15 分钟）

#### 第 1 步：评估影响

```bash
# 检查结算指标
curl http://localhost:9090/metrics | grep -E "l2\.settlement\.(submit_failures|submitted|pending)"

# 检查近期日志中的错误
tail -f /var/log/neo-l2/settlement.log | grep -i "fail\|error\|exception"

# 验证批次队列深度
kubectl exec <batcher-pod> -- ./BatchChecker --queue-depth
```

**预期输出：**
```
# 健康基线
l2_settlement_submit_failures_total 0
l2_settlement_pending_batches 0

# 问题指标
l2_settlement_submit_failures_total 15  # 递增中！
l2_settlement_pending_batches 127       # 增长中！
```

#### 第 2 步：确定根因

##### 情况 A：L1 网络拥塞（最常见）

**诊断：**
```bash
# 检查 L1 gas 价格
curl http://localhost:10332/getnep17balance NEO
# 或使用 Neo CLI 工具
neoutil getgasprice  # 应显示 gas 价格上涨

# 检查 L1 mempool 大小
curl http://localhost:10332/getrawmempool | jq length
# >1000 笔交易表示拥塞
```

**缓解：**
```json
// 更新 chain.config.json
{
  "PluginConfiguration": {
    "L1GasPrice": 1000000000  // 从默认 100000000 上调（10 倍）
  }
}
```

**应用：**
```bash
# 重载配置（按插件文档需重启）
systemctl restart neo-l2-batcher

# 监控恢复
watch -n 30 'curl http://localhost:9090/metrics | grep l2_settlement_submitted'
```

**预期恢复时间：** 取决于 L1 拥塞程度，10-30 分钟

##### 情况 B：合约被治理理事会暂停

**诊断：**
```bash
# 通过 RPC 检查暂停状态
cast call --contract $SETTLEMENT_MANAGER_HASH "paused()"

# 若返回 true，说明理事会已暂停
echo "合约已被治理理事会暂停"
```

**缓解选项：**

**选项 1：等待理事会决议**
```bash
# 监控理事会讨论渠道
watch -n 600 'echo "每 10 分钟检查一次理事会更新..."'

# 预期：严重问题理事会 1-4 小时内响应
```

**选项 2：紧急解除暂停提案**（需多重签名授权）

**⚠️ 要求：** 7 名理事会成员中至少 5 人签名

```bash
# 准备紧急解除暂停交易
./Neo.Toolbox prepare-unpause \
  --address $SETTLEMENT_MANAGER_HASH \
  --signers /path/to/signed-transactions/*.sig

# 提交到 L1
./Neo.Toolbox submit-tx \
  --network PrivateNet \
  --transaction-file unpause.tx
```

**需留档材料：**
- 理事会提案讨论链接
- 紧急情况说明备忘录
- 多重签名批准截图

**预期恢复时间：** 取决于理事会响应（通常 1-4 小时）

##### 情况 C：L1 GAS 不足以支付手续费

**诊断：**
```bash
# 检查排序器钱包余额
neo-getbalance --address $SEQUENCER_WALLET_ADDRESS

# 对比每日手续费需求
# 正常负载约 0.1 NEO/天
# 高负载 0.5 NEO/天

if [ $(neo-getbalance $WALLET) -lt 0.1 ]; then
    echo "余额不足 - 需要转入"
fi
```

**缓解：**

**第 1 步：向排序器钱包转入 GAS**
```bash
# 从配置获取钱包地址
WALLET_ADDRESS=$(jq -r '.PluginConfiguration.L1SignerWallet' chain.config.json)

# 从金库/理事会钱包转入
neo-send-gas \
  --from $TREASURY_WALLET \
  --to $WALLET_ADDRESS \
  --amount 10

# 等待确认（私有网络约 15 秒）
sleep 15

# 验证收款
neo-getbalance --address $WALLET_ADDRESS
```

**第 2 步：重启结算客户端**
```bash
systemctl restart neo-l2-settlement
```

**预期恢复时间：** 转入确认后 5-10 分钟

### 升级触发

若结算在以下时间后仍未恢复：
- **30 分钟：** 呼叫安全理事会负责人（情况 A/B）
- **1 小时：** 启动应急协议，通知运维人员
- **2 小时：** 考虑启用只读模式（见第 2 节）

### 事后行动

1. **记录根因**（24 小时内）
   ```bash
   # 提取相关日志
   grep -A 10 "settlement failure" /var/log/neo-l2/settlement.log > incident-root-cause.txt
   ```

2. **安排事后复盘会议**（48 小时内）
   - 参会：值班工程师、运维组长、安全理事会代表
   - 议程：发生了什么、为什么、如何防止复发

3. **更新运行手册**（1 周内）
   - 添加新发现的模式
   - 视需要细化升级阈值
   - 向更广的运维团队分享经验

4. **落实预防措施**（若识别到系统性问题）
   - 示例：当金库余额 <0.2 NEO 时自动充值
   - 示例：基于 L1 mempool 深度动态定价 gas

---

## 场景 2：数据库损坏

**描述：** RocksDB 状态后端损坏，导致节点无法启动或查询不一致。

### 症状

- 节点启动失败，错误：`RocksDB open failed: corrupt manifest`
- 查询返回错误：`System.IO.IOException: corrupted database`
- 节点之间状态值不一致
- `l2.audit.failures` 计数突然递增

### 立即行动（0-30 分钟）

#### 第 1 步：立即停止节点

```bash
# 防止进一步损坏
systemctl stop neo-l2-node

# 验证已停止
systemctl status neo-l2-node  # 应显示 inactive/dead
```

#### 第 2 步：保留损坏状态（切勿删除！）

```bash
# 创建取证备份
mkdir -p /tmp/neo-recovery-backup-$(date +%Y%m%d-%H%M%S)
cp -r /var/lib/neo-l2/state/* /tmp/neo-recovery-backup-$(date +%Y%m%d-%H%M%S)/

# 设置安全权限
chmod 700 /tmp/neo-recovery-backup-*
chown root:root /tmp/neo-recovery-backup-*/*

# 验证完整性
ls -lah /tmp/neo-recovery-backup-*/
```

#### 第 3 步：尝试自动恢复

**选项 A：检查 VERSION 文件（标准恢复）**

```bash
# 列出 VERSION 文件
ls -la /var/lib/neo-l2/state/VERSION*

# 若存在多个版本，检查哪个最新
cat /var/lib/neo-l2/state/VERSION  # 包含版本字符串
```

**尝试恢复：**
```bash
# 使用 Neo 状态恢复工具
neo-state-recover \
  --db-path /var/lib/neo-l2/state \
  --output /tmp/recovered-state

# 若成功，验证
neo-state-validate --input /tmp/recovered-state

# 验证通过则恢复
rm -rf /var/lib/neo-l2/state
mv /tmp/recovered-state /var/lib/neo-l2/state

# 启动节点
systemctl start neo-l2-node
```

**选项 B：手动快照恢复**（若自动恢复失败）

```bash
# 列出 S3 中的可用快照
aws s3 ls s3://neo-backups/state-snapshots/ --prefix latest/

# 下载最近的良好快照（按需替换桶名）
aws s3 sync s3://neo-backups/state-snapshots/latest/ /var/lib/neo-l2/state/

# 验证下载的文件
ls -la /var/lib/neo-l2/state/ | head -20

# 启动节点
systemctl start neo-l2-node

# 监控同步成功
journalctl -u neo-l2-node -f | grep -i "state synced"
```

#### 第 4 步：应急只读模式（备用）

若损坏严重需从创世重建：

```bash
# 激活只读模式（禁用批次封装）
cat > /etc/neo-l2/readonly-config.json <<EOF
{
  "PluginConfiguration": {
    "ReadOnlyMode": true,
    "Enabled": false
  }
}
EOF

# 以只读标志重启
neo-l2-node --read-only

# 验证服务仍能响应查询
curl http://localhost:10332/getblockcount
```

### 升级触发

若自动恢复在以下时间后失败：
- **30 分钟：** 联系基础设施组长
- **1 小时：** 评估从 L1 创世重建（预计 4-8 小时）
- **2 小时：** 考虑临时迁移到备用节点

### 预防措施

**每日自动快照**
```bash
#!/bin/bash
# /usr/local/bin/neo-daily-snapshot.sh

DATE=$(date +%Y%m%d)
SOURCE=/var/lib/neo-l2/state
BACKUP=s3://neo-backups/state-snapshots/daily/$DATE

# 同步状态到 S3
aws s3 sync $SOURCE $BACKUP

# 保留最近 30 天
aws s3 delete-objects --delete-objects file:///retention-list.json

echo "快照完成：$BACKUP"
```

**每周恢复演练**
```bash
# 每月第一个周日
# 通过 cron 调度：0 2 1-7 * * /usr/local/bin/neo-restore-drill.sh

/usr/local/bin/neo-restore-drill.sh \
  --snapshot s3://neo-backups/state-snapshots/latest/ \
  --test-db /tmp/test-restore-$RANDOM \
  --verify-checks all
```

---

*[其余 3 个场景沿用相同结构 - 为简洁略去]*

## 场景 3：应急暂停激活
...

## 场景 4：排序器委员会失陷
...

## 场景 5：多区域中断故障切换
...

---

## 附录 A：命令模板

### 常用诊断命令

```bash
# 健康检查
curl http://localhost:9090/healthz        # 进程存活
curl http://localhost:9090/readyz         # 就绪（200/503）
curl http://localhost:9090/operatorstatus # 完整运维状态 JSON

# 指标提取
curl http://localhost:9090/metrics | grep "^l2_"  # 全部 L2 指标

# 状态查询
neo-cli getblockcount                # 最新区块高度
neo-cli getstate root               # 当前状态根
neo-cli getbatch --number 100       # 特定批次信息
```

### 配置管理

```bash
# 校验配置语法
python3 -m json.tool config.json > /dev/null && echo "Valid JSON" || echo "Invalid JSON"

# 变更前备份当前配置
cp config.json config.json.backup.$(date +%Y%m%d%H%M%S)

# 重载插件配置（若支持免重启）
neo-plugin-reload --plugin L2Batch
```

---

## 附录 B：联系人升级矩阵

| 严重度 | 响应时间 | 通知方式 |
|--------|----------|----------|
| 严重（资金/活性） | <5 分钟 | PagerDuty + SMS + 电话 |
| 高（性能下降） | <1 小时 | PagerDuty + Slack DM |
| 中（非关键问题） | <4 小时 | Slack 频道 #ops-alerts |
| 低（信息类） | 下一个工作日 | 每日摘要邮件 |

**升级路径：**
1. 主要值班工程师
2. 30 分钟后仍未解决 → 二级值班 + DevOps 负责人
3. 1 小时后仍未解决 → 安全理事会 + 高管通知
4. 2 小时后仍未解决 → 准备对外公告

---

**最后评审日期：** 2026-09-14
**下次计划评审：** 2026-10-14
**演练计划：** 月度走查，季度完整故障切换测试