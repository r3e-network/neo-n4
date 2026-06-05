# BN254 Groth16 验证器合约设计

状态：评审草案。目标是提供一个可部署的 NeoHub 验证器合约，基于 Neo Core
`CryptoLib` 的 `bn254*` 原生方法，对 BN254（alt_bn128）上的 Groth16 证明执行真实
pairing 校验，使 `ContractZkVerifier` 能把 `ProofType.Zk` settlement 路由到真实密码学
验证器，而不是依赖 envelope-only 兜底。

## 1. 背景

`contracts/NeoHub.ContractZkVerifier` 是 ZK 证明路由器。它解析 N4 batch commitment，
读取 RISC-V ZK proof payload，检查 proof system 与治理注册的 verification-key id，然后：

- 调用治理注册的下游 verifier：
  `verifyZkProof(proofSystem, verificationKeyId, publicInputHash, proofBytes)`；或
- 在显式允许的 devnet / 临时模式下接受 envelope-only。

当前缺口是：没有下游合约真正执行 Groth16/BN254 证明数学。该设计用于关闭这个缺口，
服务于 SP1 / RISC-Zero 等输出 Groth16 BN254 wrapper proof 的路径。

## 2. Groth16 / BN254 校验模型

Groth16 verifying key 为 `(α ∈ G1, β ∈ G2, γ ∈ G2, δ ∈ G2, IC ∈ G1^{ℓ+1})`，
proof 为 `(A ∈ G1, B ∈ G2, C ∈ G1)`。对 public inputs `a₁..a_ℓ`，验证器计算：

```text
vk_x = IC₀ + Σ aᵢ · ICᵢ
```

并接受当且仅当：

```text
e(A, B) = e(α, β) · e(vk_x, γ) · e(C, δ)
```

这是固定工作量：四次 pairing、一次 G1 scalar multiplication、一次 G1 add、两次 GT
composition 和一次 GT equality，没有攻击者可控循环。

## 3. CryptoLib 映射

合约依赖以下 Neo Core 原生方法：

| 需求 | CryptoLib 方法 |
| --- | --- |
| 反序列化 G1/G2 | `Bn254Deserialize(byte[])` |
| G1 标量乘 | `Bn254Mul(point, scalar)` |
| G1 / GT 组合 | `Bn254Add(left, right)` |
| pairing | `Bn254Pairing(g1, g2)` |
| GT 等值判断 | `Bn254Equal(left, right)` |

点解码必须由 native 层校验 canonical 坐标、曲线点合法性，以及 G2 subgroup membership。
GT 对比必须使用 `Bn254Equal`，不能把 interop value cast 成 byte array 再做普通比较。

## 4. N4 public input 约定

当前路由 ABI 只传入一个 32-byte `publicInputHash` 和一个 `verificationKeyId`，因此 N4
约定如下：

- verifying key 身份由治理注册的 `verificationKeyId` 选择，不作为 circuit public input；
- circuit 暴露一个 public input：batch commitment 中的 public-values digest；
- `publicInputHash` 解释为 big-endian field scalar，并由 native scalar 逻辑按 r 取模。

因此该合约固定支持 `ℓ = 1`，VK 只包含 `IC₀` 与 `IC₁`。多 public input circuit 需要
新的 router ABI，不能在该 ABI 下静默近似。

## 5. 合约结构

建议合约：`contracts/NeoHub.Groth16Verifier/Groth16VerifierContract.cs`。

核心方法：

- `_deploy(object data, bool update)`：设置 owner；
- `GetOwner()` / `SetOwner(UInt160)`：治理所有权；
- `RegisterVerifyingKey(byte[] verificationKeyId, byte[] vk)`：owner-only，校验并存储 VK；
- `RemoveVerifyingKey(byte[] verificationKeyId)`：owner-only，删除 VK；
- `IsVerifyingKeyRegistered(byte[] verificationKeyId)`：只读查询；
- `verifyZkProof(byte proofSystem, byte[] verificationKeyId, byte[] publicInputHash, byte[] proofBytes)`：
  只读验证入口，供 `ContractZkVerifier` 通过 `CallFlags.ReadOnly` 调用。

## 6. 编码

- G1：64 bytes，`x || y`；
- G2：128 bytes，`x_im || x_re || y_im || y_re`；
- VK：`α(64) || β(128) || γ(128) || δ(128) || IC₀(64) || IC₁(64)`，共 576 bytes；
- proof：`A(64) || B(128) || C(64)`，共 256 bytes；
- public input：32-byte `publicInputHash`。

## 7. 安全要点

- **VK 必须可信。** 任意 VK 注册会破坏 soundness，因此注册必须 owner/governance-gated。
- **点必须由 native 解码校验。** 非 canonical、off-curve、off-subgroup 输入必须 fault。
- **成本固定。** 输入长度固定，IC 长度固定，不存在按 attacker-controlled length 线性增长的路径。
- **只读验证。** `verifyZkProof` 不写 storage，适合路由器的 `CallFlags.ReadOnly`。
- **Envelope-only 必须只用于 devnet。** 生产链应注册真实 verifier，并永久锁定 envelope-only。

## 8. 测试计划

测试应通过 `Neo.SmartContract.Testing` 或等价 in-process Neo engine 执行：

1. 合法 proof + 合法 public input 返回 `true`；
2. 篡改 proof 任一字节返回 `false` 或在点非法时 fault；
3. 错误 public input 返回 `false`；
4. 未注册 VK fault；
5. VK / proof 长度错误 fault；
6. off-curve / non-canonical 点 fault；
7. 非 owner 注册 VK fault，owner 注册成功并可查询。

## 9. 构建集成决策

合约编译依赖 devpack framework 的 `CryptoLib.BN254.cs` 绑定；运行测试必须加载
bn254-enabled 的 Neo Core。推荐在实现前明确采用以下任一方式：

1. 将 `external/neo` 打包到本地 NuGet feed，并把测试项目 pin 到该版本；或
2. 通过项目引用让测试直接使用 `external/neo/src` 的本地 core。

该决策不改变合约 ABI，但决定测试能否真正执行 pairing 数学。
