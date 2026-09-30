# Neo N4 技术演示视频 - 安装与使用指南

## 📦 一键安装与启动

### Windows 用户

1. **安装依赖并启动**

```cmd
cd D:\Git\neo-n4\demo\neo-n4-visualization
npm install
npm run dev
```

2. **浏览器自动打开** `http://localhost:3000`

### Linux/Mac 用户

```bash
cd /path/to/neo-n4/demo/neo-n4-visualization
npm install
npm run dev
```

## 🎮 使用说明

### 首次使用

1. 页面加载后，会看到 Neo N4 的 3D 架构场景
2. 左侧面板显示项目介绍和核心特性
3. 右侧面板显示实时统计数据
4. 底部中央是控制按钮

### 控制按钮说明

| 按钮 | 功能 | 说明 |
|------|------|------|
| ▶ 开始演示 | 启动动画 | 开始完整的 6 阶段技术流程演示 |
| ⏸ 暂停 | 暂停/继续 | 随时暂停或继续演示 |
| 🔄 重置 | 重置状态 | 返回初始状态，清空统计数据 |
| ⚡ 2x 速度 | 速度切换 | 在 1x 和 2x 速度之间切换 |

### 演示流程

点击"开始演示"后，系统将依次展示 6 个阶段：

#### 阶段 1: 交易收集阶段 (3秒)
- **现象**：多个黄色粒子从四周飞向青色的 L2 序列器
- **说明**：用户提交的交易被 L2 序列器收集和排队
- **统计**：交易数量实时更新（100-600 笔）

#### 阶段 2: 批次封装阶段 (2.5秒)
- **现象**：L2 序列器产生脉冲发光效果
- **说明**：交易被打包成批次，计算状态根
- **统计**：区块范围显示（如 1000-1999）

#### 阶段 3: 零知识证明生成 (4秒)
- **现象**：粉色 zkProver 发光，并向 RollupHub 发射光束
- **说明**：SP1 zkVM 为批次生成零知识证明
- **统计**：证明状态从"未生成"变为"生成中"

#### 阶段 4: 批次提交到 L1 (3秒)
- **现象**：黄色立方体（批次）从 L2 → RollupHub → Neo 主链传输
- **说明**：批次承诺和证明提交到 Neo 主链
- **统计**：证明状态更新为"已提交"

#### 阶段 5: 批次终局化 (2.5秒)
- **现象**：RollupHub（绿色）和 Neo 主链（橙色）同时脉冲发光
- **说明**：L1 验证通过，批次状态终局，更新规范根
- **统计**：状态转换显示"已终局"

#### 阶段 6: Gateway 发布 (3秒)
- **现象**：批次传输到紫色 Gateway，Gateway 发光
- **说明**：跨链消息根发布到 SharedBridge
- **统计**：证明状态显示"已发布"

### 循环播放

完成 6 个阶段后，系统自动进入下一批次，批次编号递增，流程循环播放。

## 🎨 场景交互

### 相机控制
- 相机会自动轻微摇摆，提供动态视角
- 当前版本为自动演示模式，无需手动控制

### 视觉元素

| 元素 | 颜色 | 位置 | 说明 |
|------|------|------|------|
| L2 序列器 | 青色 | 左上 | 六边形柱体，带旋转光环 |
| Neo 主链 | 橙色 | 右上 | 立方体，带环绕粒子 |
| RollupHub | 绿色 | 中央 | 八面体，双重旋转框架 |
| zkVM 证明器 | 粉色 | 左下 | 菱形，带脉冲核心 |
| Gateway | 紫色 | 右下 | 圆环，旋转光晕 |
| 交易粒子 | 黄色 | 动态 | 小球体，飞行轨迹 |
| 批次数据 | 黄色 | 动态 | 立方体，旋转传输 |
| 证明光束 | 粉色 | 动态 | 闪电效果 |
| 背景粒子 | 淡蓝 | 全局 | 1000个静态星点 |

## 🔧 故障排除

### 问题 1：页面加载缓慢
**原因**：首次加载需要下载 Three.js 库（约 600KB）  
**解决**：等待几秒钟，加载完成后会显示"正在加载 Neo N4 可视化引擎..."

### 问题 2：3D 场景不显示
**原因**：浏览器不支持 WebGL  
**解决**：使用现代浏览器（Chrome 90+、Firefox 88+、Edge 90+、Safari 14+）

### 问题 3：动画卡顿
**原因**：显卡性能不足或浏览器硬件加速未开启  
**解决**：
- Chrome: 设置 → 高级 → 系统 → 启用"使用硬件加速"
- 降低其他标签页占用
- 关闭 2x 速度模式

### 问题 4：npm install 失败
**原因**：网络问题或 npm 源速度慢  
**解决**：使用淘宝镜像源

```bash
npm config set registry https://registry.npmmirror.com
npm install
```

### 问题 5：端口 3000 被占用
**原因**：其他程序占用端口  
**解决**：修改 `vite.config.js` 中的端口号

```javascript
export default defineConfig({
  server: {
    port: 3001, // 改为其他端口
    open: true
  }
})
```

## 📊 性能建议

### 最佳体验配置
- **CPU**: Intel i5 / AMD Ryzen 5 或更高
- **GPU**: 集成显卡或独立显卡
- **内存**: 4GB 或以上
- **浏览器**: Chrome 100+ / Edge 100+ (推荐)
- **屏幕分辨率**: 1920x1080 或以上

### 性能优化选项
如遇性能问题，可在 `src/scene.js` 中调整：

```javascript
// 减少背景粒子数量（第 358 行）
const particleCount = 500; // 从 1000 改为 500

// 关闭阴影（第 50 行）
renderer.shadowMap.enabled = false; // 从 true 改为 false

// 降低阴影质量（第 74 行）
mainLight.shadow.mapSize.width = 1024; // 从 2048 改为 1024
mainLight.shadow.mapSize.height = 1024;
```

## 🎥 录制视频

### 使用 OBS Studio 录制

1. 下载 OBS Studio: https://obsproject.com/
2. 添加"浏览器源"，URL 填写 `http://localhost:3000`
3. 设置分辨率 1920x1080，帧率 60fps
4. 点击"开始演示"，然后在 OBS 中"开始录制"
5. 完整演示约 18 秒/批次，建议录制 2-3 个循环（约 1 分钟）

### 使用浏览器扩展录制

- **Chrome**: Loom、Screencastify
- **Edge**: Microsoft Stream
- **Firefox**: Firefox Screenshots (内置)

## 📱 移动端适配

当前版本针对桌面浏览器优化，移动端访问时：
- UI 会自动缩放
- 触摸可能不如鼠标流畅
- 建议在 iPad 或更大设备上体验

## 🌐 在线部署

### 部署到 GitHub Pages

```bash
npm run build
# 将 dist/ 目录推送到 gh-pages 分支
```

### 部署到 Vercel

```bash
npm install -g vercel
vercel
```

### 部署到 Netlify

将 `dist/` 目录拖放到 Netlify 网站即可。

## 💡 进阶定制

### 修改颜色主题
编辑 `index.html` 中的 CSS 变量和 `src/scene.js` 中的颜色值。

### 添加新的动画阶段
在 `src/scene.js` 的 `phases` 数组中添加新阶段，并实现对应的 `action` 函数。

### 导出为视频文件
使用 Three.js 的 `CanvasRecorder` 或 `ccapture.js` 库直接导出视频。

## 📞 技术支持

如有问题，请在 Neo N4 项目中提交 Issue:
https://github.com/neo-project/neo-n4/issues

---

**祝您体验愉快！** 🎉