# Neo N4 Gateway SP1 Host

`prove-gateway` 是 `doc.md` §4.1 定义的独立 SP1 6.2.1 递归 Gateway prover daemon。
它从新构建的 ELF 推导并锁定 batch/Gateway VK，只解析规范的扁平 compressed sidecar，
验证每个子证明，生成 356 字节 Gateway Groth16 证明，在 host 再次验证，并最后发布结果
manifest。

生产构建只允许在经审计的不可变 SP1 6.2.1 amd64 镜像 digest
`sha256:14d3c46eff7492f87e429bfbf618e3d33499ba7515b15c36eeb1bcaebc9f7b7f`
下执行 `cargo prove build --docker --locked`。构建脚本会拒绝其它
`SP1_DOCKER_IMAGE`，并把两个 Docker ELF 的哈希与 VK 对照 guest 中拆分的
`batch_vk_manifest.rs` 与 `vk_manifest.rs` fail closed 校验；Gateway guest 不会
把自己的输出 pin 编译进自身。

```bash
export SP1_DOCKER_IMAGE=ghcr.io/succinctlabs/sp1@sha256:14d3c46eff7492f87e429bfbf618e3d33499ba7515b15c36eeb1bcaebc9f7b7f
export SP1_GNARK_IMAGE=ghcr.io/succinctlabs/sp1-gnark@sha256:be8555f1ad90870acd8c6ec7fd3ba0b1a2133ea9cddf25e130665aa651129e54
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

SP1 6.2.1 发布的 gnark 镜像仅提供 `linux/amd64`。在 Apple Silicon 上应启用 SP1
上游自带的原生 gnark backend，让最终 wrapper proof 使用原生 arm64 Go，而不是经过
不稳定的 amd64 仿真层。该选项只改变 wrapper prover 的执行通道；测试仍会验证完全相同、
已固定 Gateway VK 的 Groth16 proof：

```bash
export LIBCLANG_PATH="$(brew --prefix llvm)/lib"
cargo test --release -p neo-zkvm-gateway-host --features native-gnark \
  proves_and_host_verifies_real_recursive_gateway_groth16 -- --ignored --exact
```
