# SP1 Groth16/BN254 链上验证器

状态：已实现并固定版本。生产合约位于
`contracts/NeoHub.Sp1Groth16Verifier`。

## 1. 安全边界

`NeoHub.ContractZkVerifier` 校验 N4 batch commitment、`RiscVProofPayload` 信封、证明系统、
已注册的 program VK 和 32 字节 N4 public-input hash；对于 `ProofSystem.Sp1`，再调用
`Sp1Groth16Verifier.verifyZkProof(...)` 执行完整的 BN254/Groth16 数学。

生产部署必须注册该终端验证器，并在启用 `ProofType.Zk` 路由前不可逆地执行
`DisableEnvelopeOnlyPermanently(ProofSystem.Sp1=1)`，随后调用
`LockProofSystemConfiguration(ProofSystem.Sp1=1, programVKey)` 固定唯一 program VK 与
terminal verifier。生产计划不包含 devnet 的 envelope-only 逃生路径。

该合约只服务于固定的 SP1 v6.1-compatible wrapper。Risc0、Halo2、Axiom、其他 SP1
wrapper 或新的验证密钥都必须部署独立、重新审计的终端验证器。

## 2. 精确 SP1 格式

SP1 wrapper 的五个 BN254 公共输入依次为：

1. 原始 32 字节 `vk.bytes32()` program VK；
2. committed public values 的 masked SHA-256；
3. guest exit code；
4. SP1 recursion VK root；
5. Groth16 nonce。

N4 guest 固定提交 `0x00 || publicInputHash[32]`。合约重建这 33 字节，计算 SHA-256，
并按 SP1 规则清除 digest 第一个字节的最高三位。五个输入都必须是规范的 Fr 元素；
不得对攻击者输入做静默模约减。

证明必须正好 356 字节：

| 偏移 | 长度 | 内容 |
|---:|---:|---|
| 0 | 4 | selector `0x4388a21c` |
| 4 | 32 | 必须为零的 exit code |
| 36 | 32 | recursion VK root |
| 68 | 32 | nonce |
| 100 | 64 | G1 点 `A` |
| 164 | 128 | G2 点 `B` |
| 292 | 64 | G1 点 `C` |

固定 recursion VK root 为
`0x002f850ee998974d6cc00e50cd0814b098c05bfade466d28573240d057f25352`。

## 3. 配对方程

合约固定 SP1 wrapper 的 `alpha`、`beta`、`gamma`、`delta` 和 `IC[0..5]`，计算：

```text
linearCombination = IC0 + a1*IC1 + a2*IC2 + a3*IC3 + a4*IC4 + a5*IC5
e(A, B) = e(alpha, beta) * e(linearCombination, gamma) * e(C, delta)
```

Neo `CryptoLib.Bn254Deserialize` 强制 EIP-196/EIP-197 大端规范坐标、曲线成员关系和
G2 子群检查。GT 结果通过 `Bn254Add` 组合并用 `Bn254Equal` 比较。

## 4. 不可变性与最小权限

终端验证器没有 owner、storage、升级入口或运行时 VK 注册。SP1 wrapper 变更时必须部署新
字节码并通过治理显式切换，不能原地替换密钥。

manifest 只允许调用 CryptoLib
`0x726cb6e0cd8628a1350a611384688911ab75f51b` 的 `sha256`、
`bn254Deserialize`、`bn254Add`、`bn254Mul`、`bn254Pairing`、`bn254Equal`，不含通配权限。

## 5. 成本与测试

每次验证固定执行五次 G1 标量乘、五次 G1 加法、四次 pairing、两次 GT 乘法和一次相等
检查；长度校验发生在密码学运算前，不存在攻击者控制的循环。N4 Core 测试限制总费用不超过
100 GAS。

`tests/NeoHub.Sp1Groth16Verifier.UnitTests` 直接使用当前 vendored N4
`ApplicationEngine` 执行编译后的 NEF，而不是 ABI 过旧的公开 Testing 包。测试固定 NEF
SHA-256 与最小权限 manifest，并覆盖 selector/root、错误长度、非规范标量、畸形点、
Rust 生成的正向证明和完整 pairing 失败路径。

仓库中的发布级正向向量来自 Rust SP1 prover，并逐字节固定：356 字节 `proof.bytes()`、
33 字节 public values、原始 32 字节 `vk.bytes32()` 和 N4 `publicInputHash`。它会通过
terminal 与 router 两层验证；任何 program VK、public-input hash、selector、recursion
root、nonce 或证明点变化都失败。公共网络发布证据还必须对精确部署的 NEF/VK 重跑同一组向量。

## 6. 运维顺序

1. 部署 `ContractZkVerifier` 与 `Sp1Groth16Verifier`；
2. 注册准确的 SP1 program VK 原始字节；
3. 注册 SP1 终端验证器；
4. 永久关闭 SP1 envelope-only；
5. 调用 `LockProofSystemConfiguration` 固定准确的 program VK 与 terminal verifier；
6. 最后将 `ProofType.Zk` 路由到 `ContractZkVerifier`。

该锁保存准确的 VK，而不是布尔标记。锁定后，bootstrap 阶段曾登记的其他 VK 不再被接受，
任何 owner 都不能增删 VK、替换 terminal 或修改 envelope-only 状态。升级 proof system 时必须
部署新的 immutable terminal 与 versioned router，再由外层 `VerifierRegistry` 治理切换。

Rust 与 Neo 工具之间传递 `vk.bytes32()` 时必须比较原始字节，不能依赖可能反转显示顺序的
`UInt256` 文本。
