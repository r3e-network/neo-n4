# BatchSerializer 变异实测 — 2026-09-17

这是测试敏感度实测，**不是全系统形式化验证**。Stryker.NET 5.0.0 两次运行均退出 0。
未设置分数门槛，退出 0 本身不表示质量认证。

| BatchSerializer 状态 | 修改前 | 修改后 |
| --- | ---: | ---: |
| Killed | 98 | 120 |
| Survived | 29 | 7 |
| NoCoverage | 4 | 4 |
| CompileError | 2 | 2 |
| Ignored | 6 | 6 |
| Timeout | 0 | 0 |
| 分数（杀死数 / 杀死数+存活数+未覆盖数） | 74.81% | 91.60% |

计数直接来自 JSON 中 BatchSerializer.cs 的状态。Stryker 会对项目其他文件生成变异
后再过滤，不能把其他文件的数量算作此文件覆盖。编译失败及忽略项不计入杀死数；
两次运行之间没有通过新增排除规则提高分数。

## 复现

将 dotnet-stryker 5.0.0 安装到本地工具目录。在 tests/Neo.L2.Batch.UnitTests 下运行：

```sh
dotnet-stryker --project Neo.L2.Batch.csproj \
  --test-project Neo.L2.Batch.UnitTests.csproj \
  --mutate '**/BatchSerializer.cs' --concurrency 2 \
  --reporter Json --reporter ClearText --skip-version-check \
  --break-on-initial-test-failure --output /absolute/local/output
```

完整源码快照、测试信息和变异结果保存于 evidence/mutation-before.json 和
 evidence/mutation-after.json。SHA-256：

- 修改前：`d30204f82984de717bd8f96425678fbde5f45ba2743083c473b05e14caec0989`
- 修改后：`419ca04bc3aba60e77253c59b0dcee2da4844151c390a8e8defd82fba4f4f92d`

## 四项新增回归

UT_BatchSerializer 新增：总缓冲区长度匹配的超限证明（避免长度不匹配检查掩盖
上限守卫被删除）、PublicInputs 反向块区间、两个编码入口的全部根字段空值。
Batch 项目由 132 项通过增加为 136 项通过。此覆盖修复未修改生产逻辑。

## 剩余结果

七项存活没有被排除或冒记为杀死：74、112、121、181 清空异常文本，141 修改异常
文本中预期长度的计算；诊断文本没有完全断言。80 移除 checked 加法，但前置 1 MiB
上限已将和限制在 int32 范围；191 修改读取 ForcedInclusionCount 后不再使用的游标。
后两项按当前源码判断为等价变异，不代表未来版本仍等价。

四项 NoCoverage 修改编码内部长度不一致的异常路径（行 147、259），现有用例不触发
这些内部不变量失败路径，不宣称覆盖。另有两项编译失败和六项工具过滤忽略。
没有为了达到 100% 而改变生产行为或强行约束无关诊断文本。
