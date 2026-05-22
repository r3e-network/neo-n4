# NeoVM 3.9 一致性测试夹具

`neo_vm_3_9_conformance.json` 保存从 NuGet `Neo.VM` `3.9.0` 生成的纯 VM 执行向量。

版本链如下：

- `neo-node` `v3.9.2`
- `Neo` `3.9.1`
- `Neo.VM` `3.9.0`

修改目标 NeoVM 包版本后，需要重新生成该夹具：

```powershell
dotnet run --project tools/NeoVm39ConformanceGenerator -- tests/fixtures/neo_vm_3_9_conformance.json
cargo test --test neo_vm_3_9_conformance --locked
```

这些向量刻意避开 interop syscall。它们覆盖可由 `Neo.VM.ExecutionEngine` 和 Rust 解释器直接执行的确定性 VM 语义。
