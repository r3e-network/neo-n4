# Neo N4 统一体验中心

打开本地优先的体验中心：

[启动 Neo N4 统一体验中心](../experience-hub/index.html)

体验中心是一个只读产品界面，用于理解、构建、运维和验证 Neo N4。它可视化
NeoHub 可部署 L1 合约、NeoFS DA、ContractZkVerifier、L1 可部署 ZK 验证器合约、
Gateway、SharedBridge、L2 NeoVM2/RISC-V 执行、可选 N4 L2 VM profile，
以及验证证据。

浏览器不保存私钥，也不签署部署或治理操作。有权限操作仍然留在 CLI、钱包、节点进程和合约中。

如果需要理解执行与证明路径背后的数学基础，可以打开配套的
[Neo N4 Math Lab](../interactive-math/index.html)。它用动态模型解释有限域、承诺、
NeoVM 栈语义、NeoVM2/RISC-V 周期、zkVM 算术化、证明验证、聚合和 N4 安全检查。

第一期使用确定性的本地报告和生成式仓库 manifest。真实公网 testnet 证据必须来自后续部署演练和 CI
artifact；私有 devnet 证据会在 UI 中明确标注。
