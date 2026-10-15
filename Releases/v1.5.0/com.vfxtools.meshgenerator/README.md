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
- Custom：边界控制点起伏（MVC 高度插值）+ 面内高度控制点雕刻
- Spline / Custom 控制点编辑：移动 / 旋转 / 缩放 × 世界 / 本地坐标，框选、软选、Shift 插点、坐标数值输入
- 各含十字面片的类型（Ring / Helix / Spline / GhostTrail / Cross）可单独控制新增十字面的宽度
- 轴点自由偏移、法线翻转
- FBX 导出（含 UV2 / 顶点色；可存到工程内或桌面）

## 使用
菜单 `Tools > VFX Mesh Generator`（快捷键 Alt+Ctrl+U）

UV 预览窗口：`Tools > VFX UV Viewer`，或主面板 UV 设置里的「打开 UV 预览窗口」。
