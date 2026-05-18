# Neo N4 交互式运行剧场

打开交互式模拟器：

[启动 Runtime Theater](../interactive-runtime/index.html)

该页面动态展示 Neo N4 的主要运行流程：

- L1 到 L2 充值
- 批次生命周期
- 通过 Neo Gateway 进行证明聚合
- L2 到 L1 提现
- 通过 watcher 和委员会证明接入外部链桥
- 挑战与恢复路径

它是静态页面：不需要构建步骤，不依赖外部网络，也不会连接钱包。运行逻辑位于 `docs/interactive-runtime/simulator.js`，并由 `node --test tests/interactive-runtime/simulator.test.mjs` 覆盖。

<iframe src="../interactive-runtime/index.html" title="Neo N4 Runtime Theater" style="width: 100%; min-height: 760px; border: 1px solid #d6e1dc; border-radius: 8px;"></iframe>
