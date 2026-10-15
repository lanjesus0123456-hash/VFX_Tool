# VFX Mesh Generator

VFX 特效面片程序化生成工具（Unity Editor 扩展）。

## 功能
- 8 种 Mesh 类型：Plane / Ring / Cross / Sphere / Helix / Spline / GhostTrail 刀光 / Custom 自定义多边形
- 顶点色渐变（UV 渐变 + 高度渐变）
- 双套 UV（UV1 / UV2 各自独立 Rotate、Tiling、翻转、UV 模式），Tiling 上限可自定义
- UV 模式：矩形 / 圆形 / 极坐标（Plane、Ring、Custom）
- 全局 Noise 形变（噪声贴图偏移顶点，5 种方向 + UV/高度遮罩）
- 独立 UV 预览窗口（UV1/UV2 切换 + 贴图背景 + 缩放平移）
- 预览专用（不烘焙进模型）：流速滚动、预览平铺、平铺缩略图、剔除背面
- Custom：边界控制点起伏（MVC 高度插值）+ 随边界跟随的高度控制点 + 可调不规则边缘
- Spline：多条独立路径合并为一个 Mesh；控制点位置数值可选 Local / World Position
- Spline / Custom 控制点编辑：移动 / 旋转 / 缩放 × 世界 / 本地轴向，框选、软选、Shift 插点
- Helix：沿 Y 高度的整体 XZ 轮廓缩放曲线，可收细腰部
- 各含十字面片的类型（Ring / Helix / Spline / GhostTrail / Cross）可单独控制新增十字面的宽度
- 轴点自由偏移、法线翻转
- FBX 导出（含 UV2 / 顶点色；可存到工程内或桌面）

## 使用
菜单 `Tools > VFX Mesh Generator`（快捷键 Alt+Ctrl+U）

UV 预览窗口：`Tools > VFX UV Viewer`，或主面板 UV 设置里的「打开 UV 预览窗口」。

## v1.6.0 使用提示

- **Spline 多路径**：选 Spline 后按「+ 新增 Spline」复制并错开当前路径；下拉框或点击蓝色控制点切换路径。所有路径使用同一形态和宽度设置，保存/导出时合并成一个 Mesh。
- **Spline 坐标切换**：「坐标空间」决定操纵轴朝向；「点位置坐标系」决定选中点数值框显示 Local Position 或 World Position。切换显示方式不会移动点。
- **Helix 腰部**：「沿 Y 轮廓缩放」横轴从底部到顶部，纵轴为整个 XZ 截面的倍数。中点设 0.5 可将腰部收至一半；默认曲线全为 1。
- **Custom 高度点**：开启「边界高度插值」后，面内高度点的位置以边界曲面为基准。边界 Y 抬升时高度点会跟随，面板中的高度仍是相对高度。
- **Custom 不规则边缘**：开启后调「边缘起伏幅度」「每边细分数」「起伏频率」「随机种子」。绿色控制点是固定锚点，蓝色线为实际生成边缘；幅度过大导致自交时工具会自动降低扰动。

