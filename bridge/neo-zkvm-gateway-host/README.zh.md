# Neo N4 Gateway SP1 Host

`prove-gateway` 是 `doc.md` §4.1 定义的独立 SP1 6.2.1 递归 Gateway prover daemon。
它从新构建的 ELF 推导并锁定 batch/Gateway VK，只解析规范的扁平 compressed sidecar，
验证每个子证明，生成 356 字节 Gateway Groth16 证明，在 host 再次验证，并最后发布结果
manifest。

```bash
cargo build --release -p neo-zkvm-gateway-host
target/release/prove-gateway build-manifest
target/release/prove-gateway daemon --queue /srv/gateway/queue \
  --child-proofs /srv/gateway/compressed-batches
```

Sidecar 是 SP1 `SP1ProofWithPublicValues::save` 文件，文件名只能来自规范元组：

```text
<chainId-hex8>-<batchNumber-hex16>-<publicInputHash-hex64>.sp1-compressed-proof.bin
```

请求不能自行提供 VK 或路径。Core、Plonk、Groth16、TEE 包装、错误版本、错误公共值、
符号链接、超限文件和非普通文件子证明都会失败关闭。

快速 parser/protocol 测试显式使用非证明构建：

```bash
cargo test -p neo-zkvm-gateway-host --features test-only-vk
```

真实递归门禁会执行两阶段 SP1 真证明，因此默认标记为 ignored：

```bash
cargo test --release -p neo-zkvm-gateway-host \
  proves_and_host_verifies_real_recursive_gateway_groth16 -- --ignored --exact
```
