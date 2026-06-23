# 设计文档：Helix 头尾扭曲 + Spline 多选/软选 + 预览隐藏

**日期：** 2026-05-30
**目标文件：** `Editor/VFXMeshGeneratorWindow.cs`（唯一开发源）
**约束：** 只动本文件，不碰 FBX 导出、不碰其他面片几何逻辑

---

## 功能 1：Helix 头尾扭曲

### 数据结构
- 新增 `private float helixHeadTwist = 0f;`（范围 -360~360）
- 新增 `private float helixTailTwist = 0f;`（范围 -360~360）

### UI（`DrawMeshTypeParams` Helix case）
- 在 `helixCurve` 之后新增两行：
  - `helixHeadTwist = EditorGUILayout.Slider("头部扭曲", helixHeadTwist, -360f, 360f);`
  - `helixTailTwist = EditorGUILayout.Slider("尾部扭曲", helixTailTwist, -360f, 360f);`

### 几何（`BuildHelix`）
- 在每个截面 `t` 计算额外扭转角：
  ```csharp
  float wHead = 1f - Mathf.SmoothStep(0f, 0.5f, t);   // t=0→1, t≥0.5→0
  float wTail = Mathf.SmoothStep(0.5f, 1f, t);         // t≤0.5→0, t=1→1
  float extraTwist = helixHeadTwist * wHead + helixTailTwist * wTail;
  ```
- 把主条带 `bitang` 绕 `tangent` 旋转 `extraTwist`：
  ```csharp
  if (Mathf.Abs(extraTwist) > 0.01f)
      bitang = Quaternion.AngleAxis(extraTwist, tangent) * bitang;
  ```
  （此句放在 `bitang` 安全检查之后、生成主条带顶点之前。十字片的 `crossBit` 基于 `bitang` 派生，自动跟随扭转）

### ResetAllParams
- `helixHeadTwist = 0f; helixTailTwist = 0f;`

---

## 功能 2：Spline 多选 + 软选

### 数据结构
- 保留 `selectedSplinePoint`（int，单点翻转 UI 仍用它）
- 新增 `private List<int> selectedPoints = new List<int>();`（多选集合）
- 新增软选字段：
  - `private bool splineSoftSelect = false;`
  - `private float splineSoftRadius = 1.5f;`（范围 0.1~10）
  - `private AnimationCurve splineSoftCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);`（0距离=1，半径处=0）
- 新增框选状态：`private bool isBoxSelecting = false;` `private Vector2 boxSelectStart;`

### UI（`DrawMeshTypeParams` Spline case，非 Tube 也适用）
- 在 Spline 操作区加软选折叠/开关：
  - `splineSoftSelect = EditorGUILayout.Toggle("软选", splineSoftSelect);`
  - 开启时显示 `splineSoftRadius`(Slider 0.1~10) 和 `splineSoftCurve`(CurveField)
- HelpBox 文本补充：`框选：在空白处拖出矩形多选控制点`

### Scene 交互（`DrawSplineHandles`）
- **框选**：
  - 左键在非控制点空白区按下 → `isBoxSelecting = true`, 记 `boxSelectStart = mousePos`
  - 拖动时 `Handles.BeginGUI` 画半透明矩形
  - 松开 → 遍历控制点，`WorldToGUIPoint` 落在矩形内的加入 `selectedPoints`；清空旧选；`e.Use()`
  - 框为空（无点命中）则清空选集
- **多点整体移动**：
  - `selectedPoints.Count >= 1` 时，在选中点**质心**画一个 `DoPositionHandle`
  - 拖动产生 `delta = newCenter - oldCenter`
  - 选中点：`splinePoints[idx] += delta`（全量）
  - 软选（若开启）：对**未选中**点，按到最近选中点的距离 `d`，若 `d < splineSoftRadius`，权重 `w = splineSoftCurve.Evaluate(d / splineSoftRadius)`，`splinePoints[other] += delta * w`
- 单点高亮：`selectedPoints` 内的点画黄色，其余绿色
- `selectedSplinePoint` 同步为 `selectedPoints` 的最后一个（供单点翻转 UI 用），空选时 -1

### 兼容
- 现有单击选点：改为单击=清空选集后只选该点（`selectedPoints = {i}`）
- Shift+左键插点、右键删除/添加、单点翻转 UI 全部保留
- `SyncSplineFlips` 不变；删点时同步从 `selectedPoints` 移除并修正索引

### ResetAllParams
- `selectedPoints.Clear(); splineSoftSelect = false; splineSoftRadius = 1.5f;`
- `splineSoftCurve = AnimationCurve.EaseInOut(0,1,1,0);`

---

## 功能 3：预览模型隐藏开关

### 数据结构
- 新增 `private bool hidePreview = false;`

### UI（Transform 区，`showWireframe` 那一行附近）
- `hidePreview = EditorGUILayout.Toggle("隐藏预览", hidePreview);`
- 变更时调用 `if (previewMR != null) previewMR.enabled = !hidePreview;`

### 行为
- `RebuildMesh` 末尾、`ApplyPreviewMaterial` 之后，加 `if (previewMR != null) previewMR.enabled = !hidePreview;`（保证重建后状态正确）
- `OnSceneGUI`：`hidePreview` 时跳过 `DrawWireframe`（线框也隐藏）；Spline Handles **保留**（隐藏面片时仍要能编辑路径）
- 只关 `MeshRenderer.enabled`，不销毁 previewGO

### ResetAllParams
- `hidePreview = false;`（并恢复 `previewMR.enabled = true`）

---

## 影响范围

| 改动 | 函数/字段 |
|------|----------|
| Helix 字段 | +headTwist/tailTwist |
| Helix UI/几何 | DrawMeshTypeParams、BuildHelix |
| Spline 字段 | +selectedPoints/软选三件套/框选状态 |
| Spline UI/交互 | DrawMeshTypeParams、DrawSplineHandles |
| 预览 | +hidePreview，OnGUI Transform 区、RebuildMesh、OnSceneGUI |
| 重置 | ResetAllParams |

**不动：** FBX 导出、预览材质工厂、Plane/Ring/Cross/Sphere、Tube/Strip/Cross 几何主体、顶点色、Pivot。

---

## 测试验证（Unity 引擎内手动）

1. Helix 头部扭曲调 90° → 头部条带绕路径转 90°，中间不转；尾部同理；两者叠加
2. Helix 头尾都为 0 → 与改动前一致（无回归）
3. Spline 空白处拖框 → 框内控制点全选中变黄
4. 多选后拖质心 Handle → 选中点整体平移
5. 开软选 + 半径 1.5 → 移动选中点，半径内未选点按衰减跟随；调衰减曲线影响跟随强度
6. 关软选 → 只有选中点动
7. 单击单点 → 退回单选，单点翻转 UI 正常
8. 删点后多选索引不串
9. 勾"隐藏预览" → Scene 不显示面片和线框，参数仍可调；Spline Handles 仍可编辑；取消勾选恢复
10. 重置参数 → 三功能回默认
11. 保存 FBX 正常（隐藏预览时也能保存）
