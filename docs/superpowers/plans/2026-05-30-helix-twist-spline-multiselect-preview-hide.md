# Helix 头尾扭曲 + Spline 多选/软选 + 预览隐藏 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans。单文件 Unity Editor 工具，无自动化测试，验证为引擎内手动。

**Goal:** 给 Helix 加头尾扭曲，给 Spline 加矩形框选+多点整体移动+距离衰减软选，加预览隐藏开关。

**Architecture:** 全部改 `Editor/VFXMeshGeneratorWindow.cs`。字段 → UI → 几何/交互 → 重置。不碰 FBX 导出、预览材质、其他面片几何。

**验证：** 保存后 Unity 自动编译，Console 无 error CS；`Tools > VFX Mesh Generator` 内手动验证。每完成一功能做花括号配平 + 结构校验。

---

### Task 1: Helix 头尾扭曲

**Files:** Modify `Editor/VFXMeshGeneratorWindow.cs`

- [ ] **Step 1: 字段**（helixCurve 行后，约 83 行）新增：
```csharp
    private float          helixHeadTwist = 0f;
    private float          helixTailTwist = 0f;
```

- [ ] **Step 2: UI**（Helix case `helixCurve` 行后，约 347 行）新增：
```csharp
                helixHeadTwist = EditorGUILayout.Slider("头部扭曲", helixHeadTwist, -360f, 360f);
                helixTailTwist = EditorGUILayout.Slider("尾部扭曲", helixTailTwist, -360f, 360f);
```

- [ ] **Step 3: 几何**（BuildHelix，bitang 安全检查后、主条带生成前，约 1199 行后）插入：
```csharp
                // 头尾扭曲：绕 tangent 旋转 bitang，端点强、中间(t=0.5)为 0
                float wHead = 1f - Mathf.SmoothStep(0f, 0.5f, t);
                float wTail = Mathf.SmoothStep(0.5f, 1f, t);
                float extraTwist = helixHeadTwist * wHead + helixTailTwist * wTail;
                if (Mathf.Abs(extraTwist) > 0.01f)
                    bitang = Quaternion.AngleAxis(extraTwist, tangent) * bitang;
```

- [ ] **Step 4: 重置**（约 789 行 helix 行）追加 `helixHeadTwist = 0f; helixTailTwist = 0f;`

- [ ] **Step 5: 校验** 花括号配平 + Console 无报错；引擎内：头扭 90° 只扭头、中间不扭。

---

### Task 2: Spline 多选字段 + 软选字段

**Files:** Modify `Editor/VFXMeshGeneratorWindow.cs`

- [ ] **Step 1: 字段**（splinePointFlips 行后，约 105 行）新增：
```csharp
    private List<int>   selectedPoints   = new List<int>();
    private bool        splineSoftSelect = false;
    private float       splineSoftRadius = 1.5f;
    private AnimationCurve splineSoftCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    private bool        isBoxSelecting   = false;
    private Vector2     boxSelectStart;
    private bool        hidePreview      = false;
```

- [ ] **Step 2: 校验** 编译无报错（字段新增不破坏现有引用）。

---

### Task 3: Spline 软选 UI

**Files:** Modify `Editor/VFXMeshGeneratorWindow.cs`（Spline case，`splineWidthCurve` 行后，约 354 行附近，在选中点翻转 UI 之前）

- [ ] **Step 1: 加软选 UI**，在 Spline case 的 `splineWidthCurve = ...` 之后、`EditorGUILayout.Space(4);` 之前插入：
```csharp
                splineSoftSelect = EditorGUILayout.Toggle("软选", splineSoftSelect);
                if (splineSoftSelect)
                {
                    splineSoftRadius = EditorGUILayout.Slider("软选半径", splineSoftRadius, 0.1f, 10f);
                    splineSoftCurve  = EditorGUILayout.CurveField("软选衰减", splineSoftCurve);
                }
```

- [ ] **Step 2: 更新 HelpBox 文本**，把现有 `"Scene 视图中拖动绿色控制点编辑 Spline 路径\n按住 Shift+左键 在最近的线段上插入新点\n右键选中点可删除\n选中点后可调整该点翻转角度"` 改为追加一行 `\n空白处拖矩形框选多个控制点`。

- [ ] **Step 3: 校验** 编译无报错；软选开关 UI 出现。

---

### Task 4: DrawSplineHandles 多选 + 框选 + 软选

**Files:** Modify `Editor/VFXMeshGeneratorWindow.cs`（`DrawSplineHandles`，约 499-600 行，整体替换控制点绘制 + 移动逻辑）

- [ ] **Step 1: 替换控制点绘制与移动块**（504-524 行的 for 循环），改为：
```csharp
        // 绘制控制点（多选高亮）
        for (int i = 0; i < splinePoints.Count; i++)
        {
            Vector3 wp = t.TransformPoint(splinePoints[i]);
            float   sz = HandleUtility.GetHandleSize(wp) * 0.12f;
            Handles.color = selectedPoints.Contains(i) ? Color.yellow : Color.green;
            if (Handles.Button(wp, Quaternion.identity, sz, sz * 1.2f, Handles.SphereHandleCap))
            {
                selectedPoints.Clear();
                selectedPoints.Add(i);
                selectedSplinePoint = i;
            }
        }

        // 多点整体移动 Handle（选集质心）
        if (selectedPoints.Count > 0)
        {
            Vector3 centerLocal = Vector3.zero;
            foreach (int idx in selectedPoints) centerLocal += splinePoints[idx];
            centerLocal /= selectedPoints.Count;
            Vector3 centerWorld = t.TransformPoint(centerLocal);

            EditorGUI.BeginChangeCheck();
            Vector3 newCenterWorld = Handles.DoPositionHandle(centerWorld, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 deltaLocal = t.InverseTransformPoint(newCenterWorld) - centerLocal;
                // 选中点全量移动
                foreach (int idx in selectedPoints)
                    splinePoints[idx] += deltaLocal;
                // 软选：未选中且在半径内的点按衰减跟随
                if (splineSoftSelect && deltaLocal.sqrMagnitude > 0f)
                {
                    for (int j = 0; j < splinePoints.Count; j++)
                    {
                        if (selectedPoints.Contains(j)) continue;
                        float dMin = float.MaxValue;
                        foreach (int idx in selectedPoints)
                            dMin = Mathf.Min(dMin, Vector3.Distance(splinePoints[j], splinePoints[idx] - deltaLocal));
                        if (dMin < splineSoftRadius)
                        {
                            float w = Mathf.Clamp01(splineSoftCurve.Evaluate(dMin / splineSoftRadius));
                            splinePoints[j] += deltaLocal * w;
                        }
                    }
                }
                dirty = true;
                Repaint();
            }
        }
```

- [ ] **Step 2: 框选**，在右键菜单块（`if (e.type == EventType.ContextClick)`）之前插入框选处理：
```csharp
        // 矩形框选（空白处左键拖动，非 Shift）
        if (!e.shift && e.button == 0)
        {
            if (e.type == EventType.MouseDown && !PointHovered(t, e.mousePosition))
            {
                isBoxSelecting = true;
                boxSelectStart = e.mousePosition;
            }
            else if (e.type == EventType.MouseDrag && isBoxSelecting)
            {
                Repaint();
            }
            else if (e.type == EventType.MouseUp && isBoxSelecting)
            {
                isBoxSelecting = false;
                Rect box = RectFromPoints(boxSelectStart, e.mousePosition);
                selectedPoints.Clear();
                for (int i = 0; i < splinePoints.Count; i++)
                {
                    Vector2 gp = HandleUtility.WorldToGUIPoint(t.TransformPoint(splinePoints[i]));
                    if (box.Contains(gp)) selectedPoints.Add(i);
                }
                selectedSplinePoint = selectedPoints.Count > 0 ? selectedPoints[selectedPoints.Count - 1] : -1;
                e.Use();
                Repaint();
            }
        }

        // 画框选矩形
        if (isBoxSelecting)
        {
            Handles.BeginGUI();
            Rect box = RectFromPoints(boxSelectStart, Event.current.mousePosition);
            EditorGUI.DrawRect(box, new Color(0.3f, 0.6f, 1f, 0.15f));
            GUI.Box(box, GUIContent.none);
            Handles.EndGUI();
        }
```

- [ ] **Step 3: 加两个辅助方法**（DrawSplineHandles 后、ClosestPointOnSegment 前）：
```csharp
    bool PointHovered(Transform t, Vector2 mouse)
    {
        for (int i = 0; i < splinePoints.Count; i++)
        {
            Vector2 gp = HandleUtility.WorldToGUIPoint(t.TransformPoint(splinePoints[i]));
            if (Vector2.Distance(gp, mouse) < 12f) return true;
        }
        return false;
    }

    static Rect RectFromPoints(Vector2 a, Vector2 b)
    {
        float xMin = Mathf.Min(a.x, b.x), yMin = Mathf.Min(a.y, b.y);
        return new Rect(xMin, yMin, Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }
```

- [ ] **Step 4: 同步删点/插点的 selectedPoints**：
  - Shift 插点块（约 565 行）`selectedSplinePoint = insertIdx;` 后加 `selectedPoints.Clear(); selectedPoints.Add(insertIdx);`
  - 右键删除块（约 592 行）`splinePoints.RemoveAt(idx);` 后加 `selectedPoints.Clear();` 并 `selectedSplinePoint = -1;`（已有）

- [ ] **Step 5: OnSceneGUI 的 hasSelectedPoint 改用多选**（约 439 行）：
```csharp
        bool hasSelectedPoint = (meshType == MeshType.Spline && selectedPoints.Count > 0);
```

- [ ] **Step 6: 校验** 花括号配平 + 编译无报错；引擎内：框选多点变黄、拖质心整体移动、开软选邻近点衰减跟随、单击退回单选。

---

### Task 5: 预览隐藏开关

**Files:** Modify `Editor/VFXMeshGeneratorWindow.cs`

- [ ] **Step 1: UI**（Transform 区 `showWireframe` 行后，约 178 行）新增：
```csharp
            bool prevHide = hidePreview;
            hidePreview = EditorGUILayout.Toggle("隐藏预览", hidePreview);
            if (hidePreview != prevHide && previewMR != null)
            {
                previewMR.enabled = !hidePreview;
                SceneView.RepaintAll();
            }
```

- [ ] **Step 2: RebuildMesh 末尾**（约 681 行 `ApplyPreviewMaterial(useVertColor);` 后）加：
```csharp
        if (previewMR != null) previewMR.enabled = !hidePreview;
```

- [ ] **Step 3: OnSceneGUI 线框受开关控制**（约 435 行）改为：
```csharp
        if (!hidePreview && showWireframe && previewMF != null && previewMF.sharedMesh != null)
            DrawWireframe();
```
（Spline Handles 不受影响，隐藏时仍可编辑路径）

- [ ] **Step 4: 重置**（ResetAllParams）加 `hidePreview = false;`，并在末尾 previewGO 块里加 `if (previewMR != null) previewMR.enabled = true;`

- [ ] **Step 5: 校验** 编译无报错；引擎内：勾隐藏→面片+线框消失，参数仍可调，Spline Handle 仍在；取消勾选恢复。

---

### Task 6: ResetAllParams 汇总补全

**Files:** Modify `Editor/VFXMeshGeneratorWindow.cs`（ResetAllParams）

- [ ] **Step 1:** 确认包含：`helixHeadTwist=0; helixTailTwist=0;`、`selectedPoints.Clear(); splineSoftSelect=false; splineSoftRadius=1.5f; splineSoftCurve=AnimationCurve.EaseInOut(0,1,1,0);`、`hidePreview=false;`、`isBoxSelecting=false;`

- [ ] **Step 2: 校验** 重置后三功能回默认。

---

### Task 7: 自我审查 + 存档

- [ ] **Step 1:** grep 确认无跨方法变量误用、新符号一致
- [ ] **Step 2:** 花括号配平检查
- [ ] **Step 3:** 版本升 1.2.0，更新 CHANGELOG、VERSIONING.md
- [ ] **Step 4:** 同步到 `Releases/v1.2.0/com.vfxtools.meshgenerator/`（完整 UPM 包），校验 MD5
