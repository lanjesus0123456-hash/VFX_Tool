# VFX_Tool — Bug 修复与改进说明
**日期：2026-03-31**

---

## 文件结构
```
F:\ClaudeCode\claude-work\VFX_Tool\
├── Editor\
│   ├── VFXMeshGeneratorWindow.cs            特效面片生成器（编辑器工具）
│   └── VFXSplineMeshGeneratorEditor.cs      样条线面片生成器 Inspector + Scene Handles
├── Runtime\
│   ├── SequenceFrameBlend.cs                序列帧升格控制器（新增）
│   ├── VFXSplineMeshGenerator.cs            样条线面片生成器（运行时组件）
│   └── ParticlesHomingEditMode.cs           粒子追踪（支持编辑模式预览）
└── Shaders\
    ├── VFX_SequenceFrameBlend.shader        序列帧升格平滑
    ├── VFX_PivotFracture.shader             轴点破碎
    ├── VFX_VertexOffsetSpeed.shader         顶点偏移速度感
    ├── VFX_RampColor.shader                 颜色重映射
    ├── VFX_HeatDistortion.shader            热扭曲（GrabPass版）
    ├── VFX_Dissolve.shader                  溶解
    └── VFX_Outline.shader                   描边
```

**使用方式：** 将 `Editor` 文件夹放入 Unity 项目的任意 `Editor` 文件夹下；`Runtime` 和 `Shaders` 放入 `Assets` 下任意位置即可。

---

## 关键 Bug 修复清单

### 🔴 BUG #1 — Helix 端点 tangent 为零导致 NaN 崩溃
**文件：** `VFXMeshGeneratorWindow.cs` → `BuildHelix()`
**原因：** t=1 时 `tNext = Clamp01(t + 0.01) = 1.0`，导致 `pNext == center`，`tangent = normalize(0,0,0) = NaN`，进而所有后续顶点位置变为 NaN
**修复：** t=1 时改用后向差分计算 tangent，并增加安全检查 fallback 到 `Vector3.up`

### 🔴 BUG #2 — HeadSoftness 柔化完全无效
**文件：** `VFX_VertexOffsetSpeed.shader` → `vert()`
**原因：** 原代码 `smoothstep(0, _HeadSoftness, t > 0.5 ? 1-t : t+0.5)` — 当 t∈[0, 0.5] 时结果为 t+0.5 ∈ [0.5, 1.0]，远大于 _HeadSoftness (max 0.5)，smoothstep 始终返回 1.0
**修复：** 改为 `smoothstep(0.0, _HeadSoftness, t)` — UV.y=0 处 alpha=0，平滑过渡到 _HeadSoftness 处 alpha=1

### 🟡 BUG #3 — RampColor CustomEditor 报错
**文件：** `VFX_RampColor.shader` 末尾
**原因：** `CustomEditor "ShaderGUI"` 引用了不存在的编辑器类名，Unity 会输出 "Could not create custom shader GUI" 错误
**修复：** 移除 CustomEditor 声明，使用默认 Inspector

### 🟡 BUG #4 — Outline 描边 Alpha 半透明不生效
**文件：** `VFX_Outline.shader` → Pass 1 "OUTLINE"
**原因：** Pass 1 没有 Blend 指令（默认 One Zero = 不透明），`_OutlineAlpha` 参数写入了输出颜色的 alpha 通道但不参与混合，实际效果始终不透明
**修复：** Pass 1 添加 `Blend SrcAlpha OneMinusSrcAlpha`

### 🟢 FIX #5 — 棋盘格纹理内存泄漏
**文件：** `VFXMeshGeneratorWindow.cs` → `EnsurePreview()` / `DestroyPreview()`
**原因：** `CreateCheckerTexture()` 创建的 Texture2D 没有设置 `HideAndDontSave`，也没有在 `DestroyPreview()` 中显式销毁
**修复：** 添加 `checkerTex.hideFlags = HideAndDontSave`，`DestroyPreview()` 中添加 `DestroyImmediate(checkerTex)`

### 🟢 FIX #6 — 边缘透明增加 V 方向支持
**文件：** `VFXMeshGeneratorWindow.cs` → `ApplyEdgeAlpha()`
**原因：** 原始实现只基于 UV.x 方向做边缘渐变，对 Ring/Helix 等类型的 V 方向边缘无效
**修复：** 透明调整1 控制 U 方向渐变，透明调整2 控制 V 方向渐变，二者相乘

### 🆕 新增 #7 — SequenceFrameBlend.cs 控制器脚本
**文件：** `Runtime/SequenceFrameBlend.cs`
**原因：** Shader 文档中要求配套 C# 控制器脚本，但从未生成
**功能：**
- 自动计算 FrameA/FrameB/BlendFactor 并写入 Material
- 支持循环/非循环播放
- 提供 Play/Pause/Resume/Stop/SetFrame API
- Shader 属性 ID 预缓存（PropertyToID）
- OnDestroy 自动清理实例化材质

---

## 未改动（确认无 Bug）
- `VFX_PivotFracture.shader` — 逻辑正确，hash/Rodrigues 旋转/缩放淡出均工作正常
- `VFX_HeatDistortion.shader` — GrabPass + 双层噪声逻辑正确（URP 替换需手动处理）
- `VFX_Dissolve.shader` — clip + edgeFactor + 方向性溶解逻辑正确
- `VFX_SequenceFrameBlend.shader` — FrameUV 帧编号到 UV 映射逻辑正确
- `VFXSplineMeshGeneratorEditor.cs` — Inspector/Handles/SaveMesh 逻辑正确，无需修改


---

## 第二批修复（2026-03-31 补充）

### 🔴 BUG #8 — RMF 双反射算法第二步反射轴错误
**文件：** `VFXSplineMeshGenerator.cs` → `BuildRotationMinimizingFrames()`
**原因：** 原代码的第二次反射轴 `v2 = t_cur - t_prev` 与第一次反射轴 `v1` 完全相同，退化为单反射。根据 Wang et al. 论文，第一步应先反射法线 n_prev 和切线 t_prev 得到 n_L 和 t_L，第二步反射轴应为 `v2 = t_cur - t_L`
**修复：** 补全 `t_L = t_prev - (2/c1) * dot(v1, t_prev) * v1`，再用 `v2 = t_cur - t_L` 做第二次反射

### 🟡 BUG #9 — Mesh 未标记 HideAndDontSave（内存泄漏风险）
**文件：** `VFXSplineMeshGenerator.cs` → `OnEnable()`
**原因：** ExecuteInEditMode 下创建的 Mesh 没有设 hideFlags，会被编辑器序列化进场景产生冗余数据
**修复：** 添加 `_mesh.hideFlags = HideFlags.HideAndDontSave`，并在 `OnDestroy()` 中显式销毁

### 🔴 BUG #10 — 编辑模式追踪完全不工作（Time.deltaTime = 0）
**文件：** `ParticlesHomingEditMode.cs` → `LateUpdate()`
**原因：** 编辑器非运行模式下 `Time.deltaTime` 始终为 0，导致 `_editModeTimer` 永不增长，追踪力计算 `force * 0 = 0`，粒子不会移动
**修复：** 编辑模式改用 `EditorApplication.update` 回调驱动，通过 `EditorApplication.timeSinceStartup` 计算真实 deltaTime。运行模式继续走 `LateUpdate + Time.deltaTime`

### 🟢 FIX #11 — 编辑模式粒子全灭后仍空跑
**文件：** `ParticlesHomingEditMode.cs` → `UpdateParticles()`
**原因：** 原代码 `particleCount == 0` 时仅在 `Application.isPlaying` 下调用 `Deactivate()`，编辑模式会持续空调用
**修复：** 提取 `UpdateParticles(deltaTime)` 统一方法，运行时和编辑模式共用，粒子为 0 时运行模式停用

---

## 第三批修复（2026-06-12，v1.0.4）

### 🟡 BUG #12 — Ring「UV 模式」下拉不显示
**文件：** `VFXMeshGeneratorWindow.cs` → `DrawMeshTypeParams()` Ring 分支
**原因：** `ringUVMode` 字段与 `BuildRing()` 圆形 UV 逻辑都在，但面板漏画了对应的 `EnumPopup`，用户无从选择 Auto / Rect / Circular
**修复：** 补回下拉控件（后续移入「UV 设置」折叠区，仅 Ring 类型显示）

### 🔴 BUG #13 — Spline 矩形框选框一直残留（卡死）
**文件：** `VFXMeshGeneratorWindow.cs` → `DrawSplineHandles()`（OnSceneGUI）
**原因：** 起框（MouseDown）时未抢占 `GUIUtility.hotControl` 也未 `e.Use()`，MouseUp 被 SceneView 默认工具吞掉，复位分支收不到事件，`isBoxSelecting` 卡在 true，框选矩形每帧重绘、一直跟随鼠标
**修复：** 起框时 `GUIUtility.hotControl = boxCtrlId` + `e.Use()`；拖拽/抬起按 hotControl 归属处理并复位；画框块加 `hotControl == boxCtrlId` 门槛防鬼影。点手柄/移动 gizmo 在更早处理，不受影响
