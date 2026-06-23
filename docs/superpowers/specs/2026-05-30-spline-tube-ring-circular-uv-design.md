# 设计文档：Spline 管状面 + Ring 圆形 UV

**日期：** 2026-05-30
**目标文件：** `Editor/VFXMeshGeneratorWindow.cs`（5 份副本内容一致，以 `Editor/` 为权威源）
**约束：** 不改 FBX 导出、不改预览材质、不影响其他面片类型

---

## 背景

当前 VFX Mesh Generator 有 6 种面片：Plane / Ring / Cross / Sphere / Helix / Spline。
本次迭代针对两个真实特效制作痛点：

1. Spline 只能生成扁平条带/十字片，做不了拖尾/能量柱常用的管状面。
2. Ring 高度压到 0 当圆盘用时，UV 是 `(环向, 高度)` 矩形展开，圆盘被拉直成矩形带，贴图变形。

---

## 需求 1：Spline 三选一形态下拉 + 管状面

### 数据结构变更
- 移除 `private bool splineIsCross`
- 新增 `public enum SplineShape { Strip, Cross, Tube }`
- 新增 `private SplineShape splineShape = SplineShape.Strip`
- 新增 `private int splineTubeSides = 8`（范围 3~32）

### UI 变更（`DrawMeshTypeParams` 的 Spline case）
- 将"是否为十字片"开关替换为 `EnumPopup("形态", splineShape)`
- 当 `splineShape == Tube` 时，额外显示 `IntSlider("截面边数", splineTubeSides, 3, 32)`

### 几何变更（`BuildSpline`）
- Strip / Cross 模式：保持现有逻辑不变（Cross = 原 isCross=true 的 2-pass）
- Tube 模式：
  - 每个路径采样点 `samples[i]` 生成一圈 `splineTubeSides + 1` 个顶点（首尾重叠以接缝 UV 连续）
  - 截面基底用现有 RMF：`right = rights[i]`、`up = Cross(tangents[i], rights[i])`
  - 半径 `w = splineW * splineWidthCurve.Evaluate(t) * 0.5f`（复用宽度+宽度曲线）
  - twist + 每点翻转：继续生效，绕 `tangents[i]` 旋转整圈截面（与现有 twist 逻辑一致）
  - 顶点位置：`samples[i] + (cos(a)*right + sin(a)*up) * w`，`a = 周向角`
  - UV：`U = (float)c / splineTubeSides`，`V = t`
  - 三角形：相邻两圈 `(sides+1)` 顶点连成四边形带
  - 两端开口，不封头

### 翻转处理
- Tube 是封闭面，`RebuildMesh` 里 Spline 默认不翻转。Tube 截面用 CCW 绕序生成外法线朝外；若用户勾"翻转正反面"再整体翻转。保持现有 `needFlip` 逻辑（Spline 不在默认翻转列表）。

### ResetAllParams
- `splineShape = SplineShape.Strip`、`splineTubeSides = 8`

---

## 需求 2：Ring 圆形 UV 模式

### 数据结构变更
- 新增 `public enum RingUVMode { Auto, Rect, Circular }`
- 新增 `private RingUVMode ringUVMode = RingUVMode.Auto`

### UI 变更（`DrawMeshTypeParams` 的 Ring case）
- 在 Ring 参数末尾加 `EnumPopup("UV 模式", ringUVMode)`

### 几何变更（`BuildRing`）
- 判定是否用圆形 UV：
  - `Auto`：`ringHeight < 0.01f` → 圆形，否则矩形
  - `Rect`：强制矩形（现有逻辑）
  - `Circular`：强制圆形
- 圆形 UV 算法（XZ 直接映射）：
  - `maxR = Mathf.Max(ringTopR, ringBotR)`，安全下限 `max(maxR, 0.0001f)`
  - 对每个顶点：`u = pos.x / maxR * 0.5f + 0.5f`，`v = pos.z / maxR * 0.5f + 0.5f`
  - 圆心在 UV(0.5, 0.5)，外圈贴 UV 边缘，内圈有洞则 UV 中间留空
  - 十字片（ringIsCross）pass：圆形 UV 模式下其顶点不在 XZ 平面，仍用矩形 UV（圆形 UV 仅对主环面 pass 有意义）
- 圆形 UV 在 `BuildRing` 内写入 `uvs`；之后 `ApplyUV`（Tiling/Rotate）照常作用（对圆形贴图做缩放/旋转，保留）

### ResetAllParams
- `ringUVMode = RingUVMode.Auto`

---

## 影响范围

| 改动 | 函数/字段 |
|------|----------|
| Spline 字段 | 替换 `splineIsCross` → `splineShape` + `splineTubeSides` |
| Ring 字段 | 新增 `ringUVMode` |
| UI | `DrawMeshTypeParams`（Ring case + Spline case） |
| 几何 | `BuildSpline`（加 Tube 分支）、`BuildRing`（加圆形 UV 分支） |
| 重置 | `ResetAllParams` |
| Scene Handles | `DrawSplineHandles` 不变（管状仍用同一组控制点） |

**不动：** FBX 导出、预览材质、Plane/Cross/Sphere/Helix、顶点色、Pivot、Transform。

---

## 测试验证（Unity 引擎内手动验证，无单测框架）

1. Spline → 形态下拉切 Strip/Cross/Tube，几何正确切换，无报错
2. Tube：截面边数 3→32 平滑变化；宽度曲线让管两端粗细不同；twist 让管扭转
3. Tube：顶点数/面数统计合理（`(cols+1)*(sides+1)` 量级）
4. Ring：高度=0 + Auto → 圆形 UV，拖入棋盘贴图看到圆形不变形
5. Ring：高度=0.5 + Auto → 矩形展开（立体环面）
6. Ring：Rect/Circular 强制模式符合预期
7. 切换其他 5 种类型无回归
8. 重置参数后 Spline/Ring 回默认
9. 保存 FBX 正常（管状面也能导出）
