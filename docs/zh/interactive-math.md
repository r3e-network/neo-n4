# Neo N4 数学动态教程

打开交互式教程：

[启动 Neo N4 Math Lab](../interactive-math/index.html)

Math Lab 是一个本地优先的动态教程，用来解释 NeoVM -> NeoVM2/RISC-V ->
zkVM 这一整套技术栈背后的数学原理。它面向不是密码学专家、但需要理解
Neo N4 到底证明了什么、为什么这个证明边界可信的读者。

它覆盖：

- 有限域算术与取模相等关系；
- 哈希承诺、Merkle root 与 inclusion path；
- NeoVM 栈机器状态转换语义；
- NeoVM2/RISC-V 寄存器、内存和 pc 周期执行；
- 执行轨迹与算术化；
- zkVM 证明验证与 public inputs；
- 证明聚合与 L1 结算策略；
- NeoFS DA、桥资产记账和防重放 N4 安全检查。

该页面是静态页面：不需要构建步骤，不依赖外部网络，也不会连接钱包。数学模型
位于 `docs/interactive-math/mathModel.js`，并由
`node --test tests/interactive-math/math-model.test.mjs` 覆盖。

<iframe src="../interactive-math/index.html" title="Neo N4 Math Lab" style="width: 100%; min-height: 860px; border: 1px solid #d6e1dc; border-radius: 8px;"></iframe>
