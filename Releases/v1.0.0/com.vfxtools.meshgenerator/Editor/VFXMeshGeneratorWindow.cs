// ============================================================
//  VFX Mesh Generator — Unity Editor Extension
//  放入项目任意 Editor 文件夹后，菜单 Tools > VFX Mesh Generator
//
//  修复日志 2026-03-31：
//  1. Helix t=1 端点 tangent 为零 → NaN 崩溃（后向差分修复）
//  2. 棋盘格纹理缺少 HideAndDontSave → 内存泄漏
//  3. 边缘透明增加 V 方向渐变支持
//  4. Curve 控制点不足时的安全保护
// ============================================================
using System.Collections.Generic;
using System.IO;
using Autodesk.Fbx;
using UnityEditor;
using UnityEngine;

public class VFXMeshGeneratorWindow : EditorWindow
{
    public enum MeshType { Plane, Ring, Cross, Sphere, Helix, Spline }

    // ── 公共参数 ──────────────────────────────────────────────
    private string   projectName  = "sm";
    private bool     showChecker  = true;
    private bool     edgeTransp   = false;
    private float    transpAdj1   = 0.5f;
    private float    transpAdj2   = 0.5f;
    private bool     heightGradient = false;
    private Color    heightTopColor = Color.white;
    private Color    heightBotColor = Color.black;
    private AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1);
    private bool     vertColorFoldout = false;
    private MeshType meshType     = MeshType.Helix;
    private string   meshIndex    = "00";
    private string   outputPath   = "Assets/VFXMeshes";
    private float    uvRotate     = 0f;
    private float    uTiling      = 1f;
    private float    vTiling      = 1f;
    private bool     uvFoldout    = false;
    private Texture2D uvPreviewTex;
    private bool     flipNormals  = false;
    private Vector3   pivotOffset  = Vector3.zero;

    // ── Plane ─────────────────────────────────────────────────
    private int   planeRows = 10;
    private int   planeCols = 10;
    private float planeW    = 1f;
    private float planeH    = 1f;

    // ── Ring ──────────────────────────────────────────────────
    private int   ringRows   = 2;
    private int   ringCols   = 8;
    private float ringTopR   = 0.5f;
    private float ringBotR   = 1.5f;
    private float ringHeight = 0.5f;
    private float ringArc    = 360f;
    private float ringTwist  = 0f;
    private bool  ringIsCross = false;

    // ── Cross ─────────────────────────────────────────────────
    private float crossW     = 1f;
    private float crossH     = 1f;
    private int   crossCount = 2;

    // ── Sphere ────────────────────────────────────────────────
    private float sphereR    = 1f;
    private int   sphereRows = 13;
    private int   sphereCols = 24;
    public enum SphereStyle { Full, Right, Left, Top, Bottom }
    private SphereStyle sphereStyle = SphereStyle.Right;

    // ── Helix ─────────────────────────────────────────────────
    private int            helixRows     = 20;
    private int            helixCols     = 5;
    private float          helixTopR     = 1f;
    private float          helixBotR     = 0.1f;
    private int            helixLoops    = 1;
    private float          helixH        = 1f;
    private int            helixCount    = 1;
    private bool           helixIsCross  = false;
    private float          helixW        = 0.5f;
    private AnimationCurve helixCurve    = AnimationCurve.Linear(0, 1, 1, 1);



    // ── Spline ────────────────────────────────────────────────
    private int            splineRows        = 2;
    private int            splineCols        = 16;
    private float          splineW           = 0.5f;
    private bool           splineIsCross     = false;
    private float          splineTwist       = 0f;
    private AnimationCurve splineWidthCurve  = AnimationCurve.Linear(0, 1, 1, 1);
    private List<Vector3>  splinePoints      = new List<Vector3>
    {
        new Vector3(-2f, 0f, 0f),
        new Vector3(-0.5f, 1f, 0f),
        new Vector3( 0.5f, -1f, 0f),
        new Vector3( 2f, 0f, 0f)
    };
    private int  selectedSplinePoint = -1;
    private bool splineFoldout       = true;
    private List<float> splinePointFlips = new List<float> { 0f, 0f, 0f, 0f };

    // ── 内部状态 ──────────────────────────────────────────────
    private GameObject   previewGO;
    private MeshFilter   previewMF;
    private MeshRenderer previewMR;
    private Material     checkerMat;
    private Material     vertColorMat;
    private Material     defaultMat;
    private Material     backMat;
    private Texture2D    checkerTex;
    private Vector2      scroll;
    private bool         dirty = true;
    private bool         meshTypeFoldout = true;
    private bool         showWireframe = true;
    private bool         useCustomMaterial = false;
    private Material     customMaterial;

    // ─────────────────────────────────────────────────────────
    [MenuItem("Tools/VFX Mesh Generator %&u")]
    public static void Open() => GetWindow<VFXMeshGeneratorWindow>("VFX Mesh Generator");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EnsurePreview();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        DestroyPreview();
    }

    // ── GUI ───────────────────────────────────────────────────
    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.Space(4);
        DrawSectionHeader("GENERATE");
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("↺ 重置参数")) ResetAllParams();
        if (GUILayout.Button("保存模型"))  SaveMesh();
        if (GUILayout.Button("导出到桌面")) SaveMeshToDesktop();
        EditorGUILayout.EndHorizontal();

        // ── 预览位置 ──
        if (previewGO != null)
        {
            EditorGUILayout.Space(4);
            DrawSectionHeader("TRANSFORM");
            var t = previewGO.transform;
            EditorGUI.BeginChangeCheck();
            t.position = EditorGUILayout.Vector3Field("Position", t.position);
            t.rotation = Quaternion.Euler(EditorGUILayout.Vector3Field("Rotation", t.rotation.eulerAngles));
            t.localScale = EditorGUILayout.Vector3Field("Scale", t.localScale);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            if (GUILayout.Button("归零位置"))
            {
                t.position = Vector3.zero;
                t.rotation = Quaternion.identity;
                t.localScale = Vector3.one;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("选中预览模型"))
            {
                Selection.activeGameObject = previewGO;
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.FrameSelected();
            }

            // ── 线框 & 面数 ──
            showWireframe = EditorGUILayout.Toggle("显示线框", showWireframe);

            // ── 自定义预览材质 ──
            bool prevUseCustom = useCustomMaterial;
            Material prevCustomMat = customMaterial;
            useCustomMaterial = EditorGUILayout.Toggle("自定义预览材质", useCustomMaterial);
            if (useCustomMaterial)
                customMaterial = (Material)EditorGUILayout.ObjectField("预览材质", customMaterial, typeof(Material), false);
            if (useCustomMaterial != prevUseCustom || customMaterial != prevCustomMat)
                ApplyPreviewMaterial();

            if (previewMF != null && previewMF.sharedMesh != null)
            {
                var m = previewMF.sharedMesh;
                EditorGUILayout.HelpBox($"顶点数: {m.vertexCount}   面数: {m.triangles.Length / 3}", MessageType.None);
            }
        }

        EditorGUILayout.Space(6);
        DrawSectionHeader("ASSET PARAMETERS");

        projectName = EditorGUILayout.TextField("模型名称", projectName);

        // ── 以下所有参数变动都自动触发 Rebuild ──
        EditorGUI.BeginChangeCheck();

        vertColorFoldout = EditorGUILayout.Foldout(vertColorFoldout, "顶点色渐变", true, EditorStyles.foldoutHeader);
        if (vertColorFoldout)
        {
            edgeTransp = EditorGUILayout.Toggle("UV渐变", edgeTransp);
            if (edgeTransp)
            {
                transpAdj1   = EditorGUILayout.Slider("U向渐变", transpAdj1,   0, 1);
                transpAdj2   = EditorGUILayout.Slider("V向渐变", transpAdj2,   0, 1);
            }

            heightGradient = EditorGUILayout.Toggle("高度渐变", heightGradient);
            if (heightGradient)
            {
                heightTopColor = EditorGUILayout.ColorField("顶部颜色", heightTopColor);
                heightBotColor = EditorGUILayout.ColorField("底部颜色", heightBotColor);
                heightCurve    = EditorGUILayout.CurveField("渐变曲线", heightCurve);
            }
        }

        // MeshType 切换时额外重置 pivotOffset
        var prevType = meshType;
        meshType = (MeshType)EditorGUILayout.EnumPopup("Mesh Type", meshType);
        if (meshType != prevType) pivotOffset = Vector3.zero;

        outputPath = EditorGUILayout.TextField("输出路径", outputPath);

        EditorGUILayout.Space(4);
        flipNormals  = EditorGUILayout.Toggle("翻转正反面", flipNormals);
        pivotOffset  = EditorGUILayout.Vector3Field("轴点偏移 (Pivot)", pivotOffset);

        EditorGUILayout.Space(4);
        uvFoldout = EditorGUILayout.Foldout(uvFoldout, "UV 设置", true, EditorStyles.foldoutHeader);
        if (uvFoldout)
        {
            uvRotate = EditorGUILayout.Slider("UV Rotate", uvRotate, 0f, 360f);
            uTiling  = EditorGUILayout.Slider("U Tiling",  uTiling,  0.1f, 10f);
            vTiling  = EditorGUILayout.Slider("V Tiling",  vTiling,  0.1f, 10f);

            // 贴图预览：拖入贴图替换棋盘格
            EditorGUI.BeginChangeCheck();
            uvPreviewTex = (Texture2D)EditorGUILayout.ObjectField("预览贴图", uvPreviewTex, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (checkerMat != null)
                {
                    checkerMat.SetTexture("_MainTex", uvPreviewTex != null ? uvPreviewTex : checkerTex);
                    checkerMat.SetFloat("_UsePreviewTex", uvPreviewTex != null ? 1f : 0f);
                    SceneView.RepaintAll();
                }
            }
            if (uvPreviewTex != null)
            {
                Rect texRect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
                texRect.x = (EditorGUIUtility.currentViewWidth - 128) * 0.5f;
                EditorGUI.DrawPreviewTexture(texRect, uvPreviewTex);
                if (GUILayout.Button("清除预览贴图", GUILayout.Width(128)))
                {
                    uvPreviewTex = null;
                    if (checkerMat != null)
                    {
                        checkerMat.SetTexture("_MainTex", checkerTex);
                        checkerMat.SetFloat("_UsePreviewTex", 0f);
                    }
                    SceneView.RepaintAll();
                }
            }
        }

        EditorGUILayout.Space(6);

        DrawMeshTypeParams();
        if (EditorGUI.EndChangeCheck()) dirty = true;

        EditorGUILayout.EndScrollView();

        if (dirty) { RebuildMesh(); dirty = false; }
    }

    void DrawSectionHeader(string title)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        var r = GUILayoutUtility.GetLastRect();
        r.y += EditorGUIUtility.singleLineHeight - 2;
        r.height = 1;
        EditorGUI.DrawRect(r, new Color(0.4f, 0.4f, 0.4f));
        GUILayout.Space(2);
    }

    void DrawMeshTypeParams()
    {
        switch (meshType)
        {
            case MeshType.Plane:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Plane", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                planeRows = EditorGUILayout.IntSlider("纵向面数", planeRows, 1, 100);
                planeCols = EditorGUILayout.IntSlider("横向面数", planeCols, 1, 100);
                planeW    = EditorGUILayout.Slider("宽度",    planeW,    0.01f, 10);
                planeH    = EditorGUILayout.Slider("高度",    planeH,    0.01f, 10);
                break;

            case MeshType.Ring:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Ring", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                ringRows   = EditorGUILayout.IntSlider("纵向面数",  ringRows,   1, 32);
                ringCols   = EditorGUILayout.IntSlider("环向面数", ringCols,  3, 128);
                ringTopR   = EditorGUILayout.Slider("上半径",     ringTopR,   0.01f, 5);
                ringBotR   = EditorGUILayout.Slider("下半径",     ringBotR,   0.01f, 5);
                ringHeight = EditorGUILayout.Slider("高度",       ringHeight, 0f, 5);
                ringArc    = EditorGUILayout.Slider("弧度范围",   ringArc,    1f, 360f);
                ringTwist  = EditorGUILayout.Slider("扭曲",       ringTwist,  -360f, 360f);
                ringIsCross = EditorGUILayout.Toggle("是否为十字片", ringIsCross);
                break;

            case MeshType.Cross:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Cross", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                crossW     = EditorGUILayout.Slider("宽度",     crossW,     0.01f, 10);
                crossH     = EditorGUILayout.Slider("高度",     crossH,     0.01f, 10);
                crossCount = EditorGUILayout.IntSlider("交叉片数", crossCount, 2, 6);
                break;

            case MeshType.Sphere:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Sphere", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                sphereR     = EditorGUILayout.Slider("半径",      sphereR,     0.01f, 10);
                sphereRows  = EditorGUILayout.IntSlider("纬度面数",  sphereRows,  4, 64);
                sphereCols  = EditorGUILayout.IntSlider("经度面数", sphereCols, 4, 64);
                sphereStyle = (SphereStyle)EditorGUILayout.EnumPopup("Select Style", sphereStyle);
                break;

            case MeshType.Helix:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Helix", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                helixRows    = EditorGUILayout.IntSlider("纵向面数",     helixRows,    1, 200);
                helixCols    = EditorGUILayout.IntSlider("横截面数",     helixCols,    1, 20);
                helixTopR    = EditorGUILayout.Slider("上半径",        helixTopR,    0.01f, 5);
                helixBotR    = EditorGUILayout.Slider("下半径",        helixBotR,    0.01f, 5);
                helixLoops   = EditorGUILayout.IntSlider("循环次数",   helixLoops,   1, 10);
                helixH       = EditorGUILayout.Slider("高度",          helixH,       0.01f, 10);
                helixCount   = EditorGUILayout.IntSlider("螺旋线个数", helixCount,   1, 6);
                helixIsCross = EditorGUILayout.Toggle("是否为十字片",   helixIsCross);
                helixW       = EditorGUILayout.Slider("宽度",          helixW,       0.01f, 5);
                helixCurve   = EditorGUILayout.CurveField("细节调整",  helixCurve);
                break;

            case MeshType.Spline:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Spline", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                splineRows       = EditorGUILayout.IntSlider("横截面数",   splineRows,    1, 20);
                splineCols       = EditorGUILayout.IntSlider("路径面数",   splineCols,    4, 256);
                splineW          = EditorGUILayout.Slider("宽度",        splineW,       0.01f, 5);
                splineIsCross    = EditorGUILayout.Toggle("是否为十字片", splineIsCross);
                splineTwist      = EditorGUILayout.Slider("扭曲",        splineTwist,   -360f, 360f);
                splineWidthCurve = EditorGUILayout.CurveField("宽度曲线", splineWidthCurve);
                EditorGUILayout.Space(4);

                // 选中点的翻转控制
                SyncSplineFlips();
                if (selectedSplinePoint >= 0 && selectedSplinePoint < splinePoints.Count)
                {
                    EditorGUI.BeginChangeCheck();
                    splinePointFlips[selectedSplinePoint] = EditorGUILayout.Slider(
                        $"P{selectedSplinePoint} 翻转", splinePointFlips[selectedSplinePoint], -180f, 180f);
                    if (EditorGUI.EndChangeCheck()) dirty = true;
                }

                EditorGUILayout.HelpBox("Scene 视图中拖动绿色控制点编辑 Spline 路径\n按住 Shift+左键 在最近的线段上插入新点\n右键选中点可删除\n选中点后可调整该点翻转角度", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ 添加点"))
                {
                    var last = splinePoints[splinePoints.Count - 1];
                    var dir  = splinePoints.Count > 1
                        ? (last - splinePoints[splinePoints.Count - 2]).normalized
                        : Vector3.right;
                    splinePoints.Add(last + dir * 1f);
                    splinePointFlips.Add(0f);
                    dirty = true;
                }
                GUI.enabled = splinePoints.Count > 2;
                if (GUILayout.Button("- 删除选中点") && selectedSplinePoint >= 0)
                {
                    splinePoints.RemoveAt(selectedSplinePoint);
                    splinePointFlips.RemoveAt(selectedSplinePoint);
                    selectedSplinePoint = -1;
                    dirty = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("重置 Spline"))
                {
                    splinePoints = new List<Vector3>
                    {
                        new Vector3(-2f, 0f, 0f),
                        new Vector3(-0.5f, 1f, 0f),
                        new Vector3( 0.5f, -1f, 0f),
                        new Vector3( 2f, 0f, 0f)
                    };
                    splinePointFlips = new List<float> { 0f, 0f, 0f, 0f };
                    selectedSplinePoint = -1;
                    dirty = true;
                }
                break;
        }
    }

    // ── Scene GUI ─────────────────────────────────────────────
    void OnSceneGUI(SceneView sv)
    {
        if (previewGO == null) return;

        // Scene 视图左上角悬浮按钮：快速选中预览模型
        Handles.BeginGUI();
        var btnRect = new Rect(10, 10, 140, 24);
        if (GUI.Button(btnRect, "● 选中预览模型"))
        {
            Selection.activeGameObject = previewGO;
            sv.FrameSelected();
        }
        Handles.EndGUI();

        // 线框显示
        if (showWireframe && previewMF != null && previewMF.sharedMesh != null)
            DrawWireframe();

        // Spline 模式下，未选中控制点时才画 Transform Handle，避免冲突
        bool hasSelectedPoint = (meshType == MeshType.Spline && selectedSplinePoint >= 0);
        if (!hasSelectedPoint)
            DrawPreviewTransformHandle();

        if (meshType == MeshType.Spline)
            DrawSplineHandles();
    }

    void DrawWireframe()
    {
        var mesh = previewMF.sharedMesh;
        var t = previewGO.transform;
        var verts = mesh.vertices;
        var tris = mesh.triangles;

        Handles.color = new Color(0f, 1f, 1f, 0.35f);
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = t.TransformPoint(verts[tris[i]]);
            Vector3 b = t.TransformPoint(verts[tris[i + 1]]);
            Vector3 c = t.TransformPoint(verts[tris[i + 2]]);
            Handles.DrawLine(a, b);
            Handles.DrawLine(b, c);
            Handles.DrawLine(c, a);
        }
    }

    void DrawPreviewTransformHandle()
    {
        var t = previewGO.transform;
        EditorGUI.BeginChangeCheck();
        if (Tools.current == Tool.Rotate)
        {
            Quaternion newRot = Handles.DoRotationHandle(t.rotation, t.position);
            if (EditorGUI.EndChangeCheck())
            {
                t.rotation = newRot;
                Repaint();
            }
        }
        else if (Tools.current == Tool.Scale)
        {
            Vector3 newScale = Handles.DoScaleHandle(t.localScale, t.position, t.rotation, HandleUtility.GetHandleSize(t.position));
            if (EditorGUI.EndChangeCheck())
            {
                t.localScale = newScale;
                Repaint();
            }
        }
        else
        {
            Vector3 newPos = Handles.DoPositionHandle(t.position, t.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                t.position = newPos;
                Repaint();
            }
        }
    }

    void DrawSplineHandles()
    {
        var t = previewGO.transform;

        // 绘制控制点
        for (int i = 0; i < splinePoints.Count; i++)
        {
            Vector3 wp = t.TransformPoint(splinePoints[i]);
            float   sz = HandleUtility.GetHandleSize(wp) * 0.12f;

            Handles.color = (i == selectedSplinePoint) ? Color.yellow : Color.green;
            if (Handles.Button(wp, Quaternion.identity, sz, sz * 1.2f, Handles.SphereHandleCap))
                selectedSplinePoint = i;

            if (i == selectedSplinePoint)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 nwp = Handles.DoPositionHandle(wp, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    splinePoints[i] = t.InverseTransformPoint(nwp);
                    dirty = true;
                    Repaint();
                }
            }
        }

        // 绘制样条曲线（用 Catmull-Rom 采样）
        Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
        int sampleCount = splinePoints.Count * 10;
        Vector3 prev = t.TransformPoint(CatmullRom(splinePoints, 0));
        for (int s = 1; s <= sampleCount; s++)
        {
            float st = (float)s / sampleCount * (splinePoints.Count - 1);
            Vector3 cur = t.TransformPoint(CatmullRom(splinePoints, st));
            Handles.DrawLine(prev, cur);
            prev = cur;
        }

        // Shift+左键 在最近线段上插入点
        Event e = Event.current;
        if (e.shift && e.type == EventType.MouseDown && e.button == 0)
        {
            Vector2 mousePos = e.mousePosition;
            float bestDist = float.MaxValue;
            int insertIdx = -1;
            Vector3 insertPos = Vector3.zero;

            for (int i = 0; i < splinePoints.Count - 1; i++)
            {
                Vector3 a = t.TransformPoint(splinePoints[i]);
                Vector3 b = t.TransformPoint(splinePoints[i + 1]);
                Vector2 sa = HandleUtility.WorldToGUIPoint(a);
                Vector2 sb = HandleUtility.WorldToGUIPoint(b);
                Vector2 closest = ClosestPointOnSegment(mousePos, sa, sb);
                float d = Vector2.Distance(mousePos, closest);
                if (d < bestDist && d < 30f)
                {
                    bestDist  = d;
                    insertIdx = i + 1;
                    insertPos = (splinePoints[i] + splinePoints[i + 1]) * 0.5f;
                }
            }

            if (insertIdx >= 0)
            {
                splinePoints.Insert(insertIdx, insertPos);
                splinePointFlips.Insert(insertIdx, 0f);
                selectedSplinePoint = insertIdx;
                dirty = true;
                Repaint();
                e.Use();
            }
        }

        // 右键菜单
        if (e.type == EventType.ContextClick)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("添加控制点（尾部）"), false, () =>
            {
                var last = splinePoints[splinePoints.Count - 1];
                var dir  = splinePoints.Count > 1
                    ? (last - splinePoints[splinePoints.Count - 2]).normalized
                    : Vector3.right;
                splinePoints.Add(last + dir * 1f);
                dirty = true; Repaint();
            });
            if (selectedSplinePoint >= 0 && splinePoints.Count > 2)
            {
                int idx = selectedSplinePoint;
                menu.AddItem(new GUIContent("删除选中点"), false, () =>
                {
                    splinePoints.RemoveAt(idx);
                    selectedSplinePoint = -1;
                    dirty = true; Repaint();
                });
            }
            menu.ShowAsContext();
            e.Use();
        }
    }

    static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return a + ab * t;
    }

    // ── Preview ───────────────────────────────────────────────
    void EnsurePreview()
    {
        if (previewGO != null) return;

        previewGO = new GameObject("_VFXMeshPreview")
            { hideFlags = HideFlags.HideAndDontSave };
        previewMF = previewGO.AddComponent<MeshFilter>();
        previewMR = previewGO.AddComponent<MeshRenderer>();

        defaultMat = new Material(CreateDefaultShader())
            { hideFlags = HideFlags.HideAndDontSave };

        // 棋盘格：用双面 Shader，正面棋盘格，背面纯色
        checkerTex = CreateCheckerTexture();
        checkerTex.hideFlags = HideFlags.HideAndDontSave;   // [FIX] 防止纹理泄漏

        checkerMat = new Material(CreateTwoSideShader())
            { hideFlags = HideFlags.HideAndDontSave };
        checkerMat.SetTexture("_MainTex", checkerTex);

        // 顶点色材质：内置 Shader 保证兼容性
        vertColorMat = new Material(CreateVertColorShader())
            { hideFlags = HideFlags.HideAndDontSave };

        previewMR.sharedMaterial = showChecker ? checkerMat : defaultMat;
    }

    void DestroyPreview()
    {
        if (previewGO    != null) DestroyImmediate(previewGO);
        if (checkerMat   != null) DestroyImmediate(checkerMat);
        if (checkerTex   != null) DestroyImmediate(checkerTex);   // [FIX] 销毁棋盘纹理
        if (vertColorMat != null) DestroyImmediate(vertColorMat);
        if (defaultMat   != null) DestroyImmediate(defaultMat);
    }

    // ── 重建与保存 ────────────────────────────────────────────
    void RebuildMesh()
    {
        EnsurePreview();

        Mesh mesh;
        switch (meshType)
        {
            case MeshType.Plane:  mesh = BuildPlane();  break;
            case MeshType.Ring:   mesh = BuildRing();   break;
            case MeshType.Cross:  mesh = BuildCross();  break;
            case MeshType.Sphere: mesh = BuildSphere(); break;
            case MeshType.Helix:  mesh = BuildHelix();  break;
            case MeshType.Spline: mesh = BuildSpline(); break;
            default:              mesh = new Mesh();    break;
        }

        ApplyUV(mesh);

        // 顶点色：边缘渐变 + 高度渐变
        bool useVertColor = edgeTransp || heightGradient;
        if (useVertColor) ApplyVertexColors(mesh);

        // 正反面翻转：除 Plane/Spline 外默认需要翻转，flipNormals 再次切换
        bool needFlip = (meshType != MeshType.Plane && meshType != MeshType.Spline);
        if (flipNormals) needFlip = !needFlip;
        if (needFlip) FlipTriangles(mesh);

        // 轴点偏移
        ApplyPivot(mesh);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        previewMF.sharedMesh = mesh;

        ApplyPreviewMaterial(useVertColor);
        SceneView.RepaintAll();
    }

    void ApplyUV(Mesh mesh)
    {
        var uvs = mesh.uv;
        float angle = uvRotate * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        for (int i = 0; i < uvs.Length; i++)
        {
            float u = uvs[i].x * uTiling - 0.5f;
            float v = uvs[i].y * vTiling - 0.5f;
            uvs[i] = new Vector2(cos * u - sin * v + 0.5f, sin * u + cos * v + 0.5f);
        }
        mesh.uv = uvs;
    }

    void ApplyVertexColors(Mesh mesh)
    {
        var verts  = mesh.vertices;
        var colors = new Color[mesh.vertexCount];

        // 初始化为白色
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.white;

        // 边缘渐变 (UV基准，用原始0~1范围的UV)
        if (edgeTransp)
        {
            // 用 mesh 的原始 UV 计算渐变，但需要反推回 Tiling/Rotate 前的值
            // 更简单的方法：重新从 mesh 生成器拿原始 UV
            // 这里用一个简化方案：将 UV clamp 到 0~1 后计算
            var uvs = mesh.uv;
            for (int i = 0; i < uvs.Length; i++)
            {
                float u = Mathf.Repeat(uvs[i].x, 1f);
                float v = Mathf.Repeat(uvs[i].y, 1f);

                float fadeU = 1f;
                float fadeV = 1f;

                if (transpAdj1 > 0.01f)
                {
                    float edgeU = Mathf.Min(u, 1f - u);  // 到最近边缘的距离 (0~0.5)
                    fadeU = Mathf.Clamp01(edgeU / transpAdj1);
                }
                if (transpAdj2 > 0.01f)
                {
                    float edgeV = Mathf.Min(v, 1f - v);
                    fadeV = Mathf.Clamp01(edgeV / transpAdj2);
                }

                float fade = fadeU * fadeV;
                colors[i].a = fade;
            }
        }

        // 高度渐变
        if (heightGradient)
        {
            // 计算 mesh 的 Y 范围
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i].y < minY) minY = verts[i].y;
                if (verts[i].y > maxY) maxY = verts[i].y;
            }
            float rangeY = maxY - minY;
            if (rangeY < 0.0001f) rangeY = 1f;

            for (int i = 0; i < verts.Length; i++)
            {
                float t = (verts[i].y - minY) / rangeY;
                t = heightCurve.Evaluate(t);
                Color hc = Color.Lerp(heightBotColor, heightTopColor, t);
                // 与边缘渐变的 alpha 相乘融合
                colors[i] = new Color(hc.r, hc.g, hc.b, colors[i].a * hc.a);
            }
        }

        mesh.colors = colors;
    }

    void ResetAllParams()
    {
        edgeTransp   = false;
        transpAdj1   = 0.5f;
        transpAdj2   = 0.5f;
        heightGradient = false;
        heightTopColor = Color.white;
        heightBotColor = Color.black;
        heightCurve  = AnimationCurve.Linear(0, 0, 1, 1);
        meshType     = MeshType.Helix;
        uvRotate     = 0f;
        uTiling      = 1f;
        vTiling      = 1f;
        flipNormals  = false;
        pivotOffset  = Vector3.zero;

        planeRows = 10; planeCols = 10; planeW = 1f; planeH = 1f;
        ringRows = 2; ringCols = 8; ringTopR = 0.5f; ringBotR = 1.5f;
        ringHeight = 0.5f; ringArc = 360f; ringTwist = 0f; ringIsCross = false;
        crossW = 1f; crossH = 1f; crossCount = 2;
        sphereR = 1f; sphereRows = 13; sphereCols = 24; sphereStyle = SphereStyle.Right;
        helixRows = 20; helixCols = 5; helixTopR = 1f; helixBotR = 0.1f;
        helixLoops = 1; helixH = 1f; helixCount = 1; helixIsCross = false;
        helixW = 0.5f; helixCurve = AnimationCurve.Linear(0, 1, 1, 1);
        splineRows = 2; splineCols = 16; splineW = 0.5f; splineIsCross = false;
        splineTwist = 0f; splineWidthCurve = AnimationCurve.Linear(0, 1, 1, 1);
        splinePoints = new List<Vector3> {
            new Vector3(-2f, 0f, 0f), new Vector3(-0.5f, 1f, 0f),
            new Vector3(0.5f, -1f, 0f), new Vector3(2f, 0f, 0f)
        };
        splinePointFlips = new List<float> { 0f, 0f, 0f, 0f };
        selectedSplinePoint = -1;

        if (previewGO != null)
        {
            previewGO.transform.position = Vector3.zero;
            previewGO.transform.rotation = Quaternion.identity;
            previewGO.transform.localScale = Vector3.one;
        }
        dirty = true;
    }

    void SaveMesh()
    {
        if (previewMF == null || previewMF.sharedMesh == null) return;

        string dir = outputPath.StartsWith("Assets") ? outputPath : ("Assets/" + outputPath.TrimStart('/'));
        string absDir = Application.dataPath + dir.Substring(6);
        Directory.CreateDirectory(absDir);

        // 命名规则：基础名 = projectName_MeshType，重复时从 _01 开始递增
        string baseName = $"{projectName}_{meshType}";
        string fileName  = $"{baseName}.fbx";
        string fullPath  = Path.Combine(absDir, fileName);
        if (File.Exists(fullPath))
        {
            int suffix = 1;
            do
            {
                fileName = $"{baseName}_{suffix:D2}.fbx";
                fullPath = Path.Combine(absDir, fileName);
                suffix++;
            } while (File.Exists(fullPath));
        }

        var copy = Instantiate(previewMF.sharedMesh);
        copy.name = Path.GetFileNameWithoutExtension(fileName);

        // 缩放烘焙到顶点
        Vector3 s = previewGO.transform.localScale;
        ApplyScale(copy, s);
        previewGO.transform.localScale = Vector3.one;

        ExportMeshToFbx(copy, fullPath);
        DestroyImmediate(copy);

        AssetDatabase.Refresh();
        string assetPath = $"{dir}/{fileName}";
        Debug.Log($"[VFX Mesh Generator] 已保存：{assetPath}");

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
    }

    void SaveMeshToDesktop()
    {
        if (previewMF == null || previewMF.sharedMesh == null) return;

        string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
        string baseName = $"{projectName}_{meshType}";
        string fileName = $"{baseName}.fbx";
        string filePath = Path.Combine(desktop, fileName);

        // 同名自动加后缀，从 _01 开始
        if (File.Exists(filePath))
        {
            int counter = 1;
            do
            {
                fileName = $"{baseName}_{counter:D2}.fbx";
                filePath = Path.Combine(desktop, fileName);
                counter++;
            } while (File.Exists(filePath));
        }

        var mesh = Instantiate(previewMF.sharedMesh);

        // 缩放烘焙到顶点
        Vector3 s = previewGO.transform.localScale;
        ApplyScale(mesh, s);
        previewGO.transform.localScale = Vector3.one;

        ExportMeshToFbx(mesh, filePath);
        DestroyImmediate(mesh);

        Debug.Log($"[VFX Mesh Generator] 已导出到桌面：{filePath}");
    }

    void ExportMeshToFbx(Mesh mesh, string path)
    {
        using (var fbxManager = FbxManager.Create())
        {
            var fbxScene = FbxScene.Create(fbxManager, "Scene");

            // 设置单位为米，避免 Unity 导入时 1cm→0.01m 缩放
            var globalSettings = fbxScene.GetGlobalSettings();
            globalSettings.SetSystemUnit(FbxSystemUnit.m);

            var fbxNode = FbxNode.Create(fbxScene, mesh.name);
            var fbxMesh = FbxMesh.Create(fbxScene, mesh.name + "_mesh");

            var verts = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var tris = mesh.triangles;
            var colors = mesh.colors;

            // 顶点（翻转X轴：Unity左手 → FBX右手坐标系）
            fbxMesh.InitControlPoints(verts.Length);
            for (int i = 0; i < verts.Length; i++)
                fbxMesh.SetControlPointAt(new FbxVector4(-verts[i].x, verts[i].y, verts[i].z, 0), i);

            // 创建 Layer 0
            fbxMesh.CreateLayer();
            var layer = fbxMesh.GetLayer(0);

            // 法线（同样翻转X）
            if (normals != null && normals.Length > 0)
            {
                var layerNormal = FbxLayerElementNormal.Create(fbxMesh, "Normals");
                layerNormal.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                layerNormal.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                for (int i = 0; i < normals.Length; i++)
                    layerNormal.GetDirectArray().Add(new FbxVector4(-normals[i].x, normals[i].y, normals[i].z, 0));
                layer.SetNormals(layerNormal);
            }

            // UV
            if (uvs != null && uvs.Length > 0)
            {
                var layerUV = FbxLayerElementUV.Create(fbxMesh, "UVSet0");
                layerUV.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                layerUV.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                for (int i = 0; i < uvs.Length; i++)
                    layerUV.GetDirectArray().Add(new FbxVector2(uvs[i].x, uvs[i].y));
                layer.SetUVs(layerUV);
            }

            // 顶点色
            if (colors != null && colors.Length > 0)
            {
                var layerColor = FbxLayerElementVertexColor.Create(fbxMesh, "Colors");
                layerColor.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                layerColor.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                for (int i = 0; i < colors.Length; i++)
                    layerColor.GetDirectArray().Add(new FbxColor(colors[i].r, colors[i].g, colors[i].b, colors[i].a));
                layer.SetVertexColors(layerColor);
            }

            // 三角面（翻转绕序补偿X轴镜像）
            for (int i = 0; i < tris.Length; i += 3)
            {
                fbxMesh.BeginPolygon();
                fbxMesh.AddPolygon(tris[i]);
                fbxMesh.AddPolygon(tris[i + 2]);
                fbxMesh.AddPolygon(tris[i + 1]);
                fbxMesh.EndPolygon();
            }

            fbxNode.SetNodeAttribute(fbxMesh);
            fbxScene.GetRootNode().AddChild(fbxNode);

            // 导出为二进制 FBX
            var fbxExporter = FbxExporter.Create(fbxManager, "Exporter");
            int fileFormat = fbxManager.GetIOPluginRegistry().FindWriterIDByDescription("FBX binary (*.fbx)");
            fbxExporter.Initialize(path, fileFormat, fbxManager.GetIOSettings());
            fbxExporter.Export(fbxScene);
            fbxExporter.Destroy();
        }
    }

    // ════════════════════════════════════════════════════════
    //  Mesh 生成器
    // ════════════════════════════════════════════════════════

    Mesh BuildPlane()
    {
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        for (int r = 0; r <= planeRows; r++)
        for (int c = 0; c <= planeCols; c++)
        {
            float u = (float)c / planeCols;
            float v = (float)r / planeRows;
            verts.Add(new Vector3((u - 0.5f) * planeW, 0, (v - 0.5f) * planeH));
            uvs.Add(new Vector2(u, v));
        }
        QuadTris(tris, planeRows, planeCols);
        return BuildMesh("Plane", verts, uvs, tris);
    }

    Mesh BuildRing()
    {
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        // Pass 0: 普通环面
        for (int r = 0; r <= ringRows; r++)
        {
            float t      = (float)r / ringRows;
            float radius = Mathf.Lerp(ringTopR, ringBotR, t);
            float y      = (1f - t) * ringHeight;
            float twist  = t * ringTwist * Mathf.Deg2Rad;

            for (int c = 0; c <= ringCols; c++)
            {
                float ct    = (float)c / ringCols;
                float angle = ct * ringArc * Mathf.Deg2Rad + twist;
                verts.Add(new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
                uvs.Add(new Vector2(ct, t));
            }
        }
        QuadTris(tris, ringRows, ringCols);

        // Pass 1: 十字片 — 每个截面的条带旋转90°（绕切线方向），与环面垂直
        if (ringIsCross)
        {
            int baseIdx = verts.Count;

            for (int r = 0; r <= ringRows; r++)
            {
                float t = (float)r / ringRows;

                for (int c = 0; c <= ringCols; c++)
                {
                    float ct    = (float)c / ringCols;
                    float twist = t * ringTwist * Mathf.Deg2Rad;
                    float angle = ct * ringArc * Mathf.Deg2Rad + twist;

                    // 该列的顶点和底点（含扭曲）
                    float topAngle = ct * ringArc * Mathf.Deg2Rad;
                    float botAngle = ct * ringArc * Mathf.Deg2Rad + ringTwist * Mathf.Deg2Rad;
                    Vector3 topPt = new Vector3(Mathf.Cos(topAngle) * ringTopR, ringHeight, Mathf.Sin(topAngle) * ringTopR);
                    Vector3 botPt = new Vector3(Mathf.Cos(botAngle) * ringBotR, 0,          Mathf.Sin(botAngle) * ringBotR);

                    Vector3 center  = (topPt + botPt) * 0.5f;
                    Vector3 stripV  = topPt - botPt;
                    float   halfLen = stripV.magnitude * 0.5f;
                    Vector3 stripDir = stripV.normalized;

                    // 切线方向（沿圆周）
                    Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle));

                    // 十字方向 = 条带绕切线旋转90°
                    Vector3 crossDir = Vector3.Cross(tangent, stripDir).normalized;
                    if (crossDir.sqrMagnitude < 0.001f)
                        crossDir = Vector3.Cross(tangent, Vector3.up).normalized;

                    float offset = (t * 2f - 1f) * halfLen;
                    verts.Add(center + crossDir * offset);
                    uvs.Add(new Vector2(ct, t));
                }
            }

            for (int r = 0; r < ringRows; r++)
            for (int c = 0; c < ringCols; c++)
            {
                int i = baseIdx + r * (ringCols + 1) + c;
                int j = i + ringCols + 1;
                tris.Add(i); tris.Add(j); tris.Add(i + 1);
                tris.Add(i + 1); tris.Add(j); tris.Add(j + 1);
            }
        }

        return BuildMesh("Ring", verts, uvs, tris);
    }

    Mesh BuildCross()
    {
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        float step = Mathf.PI / crossCount;
        for (int i = 0; i < crossCount; i++)
        {
            float a     = i * step;
            var   right = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * crossW * 0.5f;
            var   up    = Vector3.up * crossH * 0.5f;
            int   b     = verts.Count;

            verts.Add(-right - up); verts.Add(right - up);
            verts.Add(-right + up); verts.Add(right + up);
            uvs.Add(new Vector2(0,0)); uvs.Add(new Vector2(1,0));
            uvs.Add(new Vector2(0,1)); uvs.Add(new Vector2(1,1));

            // 双面（预览材质是 Cull Back，需要正反面三角形）
            tris.Add(b); tris.Add(b+2); tris.Add(b+1);
            tris.Add(b+1); tris.Add(b+2); tris.Add(b+3);
            tris.Add(b+1); tris.Add(b+2); tris.Add(b);
            tris.Add(b+3); tris.Add(b+2); tris.Add(b+1);
        }
        return BuildMesh("Cross", verts, uvs, tris);
    }

    Mesh BuildSphere()
    {
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        // Select Style 控制生成范围
        float phiStart = 0f, phiEnd = Mathf.PI;
        float thetaStart = 0f, thetaEnd = 2f * Mathf.PI;
        switch (sphereStyle)
        {
            case SphereStyle.Top:    phiEnd   = Mathf.PI * 0.5f; break;
            case SphereStyle.Bottom: phiStart = Mathf.PI * 0.5f; break;
            case SphereStyle.Right:  thetaEnd = Mathf.PI;        break;
            case SphereStyle.Left:   thetaStart = Mathf.PI; thetaEnd = 2f * Mathf.PI; break;
        }

        for (int r = 0; r <= sphereRows; r++)
        {
            float phi = Mathf.Lerp(phiStart, phiEnd, (float)r / sphereRows);
            for (int c = 0; c <= sphereCols; c++)
            {
                float theta = Mathf.Lerp(thetaStart, thetaEnd, (float)c / sphereCols);
                verts.Add(new Vector3(
                    sphereR * Mathf.Sin(phi) * Mathf.Cos(theta),
                    sphereR * Mathf.Cos(phi),
                    sphereR * Mathf.Sin(phi) * Mathf.Sin(theta)));
                uvs.Add(new Vector2((float)c / sphereCols, 1f - (float)r / sphereRows));
            }
        }
        QuadTris(tris, sphereRows, sphereCols);
        return BuildMesh("Sphere", verts, uvs, tris);
    }

    Mesh BuildHelix()
    {
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        float angleOffset = 2 * Mathf.PI / helixCount;
        int   stride      = helixIsCross ? (helixCols + 1) * 2 : (helixCols + 1);

        for (int h = 0; h < helixCount; h++)
        {
            int baseIdx = verts.Count;

            for (int r = 0; r <= helixRows; r++)
            {
                float t      = (float)r / helixRows;
                float angle  = t * helixLoops * 2 * Mathf.PI + h * angleOffset;
                float radius = Mathf.Lerp(helixBotR, helixTopR, t);
                float y      = t * helixH;
                float width  = helixW * helixCurve.Evaluate(t) * 0.5f;

                var center = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

                // [FIX] tangent 计算 — 前向差分 + 端点后向差分，避免 t=1 时 NaN
                Vector3 tangent;
                if (t < 1f)
                {
                    float dt    = 0.01f;
                    float tNext = t + dt;
                    float aNext = tNext * helixLoops * 2 * Mathf.PI + h * angleOffset;
                    float rNext = Mathf.Lerp(helixBotR, helixTopR, tNext);
                    var   pNext = new Vector3(Mathf.Cos(aNext) * rNext, tNext * helixH, Mathf.Sin(aNext) * rNext);
                    tangent = (pNext - center).normalized;
                }
                else
                {
                    // 端点用后向差分
                    float dt    = 0.01f;
                    float tPrev = t - dt;
                    float aPrev = tPrev * helixLoops * 2 * Mathf.PI + h * angleOffset;
                    float rPrev = Mathf.Lerp(helixBotR, helixTopR, tPrev);
                    var   pPrev = new Vector3(Mathf.Cos(aPrev) * rPrev, tPrev * helixH, Mathf.Sin(aPrev) * rPrev);
                    tangent = (center - pPrev).normalized;
                }

                // 安全检查：万一 tangent 仍为零
                if (tangent.sqrMagnitude < 0.001f)
                    tangent = Vector3.up;

                var outDir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var bitang = Vector3.Cross(tangent, outDir).normalized * width;

                // 安全检查：bitangent 退化
                if (bitang.sqrMagnitude < 0.0001f)
                    bitang = Vector3.Cross(tangent, Vector3.up).normalized * width;

                // 主条带
                for (int c = 0; c <= helixCols; c++)
                {
                    float ct = (float)c / helixCols;
                    verts.Add(center + bitang * (ct * 2 - 1));
                    uvs.Add(new Vector2(ct, t));
                }

                // 十字片
                if (helixIsCross)
                {
                    var crossBit = Vector3.Cross(tangent, bitang.normalized).normalized * width;
                    for (int c = 0; c <= helixCols; c++)
                    {
                        float ct = (float)c / helixCols;
                        verts.Add(center + crossBit * (ct * 2 - 1));
                        uvs.Add(new Vector2(ct, t));
                    }
                }
            }

            int passes = helixIsCross ? 2 : 1;
            for (int p = 0; p < passes; p++)
            for (int r = 0; r < helixRows; r++)
            {
                int off = baseIdx + r * stride + p * (helixCols + 1);
                for (int c = 0; c < helixCols; c++)
                {
                    int i = off + c;
                    tris.Add(i); tris.Add(i + stride); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + stride); tris.Add(i + stride + 1);
                }
            }
        }

        return BuildMesh("Helix", verts, uvs, tris);
    }

    Mesh BuildSpline()
    {
        if (splinePoints.Count < 2) return new Mesh { name = "Spline" };

        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        // Catmull-Rom 沿样条采样
        var samples = new List<Vector3>();
        for (int s = 0; s <= splineCols; s++)
        {
            float st = (float)s / splineCols * (splinePoints.Count - 1);
            samples.Add(CatmullRom(splinePoints, st));
        }

        int rowVerts = splineRows + 1;
        int passes   = splineIsCross ? 2 : 1;

        // RMF (Rotation Minimizing Frame) — 比简单 Cross(tangent, up) 更稳定
        var tangents = new Vector3[samples.Count];
        var rights   = new Vector3[samples.Count];

        // 计算 tangent
        for (int i = 0; i < samples.Count; i++)
        {
            if      (i == 0)                  tangents[i] = (samples[1] - samples[0]).normalized;
            else if (i == samples.Count - 1)  tangents[i] = (samples[i] - samples[i - 1]).normalized;
            else                              tangents[i] = (samples[i + 1] - samples[i - 1]).normalized;
        }

        // 初始 right: 找到与第一个 tangent 不平行的参考轴
        Vector3 initRight = Vector3.Cross(tangents[0], Vector3.up);
        if (initRight.sqrMagnitude < 0.001f)
            initRight = Vector3.Cross(tangents[0], Vector3.forward);
        rights[0] = initRight.normalized;

        // RMF 传播
        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 v1 = samples[i] - samples[i - 1];
            float c1 = Vector3.Dot(v1, v1);
            if (c1 < 0.0001f) { rights[i] = rights[i - 1]; continue; }
            Vector3 rL = rights[i - 1] - (2f / c1) * Vector3.Dot(v1, rights[i - 1]) * v1;
            Vector3 tL = tangents[i - 1] - (2f / c1) * Vector3.Dot(v1, tangents[i - 1]) * v1;
            Vector3 v2 = tangents[i] - tL;
            float c2 = Vector3.Dot(v2, v2);
            if (c2 < 0.0001f) { rights[i] = rL; continue; }
            rights[i] = (rL - (2f / c2) * Vector3.Dot(v2, rL) * v2).normalized;
        }

        for (int p = 0; p < passes; p++)
        {
            int baseIdx = verts.Count;

            for (int i = 0; i < samples.Count; i++)
            {
                float t = (float)i / Mathf.Max(samples.Count - 1, 1);
                float w = splineW * splineWidthCurve.Evaluate(t) * 0.5f;

                Vector3 right = rights[i];

                // 全局扭曲：绕 tangent 旋转
                float totalTwist = splineTwist * t;

                // 每点翻转：插值控制点的 flip 角度
                SyncSplineFlips();
                float flipAngle = 0f;
                {
                    float st = t * (splinePoints.Count - 1);
                    int fi = Mathf.FloorToInt(st);
                    float ff = st - fi;
                    fi = Mathf.Clamp(fi, 0, splinePointFlips.Count - 1);
                    int fi2 = Mathf.Min(fi + 1, splinePointFlips.Count - 1);
                    flipAngle = Mathf.Lerp(splinePointFlips[fi], splinePointFlips[fi2], ff);
                }
                totalTwist += flipAngle;

                if (Mathf.Abs(totalTwist) > 0.01f)
                {
                    Quaternion twistRot = Quaternion.AngleAxis(totalTwist, tangents[i]);
                    right = twistRot * right;
                }

                // 十字片第二 pass
                Vector3 dir = right;
                if (p == 1)
                    dir = Vector3.Cross(right, tangents[i]).normalized;

                for (int r = 0; r <= splineRows; r++)
                {
                    float rt = (float)r / splineRows;
                    verts.Add(samples[i] + dir * w * (rt * 2f - 1f));
                    uvs.Add(new Vector2(rt, t));
                }
            }

            // 三角形
            for (int i = 0; i < samples.Count - 1; i++)
            {
                for (int r = 0; r < splineRows; r++)
                {
                    int bl = baseIdx + i * rowVerts + r;
                    int br = bl + 1;
                    int tl = bl + rowVerts;
                    int tr = tl + 1;
                    tris.Add(bl); tris.Add(tl); tris.Add(br);
                    tris.Add(br); tris.Add(tl); tris.Add(tr);
                }
            }
        }

        return BuildMesh("Spline", verts, uvs, tris);
    }

    // ── 工具方法 ───────────────────────────────────

    void ApplyPreviewMaterial(bool useVertColor = false)
    {
        if (previewMR == null) return;
        if (useCustomMaterial && customMaterial != null)
            previewMR.sharedMaterial = customMaterial;
        else if (useVertColor || edgeTransp || heightGradient)
            previewMR.sharedMaterial = vertColorMat;
        else
            previewMR.sharedMaterial = showChecker ? checkerMat : defaultMat;
    }

    void ApplyScale(Mesh mesh, Vector3 scale)
    {
        if (scale == Vector3.one) return;

        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
            verts[i] = Vector3.Scale(verts[i], scale);
        mesh.vertices = verts;

        // 修正法线
        var normals = mesh.normals;
        if (normals != null && normals.Length > 0)
        {
            Vector3 invS = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            for (int i = 0; i < normals.Length; i++)
                normals[i] = Vector3.Scale(normals[i], invS).normalized;
            mesh.normals = normals;
        }

        mesh.RecalculateBounds();
    }

    void SyncSplineFlips()
    {
        while (splinePointFlips.Count < splinePoints.Count)
            splinePointFlips.Add(0f);
        while (splinePointFlips.Count > splinePoints.Count)
            splinePointFlips.RemoveAt(splinePointFlips.Count - 1);
    }

    static void FlipTriangles(Mesh mesh)
    {
        var tris = mesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
        {
            int tmp = tris[i];
            tris[i] = tris[i + 2];
            tris[i + 2] = tmp;
        }
        mesh.triangles = tris;
    }

    void ApplyPivot(Mesh mesh)
    {
        if (pivotOffset == Vector3.zero) return;

        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
            verts[i] -= pivotOffset;
        mesh.vertices = verts;
    }

    // ── 内置 Shader 工厂 ───────────────────────────────────
    static Shader CreateShaderFromSource(string src)
    {
#if UNITY_2021_2_OR_NEWER
        return ShaderUtil.CreateShaderAsset(ref src, false);
#else
        return ShaderUtil.CreateShaderAsset(src, false);
#endif
    }

    static Shader CreateDefaultShader()
    {
        string src = @"
Shader ""Hidden/VFXMeshGen_Default"" {
    SubShader {
        Tags { ""RenderType""=""Opaque"" }
        Pass {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.normal = UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target {
                float ndl = saturate(dot(i.normal * sign(facing), float3(0.3, 0.8, 0.2)));
                return fixed4((0.55 + 0.45 * ndl).xxx, 1);
            }
            ENDCG
        }
    }
}";
        return CreateShaderFromSource(src);
    }

    static Shader CreateVertColorShader()
    {
        string src = @"
Shader ""Hidden/VFXMeshGen_VertColor"" {
    SubShader {
        Tags { ""Queue""=""Transparent"" ""RenderType""=""Transparent"" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; return o; }
            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}";
        return CreateShaderFromSource(src);
    }

    static Shader CreateTwoSideShader()
    {
        string src = @"
Shader ""Hidden/VFXMeshGen_TwoSide"" {
    Properties {
        _MainTex (""Texture"", 2D) = ""white"" {}
        _BackColor1 (""Back Color 1"", Color) = (0.2, 0.35, 0.6, 1)
        _BackColor2 (""Back Color 2"", Color) = (0.15, 0.25, 0.45, 1)
        _UsePreviewTex (""Use Preview Tex"", Float) = 0
    }
    SubShader {
        Tags { ""Queue""=""Transparent"" ""RenderType""=""Transparent"" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            sampler2D _MainTex;
            fixed4 _BackColor1;
            fixed4 _BackColor2;
            float _UsePreviewTex;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target {
                // 有预览贴图时正反面都显示贴图（含Alpha）
                if (_UsePreviewTex > 0.5) return tex2D(_MainTex, i.uv);
                // 正面棋盘格
                if (facing > 0) return tex2D(_MainTex, i.uv);
                // 背面蓝色棋盘格
                float2 uvScaled = i.uv * 8.0;
                int cx = (int)floor(uvScaled.x);
                int cy = (int)floor(uvScaled.y);
                bool checker = ((cx + cy) & 1) == 0;
                return checker ? _BackColor1 : _BackColor2;
            }
            ENDCG
        }
    }
}";
        return CreateShaderFromSource(src);
    }

    static Texture2D CreateCheckerTexture(int size = 128, int cell = 16)
    {
        var tex = new Texture2D(size, size) { wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool c = ((x / cell) + (y / cell)) % 2 == 0;
            tex.SetPixel(x, y, c ? Color.white : new Color(0.55f, 0.55f, 0.55f));
        }
        tex.Apply();
        return tex;
    }

    static Mesh BuildMesh(string name, List<Vector3> v, List<Vector2> uv, List<int> t)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        return mesh;
    }

    static void QuadTris(List<int> tris, int rows, int cols)
    {
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int i = r * (cols + 1) + c;
            tris.Add(i); tris.Add(i + cols + 1); tris.Add(i + 1);
            tris.Add(i + 1); tris.Add(i + cols + 1); tris.Add(i + cols + 2);
        }
    }

    static Vector3 CatmullRom(List<Vector3> pts, float t)
    {
        int n  = pts.Count;
        int i  = Mathf.FloorToInt(t);
        float f = t - i;
        var p0 = pts[Mathf.Clamp(i-1, 0, n-1)];
        var p1 = pts[Mathf.Clamp(i,   0, n-1)];
        var p2 = pts[Mathf.Clamp(i+1, 0, n-1)];
        var p3 = pts[Mathf.Clamp(i+2, 0, n-1)];
        return 0.5f * (2*p1 + (-p0+p2)*f + (2*p0-5*p1+4*p2-p3)*f*f + (-p0+3*p1-3*p2+p3)*f*f*f);
    }
}
