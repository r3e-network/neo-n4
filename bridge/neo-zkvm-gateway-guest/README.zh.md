# Neo N4 Gateway SP1 Guest

这个独立的 SP1 6.2.1 guest 证明 `doc.md` §4.1 定义的 Phase-5 Gateway 语句。

- 输入严格为 `NEO4GWP1 || binding170 || countLE32 || commitments`。
- 每个 commitment 都是 .NET `BatchSerializer` 的规范编码，必须按
  `(chainId, batchNumber)` 严格排序、使用 `ProofType.Zk`，并由 guest 重新计算根。
- 每个延迟验证的子证明都使用编译期锁定的 batch guest VK，以及
  `0x00 || batch.PublicInputHash` 公共值。
- 唯一承诺输出为 `0x00 || Hash256(binding170)`。

独立 host 测试必须显式启用非生产特性：

```bash
cargo test -p neo-zkvm-gateway-guest --features test-only-vk
```

生产发布工件必须通过 `neo-zkvm-gateway-host` 生成。仓库中的 `vk_manifest.rs` 固定 SP1
版本、batch/Gateway 验证密钥以及两个 guest ELF 摘要；host 构建会重新推导并逐项比较。
任一固定值缺失、为零或不一致时，生产构建都会失败关闭。`test-only-vk` 是唯一明确的
host 测试旁路，禁止用于生产。
