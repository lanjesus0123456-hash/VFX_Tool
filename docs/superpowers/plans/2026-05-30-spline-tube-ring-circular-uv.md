# Spline 管状面 + Ring 圆形 UV 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 Spline 增加管状面形态（三选一下拉），给 Ring 增加圆形 UV 模式，用于拖尾/能量柱和地面圆盘类特效。

**Architecture:** 单文件 Unity Editor 工具 `VFXMeshGeneratorWindow.cs`。改动集中在字段定义、`DrawMeshTypeParams`（UI）、`BuildSpline`/`BuildRing`（几何）、`ResetAllParams`（重置）。不动 FBX 导出、预览材质、其他面片类型。

**Tech Stack:** Unity Editor (C#), Built-in Render Pipeline, IMGUI。无单测框架——验证方式为：保存文件后 Unity 自动编译，观察 Console 无报错，再在 `Tools > VFX Mesh Generator` 窗口内手动验证几何与 UV。

**权威源文件：** `Editor/VFXMeshGeneratorWindow.cs`。共有 5 份字节相同的副本，全部实现完成后需同步（见 Task 7）。

---

### Task 1: Spline/Ring 字段改造

**Files:**
- Modify: `Editor/VFXMeshGeneratorWindow.cs`（Spline 字段区 86-101 行附近；Ring 字段区 50-57 行附近）

- [ ] **Step 1: 替换 Spline 的 isCross 开关为 SplineShape 枚举 + 管状边数**

把第 89 行：
```csharp
    private bool           splineIsCross     = false;
```
替换为：
```csharp
    public enum SplineShape { Strip, Cross, Tube }
    private SplineShape    splineShape       = SplineShape.Strip;
    private int            splineTubeSides   = 8;
```

- [ ] **Step 2: Ring 字段区新增 RingUVMode 枚举字段**

在 Ring 字段区（`ringIsCross` 那一行，第 57 行 `private bool  ringIsCross = false;` 之后）新增：
```csharp
    public enum RingUVMode { Auto, Rect, Circular }
    private RingUVMode ringUVMode = RingUVMode.Auto;
```

- [ ] **Step 3: 编译验证**

在 Unity 中保存触发编译。预期：Console 报错 `splineIsCross` 未定义（在 `DrawMeshTypeParams` 和 `BuildSpline` 仍引用旧字段）。这是预期的中间状态，下一个 Task 修复。

- [ ] **Step 4: 提交**

```bash
git add Editor/VFXMeshGeneratorWindow.cs
git commit -m "refactor: replace splineIsCross with SplineShape enum, add RingUVMode field"
```

---

### Task 2: Spline UI 改三选一下拉 + 截面边数

**Files:**
- Modify: `Editor/VFXMeshGeneratorWindow.cs`（`DrawMeshTypeParams` 的 Spline case，约 346-354 行）

- [ ] **Step 1: 替换 Spline case 的"是否为十字片"开关为形态下拉**

找到（约 349-354 行）：
```csharp
                splineRows       = EditorGUILayout.IntSlider("横截面数",   splineRows,    1, 20);
                splineCols       = EditorGUILayout.IntSlider("路径面数",   splineCols,    4, 256);
                splineW          = EditorGUILayout.Slider("宽度",        splineW,       0.01f, 5);
                splineIsCross    = EditorGUILayout.Toggle("是否为十字片", splineIsCross);
                splineTwist      = EditorGUILayout.Slider("扭曲",        splineTwist,   -360f, 360f);
                splineWidthCurve = EditorGUILayout.CurveField("宽度曲线", splineWidthCurve);
```
替换为：
```csharp
                splineShape      = (SplineShape)EditorGUILayout.EnumPopup("形态", splineShape);
                if (splineShape == SplineShape.Tube)
                {
                    splineTubeSides = EditorGUILayout.IntSlider("截面边数", splineTubeSides, 3, 32);
                    splineCols      = EditorGUILayout.IntSlider("路径面数", splineCols, 4, 256);
                    splineW         = EditorGUILayout.Slider("管半径(宽度)", splineW, 0.01f, 5);
                }
                else
                {
                    splineRows   = EditorGUILayout.IntSlider("横截面数", splineRows, 1, 20);
                    splineCols   = EditorGUILayout.IntSlider("路径面数", splineCols, 4, 256);
                    splineW      = EditorGUILayout.Slider("宽度",      splineW,    0.01f, 5);
                }
                splineTwist      = EditorGUILayout.Slider("扭曲",        splineTwist,   -360f, 360f);
                splineWidthCurve = EditorGUILayout.CurveField("宽度曲线", splineWidthCurve);
```

- [ ] **Step 2: 编译验证**

保存。预期：Console 仍报错 `BuildSpline` 里引用了 `splineIsCross`（下个 Task 修）。UI 引用已修复。

- [ ] **Step 3: 提交**

```bash
git add Editor/VFXMeshGeneratorWindow.cs
git commit -m "feat: Spline UI shape dropdown (Strip/Cross/Tube) with tube sides"
```

---

### Task 3: BuildSpline 加 Tube 几何

**Files:**
- Modify: `Editor/VFXMeshGeneratorWindow.cs`（`BuildSpline`，约 1208-1321 行）

- [ ] **Step 1: 把 isCross 逻辑改为 shape 驱动，并加 Tube 分支**

在 `BuildSpline` 中找到（约 1225 行）：
```csharp
        int rowVerts = splineRows + 1;
        int passes   = splineIsCross ? 2 : 1;
```
替换为：
```csharp
        int rowVerts = splineRows + 1;
        int passes   = (splineShape == SplineShape.Cross) ? 2 : 1;
```

- [ ] **Step 2: 在 RMF 计算之后、`for (int p = 0; p < passes; p++)` 之前插入 Tube 分支**

找到（约 1259 行）`for (int p = 0; p < passes; p++)` 这一行，在它**之前**插入完整的 Tube 处理块（Tube 模式生成几何后直接 `return`，不走下面的 strip/cross 循环）：
```csharp
        // ── Tube 模式：沿路径生成封闭圆形截面 ──
        if (splineShape == SplineShape.Tube)
        {
            int sidesV = splineTubeSides + 1;   // 首尾重叠以接缝 UV 连续
            for (int i = 0; i < samples.Count; i++)
            {
                float t = (float)i / Mathf.Max(samples.Count - 1, 1);
                float radius = splineW * splineWidthCurve.Evaluate(t) * 0.5f;

                Vector3 tangent = tangents[i];
                Vector3 right   = rights[i];

                // twist + 每点翻转（与 strip 模式一致）
                float totalTwist = splineTwist * t;
                SyncSplineFlips();
                {
                    float st = t * (splinePoints.Count - 1);
                    int fi = Mathf.FloorToInt(st);
                    float ff = st - fi;
                    fi = Mathf.Clamp(fi, 0, splinePointFlips.Count - 1);
                    int fi2 = Mathf.Min(fi + 1, splinePointFlips.Count - 1);
                    totalTwist += Mathf.Lerp(splinePointFlips[fi], splinePointFlips[fi2], ff);
                }
                if (Mathf.Abs(totalTwist) > 0.01f)
                    right = Quaternion.AngleAxis(totalTwist, tangent) * right;

                Vector3 up = Vector3.Cross(tangent, right).normalized;

                for (int c = 0; c <= splineTubeSides; c++)
                {
                    float a = (float)c / splineTubeSides * Mathf.PI * 2f;
                    Vector3 dir = Mathf.Cos(a) * right + Mathf.Sin(a) * up;
                    verts.Add(samples[i] + dir * radius);
                    uvs.Add(new Vector2((float)c / splineTubeSides, t));
                }
            }

            for (int i = 0; i < samples.Count - 1; i++)
            for (int c = 0; c < splineTubeSides; c++)
            {
                int bl = i * sidesV + c;
                int br = bl + 1;
                int tl = bl + sidesV;
                int tr = tl + 1;
                tris.Add(bl); tris.Add(tl); tris.Add(br);
                tris.Add(br); tris.Add(tl); tris.Add(tr);
            }

            return BuildMesh("SplineTube", verts, uvs, tris);
        }

```

- [ ] **Step 3: 编译验证**

保存。预期：Console 无报错（`splineIsCross` 全部清除）。

- [ ] **Step 4: 引擎内手动验证 Strip/Cross 无回归**

打开 `Tools > VFX Mesh Generator`，选 Spline。形态切 Strip → 条带正常；切 Cross → 十字片正常（等于原 isCross=true）。

- [ ] **Step 5: 引擎内手动验证 Tube**

形态切 Tube：
- 出现"截面边数"滑条，3→32 平滑变化，管面越来越圆
- 宽度曲线改成非线性 → 管两端粗细不同
- 扭曲 / 每点翻转 → 管整体扭转
- 底部顶点数 ≈ `(splineCols+1)*(splineTubeSides+1)`，面数合理
- 拖动 Scene 控制点 → 管随路径弯曲

- [ ] **Step 6: 提交**

```bash
git add Editor/VFXMeshGeneratorWindow.cs
git commit -m "feat: add Spline Tube mode (closed circular cross-section along path)"
```

---

### Task 4: BuildRing 加圆形 UV

**Files:**
- Modify: `Editor/VFXMeshGeneratorWindow.cs`（`BuildRing`，约 975-1050 行）

- [ ] **Step 1: 在主环面 pass 生成 UV 后，按模式覆盖为圆形 UV**

在 `BuildRing` 中，`QuadTris(tris, ringRows, ringCols);`（约 997 行，主环面 pass 之后、十字片 pass `if (ringIsCross)` 之前）**之前**插入圆形 UV 处理。具体：找到主环面 pass 的 `QuadTris(tris, ringRows, ringCols);`（第一处），在它**前面**插入：
```csharp
        // ── 圆形 UV 模式：把主环面顶点的 XZ 直接映射到 UV 框 ──
        bool useCircularUV =
            ringUVMode == RingUVMode.Circular ||
            (ringUVMode == RingUVMode.Auto && ringHeight < 0.01f);
        if (useCircularUV)
        {
            float maxR = Mathf.Max(Mathf.Max(ringTopR, ringBotR), 0.0001f);
            for (int i = 0; i < verts.Count; i++)
            {
                uvs[i] = new Vector2(
                    verts[i].x / maxR * 0.5f + 0.5f,
                    verts[i].z / maxR * 0.5f + 0.5f);
            }
        }
```

注意：此块在主环面 pass 全部顶点生成完、十字片 pass 之前执行，所以 `verts.Count` 此时正好等于主环面顶点数，十字片 pass 的顶点（之后才追加）保持原矩形 UV，符合设计。

- [ ] **Step 2: 编译验证**

保存。预期：Console 无报错。

- [ ] **Step 3: 引擎内手动验证圆形 UV**

选 Ring：
- 高度=0，上半径=0.01、下半径=1.5，UV 模式=Auto → 实心圆盘
- UV 设置里拖入棋盘格预览贴图（或开预览贴图）→ 看到圆形棋盘不变形，圆心在贴图中心
- 上半径=0.5、下半径=1.5、高度=0 → 圆环，UV 中间留空洞
- UV 模式=Rect → 退回矩形展开（拉直）
- UV 模式=Circular + 高度=0.5 → 强制圆形（立体环面投影，预期会压扁，符合 Circular 强制语义）

- [ ] **Step 4: 引擎内手动验证矩形模式无回归**

高度=0.5、UV 模式=Auto → 立体锥面，UV 为矩形展开（与改动前一致）。

- [ ] **Step 5: 提交**

```bash
git add Editor/VFXMeshGeneratorWindow.cs
git commit -m "feat: add Ring circular UV mode (Auto/Rect/Circular)"
```

---

### Task 5: ResetAllParams 同步新字段

**Files:**
- Modify: `Editor/VFXMeshGeneratorWindow.cs`（`ResetAllParams`，约 776-777 行 Spline 重置区 / Ring 重置区 769-770 行）

- [ ] **Step 1: 更新 Spline 重置行**

找到（约 776 行）：
```csharp
        splineRows = 2; splineCols = 16; splineW = 0.5f; splineIsCross = false;
```
替换为：
```csharp
        splineRows = 2; splineCols = 16; splineW = 0.5f;
        splineShape = SplineShape.Strip; splineTubeSides = 8;
```

- [ ] **Step 2: 更新 Ring 重置行加入 ringUVMode**

找到（约 769-770 行）：
```csharp
        ringRows = 2; ringCols = 8; ringTopR = 0.5f; ringBotR = 1.5f;
        ringHeight = 0.5f; ringArc = 360f; ringTwist = 0f; ringIsCross = false;
```
替换为：
```csharp
        ringRows = 2; ringCols = 8; ringTopR = 0.5f; ringBotR = 1.5f;
        ringHeight = 0.5f; ringArc = 360f; ringTwist = 0f; ringIsCross = false;
        ringUVMode = RingUVMode.Auto;
```

- [ ] **Step 3: 编译验证**

保存。预期：Console 无报错。

- [ ] **Step 4: 引擎内验证重置**

点"↺ 重置参数"：Spline 形态回 Strip、截面边数回 8；Ring UV 模式回 Auto。

- [ ] **Step 5: 提交**

```bash
git add Editor/VFXMeshGeneratorWindow.cs
git commit -m "feat: reset new Spline/Ring params in ResetAllParams"
```

---

### Task 6: 保存导出回归验证

**Files:**
- 无代码改动，仅验证

- [ ] **Step 1: Tube 保存 FBX**

Spline Tube 模式 → 点"保存模型" → Console 输出已保存路径，Project 里生成 FBX，导入无报错，管状几何正确。

- [ ] **Step 2: Ring 圆盘保存 FBX**

Ring 圆盘 + 圆形 UV → 保存 FBX → 导入后 UV 圆形保持。

- [ ] **Step 3: 缩放烘焙验证**

Tube/Ring 改 Scale 后保存 → Scale 烘焙进顶点，预览 Scale 重置为 1。

---

### Task 7: 同步 5 份副本

**Files:**
- Copy: `Editor/VFXMeshGeneratorWindow.cs` → 其余 4 处

- [ ] **Step 1: 用权威源覆盖其他 4 份副本**

```powershell
$src = "F:\ClaudeCode\claude-work\VFX_Tool\Editor\VFXMeshGeneratorWindow.cs"
$dests = @(
  "F:\ClaudeCode\claude-work\VFX_Tool\com.vfxtools.meshgenerator\Editor\VFXMeshGeneratorWindow.cs",
  "F:\ClaudeCode\claude-work\VFX_Tool\Package\com.vfxtools.meshgenerator\Editor\VFXMeshGeneratorWindow.cs",
  "F:\ClaudeCode\claude-work\VFX_Tool\VFX_MeshTool\com.vfxtools.meshgenerator\Editor\VFXMeshGeneratorWindow.cs",
  "F:\ClaudeCode\claude-work\VFX_Tool\VFXMeshGenerator.cs"
)
foreach ($d in $dests) { Copy-Item -LiteralPath $src -Destination $d -Force }
```

- [ ] **Step 2: 校验 5 份 MD5 一致**

```powershell
$files = @(
  "F:\ClaudeCode\claude-work\VFX_Tool\Editor\VFXMeshGeneratorWindow.cs",
  "F:\ClaudeCode\claude-work\VFX_Tool\com.vfxtools.meshgenerator\Editor\VFXMeshGeneratorWindow.cs",
  "F:\ClaudeCode\claude-work\VFX_Tool\Package\com.vfxtools.meshgenerator\Editor\VFXMeshGeneratorWindow.cs",
  "F:\ClaudeCode\claude-work\VFX_Tool\VFX_MeshTool\com.vfxtools.meshgenerator\Editor\VFXMeshGeneratorWindow.cs",
  "F:\ClaudeCode\claude-work\VFX_Tool\VFXMeshGenerator.cs"
)
$files | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm MD5).Hash }
```
预期：5 行哈希全相同。

- [ ] **Step 3: 更新 CHANGELOG 与提交**

`com.vfxtools.meshgenerator/CHANGELOG.md` 加一条 `## [1.1.0]` 记录 Spline Tube + Ring 圆形 UV，`package.json` 版本改 1.1.0。
```bash
git add Editor/ com.vfxtools.meshgenerator/ Package/ VFX_MeshTool/ VFXMeshGenerator.cs
git commit -m "chore: sync all copies, bump to 1.1.0 (Spline Tube + Ring circular UV)"
```

---

## 验证摘要

| 验证项 | Task |
|--------|------|
| Spline Strip/Cross 无回归 | 3.4 |
| Spline Tube 几何/参数 | 3.5 |
| Ring 圆形 UV（实心/圆环/强制模式） | 4.3 |
| Ring 矩形模式无回归 | 4.4 |
| 重置参数 | 5.4 |
| FBX 保存/导出/缩放烘焙 | 6 |
| 5 份副本一致 | 7.2 |
