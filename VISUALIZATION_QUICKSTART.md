# 🎬 Neo N4 交互式 3D 演示 - 快速启动

体验 Neo N4 Layer 2 Rollup 的完整技术架构和工作流程，通过炫酷的 3D 可视化！

## 🚀 30 秒快速启动

### Windows 用户
```cmd
cd D:\Git\neo-n4\demo\neo-n4-visualization
start.bat
```

### Linux/Mac 用户
```bash
cd /path/to/neo-n4/demo/neo-n4-visualization
./start.sh
```

浏览器将自动打开 `http://localhost:3000`

## 🎮 使用演示

1. 点击 **"▶ 开始演示"** 按钮
2. 观看完整的 6 阶段动画循环（约 18 秒/批次）
3. 使用控制按钮：
   - **⏸ 暂停/继续**：随时暂停和恢复
   - **🔄 重置**：返回初始状态
   - **⚡ 2x 速度**：加速观看

## 🎯 你将看到什么

### 6 个动画阶段

1. **交易收集**（3秒） - 黄色粒子飞向青色 L2 序列器
2. **批次封装**（2.5秒） - L2 序列器脉冲发光，生成状态根
3. **零知识证明生成**（4秒） - 粉色 zkProver 发射光束到 RollupHub
4. **批次提交到 L1**（3秒） - 黄色批次立方体传输到 Neo 主链
5. **批次终局化**（2.5秒） - RollupHub 和 Neo 主链同时发光
6. **Gateway 发布**（3秒） - 批次数据传输到紫色 Gateway

### 实时统计面板

- **批次编号**：递增的批次序号
- **交易数量**：每批次 100-600 笔随机交易
- **区块范围**：如 1000-1999
- **状态转换**：收集中 → 封装完成 → 已终局
- **证明状态**：未生成 → 生成中 → 已提交 → 已发布

## 🎨 视觉组件说明

| 颜色 | 组件 | 形状 | 位置 |
|------|------|------|------|
| 🔵 青色 | L2 序列器 | 六边形柱体 | 左上 |
| 🟠 橙色 | Neo 主链 | 立方体 | 右上 |
| 🟢 绿色 | RollupHub | 八面体 | 中央 |
| 🔴 粉色 | zkVM 证明器 | 菱形 | 左下 |
| 🟣 紫色 | Gateway 网关 | 圆环 | 右下 |

## 📚 完整文档

- **README.md** - 项目概述和功能特性
- **GUIDE.md** - 详细使用指南和故障排除
- **PROJECT_SUMMARY.md** - 技术架构和代码亮点

## ⚙️ 系统要求

- **浏览器**: Chrome 90+ / Edge 90+ / Firefox 88+ / Safari 14+
- **Node.js**: 16+ (首次运行需要安装依赖)
- **内存**: 4GB+
- **显卡**: 支持 WebGL

## 🎥 录制视频

使用 OBS Studio 或浏览器录屏扩展，推荐设置：
- 分辨率：1920x1080
- 帧率：60fps
- 录制时长：1-2 分钟（完整展示 3-6 个批次循环）

## 🐛 遇到问题？

### Q: 页面加载慢或空白？
A: 首次加载需要下载 Three.js 库（约 600KB），等待几秒钟即可。

### Q: npm install 失败？
A: 使用国内镜像源：
```bash
npm config set registry https://registry.npmmirror.com
npm install
```

### Q: 3D 场景不显示？
A: 确认浏览器支持 WebGL，并在设置中启用硬件加速。

### Q: 动画卡顿？
A: 关闭其他标签页，使用 Chrome/Edge 浏览器，避免使用 2x 速度。

## 📞 获取帮助

详细文档：`demo/neo-n4-visualization/GUIDE.md`

---

**现在就试试吧！** 🚀

```bash
cd demo/neo-n4-visualization && npm run dev
```