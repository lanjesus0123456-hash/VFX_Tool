# 第二套 UV (UV2) 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans。单文件 Unity Editor 工具，无自动化测试，引擎内手动验证。

**Goal:** 给面片加可选的第二套 UV（UV2），基于原始 0~1 展开，带独立 Tiling/Rotate，FBX 导出第二套 UVSet1。默认关闭，不影响现有资产。

**Architecture:** 改 `Editor/VFXMeshGeneratorWindow.cs`。抽出共享 `TransformUV` 方法供 UV1/UV2 复用（DRY）；UV2 在 ApplyUV(UV1) 之前从原始 channel0 拷贝生成，写入 channel1；FBX 导出加 UVSet1。

**验证：** 保存后 Unity 编译无 error CS；引擎内验证 UV2 开关、独立 Tiling/Rotate；导出 FBX 检查双 UV。

---

### Task 1: UV2 字段

- [ ] **Step 1:** 在 `vTiling` 字段后（约 37 行）新增：
```csharp
    private bool     uv2Enabled   = false;
    private float    uv2Rotate    = 0f;
    private float    uv2UTiling   = 1f;
    private float    uv2VTiling   = 1f;
```

---

### Task 2: 抽出共享 TransformUV + 重构 ApplyUV + 写 UV2

- [ ] **Step 1:** 把 `ApplyUV`（约 804-816 行）整体替换为：
```csharp
    void ApplyUV(Mesh mesh)
    {
        // UV2：基于原始 channel0（未被 UV1 Tiling/Rotate 污染）生成，写入 channel1
        if (uv2Enabled)
        {
            var uv2 = TransformUV(mesh.uv, uv2Rotate, uv2UTiling, uv2VTiling);
            mesh.SetUVs(1, new List<Vector2>(uv2));
        }

        // UV1：施加到 channel0
        mesh.uv = TransformUV(mesh.uv, uvRotate, uTiling, vTiling);
    }

    static Vector2[] TransformUV(Vector2[] src, float rotateDeg, float uTile, float vTile)
    {
        float angle = rotateDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        var dst = new Vector2[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            float u = src[i].x * uTile - 0.5f;
            float v = src[i].y * vTile - 0.5f;
            dst[i] = new Vector2(cos * u - sin * v + 0.5f, sin * u + cos * v + 0.5f);
        }
        return dst;
    }
```
（注意：UV2 必须先于 UV1 计算，因为两者都读 `mesh.uv` 的原始值；UV1 计算会覆盖 `mesh.uv`，所以 UV2 在前）

- [ ] **Step 2: 校验** 编译无报错；与原算法一致（UV1 行为不变）。

---

### Task 3: UV2 UI

- [ ] **Step 1:** 在 UV 折叠面板内 `vTiling` 滑条后（约 258 行后）新增：
```csharp
            EditorGUILayout.Space(2);
            uv2Enabled = EditorGUILayout.Toggle("启用 UV2", uv2Enabled);
            if (uv2Enabled)
            {
                uv2Rotate  = EditorGUILayout.Slider("UV2 Rotate", uv2Rotate, 0f, 360f);
                uv2UTiling = EditorGUILayout.Slider("UV2 U Tiling", uv2UTiling, 0.1f, 10f);
                uv2VTiling = EditorGUILayout.Slider("UV2 V Tiling", uv2VTiling, 0.1f, 10f);
            }
```

- [ ] **Step 2: 校验** UV 折叠里出现 UV2 开关，勾选展开三参数。

---

### Task 4: FBX 导出 UVSet1

- [ ] **Step 1:** 在 ExportMeshToFbx 取数处（约 1026 行 `var uvs = mesh.uv;` 后）加：
```csharp
            var uv2 = mesh.uv2;
```

- [ ] **Step 2:** 在 UVSet0 写入块（约 1059 行 `layer.SetUVs(layerUV);` 的 `}` 后）追加 UVSet1：
```csharp
            // UV2 (UVSet1)
            if (uv2 != null && uv2.Length > 0)
            {
                var layerUV2 = FbxLayerElementUV.Create(fbxMesh, "UVSet1");
                layerUV2.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                layerUV2.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                for (int i = 0; i < uv2.Length; i++)
                    layerUV2.GetDirectArray().Add(new FbxVector2(uv2[i].x, uv2[i].y));
                layer.SetUVs(layerUV2, FbxLayerElement.EType.eUV);
            }
```
（说明：第二套 UV 通过 `layer.SetUVs(elem, eType)` 重载挂到同一 layer。若引擎导入只认到一套，Task 6 验证时改用 layer1 方案）

- [ ] **Step 3: 校验** 编译无报错。

---

### Task 5: ResetAllParams

- [ ] **Step 1:** 在 ResetAllParams 的 UV 重置区（`vTiling = 1f;` 后，约 777 行）加：
```csharp
        uv2Enabled = false; uv2Rotate = 0f; uv2UTiling = 1f; uv2VTiling = 1f;
```

- [ ] **Step 2: 校验** 重置后 UV2 回默认关闭。

---

### Task 6: 引擎内验证 + 自审 + 存档

- [ ] **Step 1: 引擎验证**
  - 关 UV2（默认）→ 行为与原生一致，导出 FBX 只有 UVSet0
  - 开 UV2 → 出现三参数；改 UV1 Tiling/Rotate 不影响 UV2；改 UV2 参数不影响 UV1
  - 导出 FBX → 导入后 mesh 有两套 UV（UV0/UV1）

- [ ] **Step 2: 自审** 花括号配平；`TransformUV`/`uv2*` 符号一致；UV1 行为无回归

- [ ] **Step 3: 存档** 版本升 1.3.0；更新 CHANGELOG、VERSIONING.md；创建 `Releases/v1.3.0/com.vfxtools.meshgenerator/`（完整 UPM 包），校验 cs MD5 一致
