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
using UnityEngine.Playables;

public class VFXMeshGeneratorWindow : EditorWindow
{
    public enum MeshType { Plane, Ring, Cross, Sphere, Helix, Spline, GhostTrail }

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
    private bool     flipU        = false;   // UV1 U 方向翻转
    private bool     flipV        = false;   // UV1 V 方向翻转
    private bool     uv2Enabled   = false;
    private float    uv2Rotate    = 0f;
    private float    uv2UTiling   = 1f;
    private float    uv2VTiling   = 1f;
    private bool     flipU2       = false;   // UV2 U 方向翻转
    private bool     flipV2       = false;   // UV2 V 方向翻转
    private bool     uvFoldout    = false;
    private bool     transformFoldout = false;   // 预览模型 Transform(TRS) 栏，默认收起
    private bool     meshFixFoldout   = false;   // 法线/轴点 栏，默认收起
    private Texture2D uvPreviewTex;
    private bool     flipNormals  = false;
    private Vector3   pivotOffset  = Vector3.zero;

    // ── Plane ─────────────────────────────────────────────────
    private int   planeRows = 10;
    private int   planeCols = 10;
    private float planeW    = 1f;
    private float planeH    = 1f;
    private float planeTwist = 0f;                                   // 中心扭曲角度（度），向外衰减
    private AnimationCurve planeTwistCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f); // 半径(0中心~1外圈)→扭曲权重

    // ── Ring ──────────────────────────────────────────────────
    private int   ringRows   = 2;
    private int   ringCols   = 8;
    private float ringTopR   = 0.5f;
    private float ringBotR   = 1.5f;
    private float ringHeight = 0.5f;
    private float ringArc    = 360f;
    private float ringTwist  = 0f;
    private AnimationCurve ringTwistCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f); // 上半径(0)→下半径(1) 的扭曲权重
    private bool  ringIsCross = false;
    public enum RingUVMode { Rect, Circular }   // 矩形 / 圆形（已移除 Auto）
    private RingUVMode ringUVMode  = RingUVMode.Rect;       // UV1 模式
    private RingUVMode ringUVMode2 = RingUVMode.Circular;   // UV2 模式（独立，默认圆形让 UV2 有意义）
    static readonly string[] RingUVModeLabels = { "矩形", "圆形" };

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
    private float          helixHeadTwist = 0f;
    private float          helixTailTwist = 0f;



    // ── Spline ────────────────────────────────────────────────
    private int            splineRows        = 2;
    private int            splineCols        = 16;
    private float          splineW           = 0.5f;
    public enum SplineShape { Strip, Cross, Tube }
    private SplineShape    splineShape       = SplineShape.Strip;
    static readonly string[] SplineShapeLabels = { "条带", "十字", "管状" };
    private int            splineTubeSides   = 8;
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
    private List<int>   selectedPoints   = new List<int>();
    private bool        splineSoftSelect = false;
    private float       splineSoftRadius = 1.5f;
    private AnimationCurve splineSoftCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    private bool        isBoxSelecting   = false;
    private Vector2     boxSelectStart;
    private bool        hidePreview      = false;

    // ── GhostTrail（刀光：按 timeline 采样武器运动）──────────────
    public enum TrailMode { SinglePoint, TwoPoints }      // 单点+固定长度 / 两点（上下边各一个标记）
    public enum TrailAxis { Up, Right, Forward }          // 单点模式下沿标记点的哪个本地轴拉宽
    public enum TrailAnchor { Center, EndPoint, StartPoint }  // 单点模式轴点：标记点居中 / 作端点 / 作起始点
    private TrailMode   trailMode  = TrailMode.SinglePoint;
    private TrailAxis   trailAxis  = TrailAxis.Forward;
    private TrailAnchor trailAnchor = TrailAnchor.StartPoint;
    private float       trailWidth = 1f;                   // 单点模式：刀光宽度（固定长度，实时影响生成）
    private bool        trailExportWorldSpace = false;     // 导出时保持世界位置；关=导出时重定心到原点（预览始终在世界位置便于核对）
    private bool        trailRecording  = false;           // 记录状态（跟随播放 / 自动播放 共用）
    private bool        trailAutoPlay   = false;           // 自动播放并记录中（工具自己驱动 director 走帧）
    private bool        trailAutoPrimed = false;           // 自动播放：首帧是否已预热(先播放后采样)
    private double      trailAutoFrame  = 0;               // 自动播放当前帧游标
    private double      trailRestoreTime = 0;              // 自动播放前 director 时间备份（结束后还原）
    private bool        trailResample   = false;           // 重采样：把采样路径归一到固定段数，统一面数密度
    private int         trailSegments   = 30;              // 重采样段数（刀光长度方向的面数）
    private int         trailWidthSeg   = 1;               // 纵向面数分段（沿刀光宽度方向细分）
    private bool        trailSmooth     = false;           // 平滑：对采样路径做拉普拉斯平滑（独立于重采样）
    private float       trailSmoothStrength = 0.5f;        // 平滑强度 0~trailSmoothMax
    private float       trailSmoothMax  = 1f;              // 平滑强度滑条最大值（可自定义）
    private bool        trailSingleData = true;            // 采样数据是否为单点模式（宽度可实时重建）
    private bool        trailCross      = false;           // 十字面片：再生成一片绕中心线轴向旋转 90° 的带，组成十字截面
    private int         trailSubsteps   = 10;              // 自动播放：每帧细分采样次数（子帧步进，降速采样，规避运动过快折线）
    private bool        trailAdvFoldout = false;           // 更多设置 折叠
    private bool        trailDirTried   = false;           // 自动检测 Director 是否已尝试
    static readonly string[] TrailModeLabels = { "单点+固定长度", "两点(上下边)" };
    static readonly string[] TrailAxisLabels = { "本地 Up", "本地 Right", "本地 Forward" };
    static readonly string[] TrailAnchorLabels = { "居中", "端点", "起始点" };
    private PlayableDirector trailDirector;                 // 驱动特效的 PlayableDirector（空则自动 Find）
    private Transform   trailEdgeTop;                        // 单点模式=该点；两点模式=刀刃上边
    private Transform   trailEdgeBot;                        // 两点模式：刀刃下边标记点
    private int         trailStartFrame = 0;
    private int         trailEndFrame   = 30;
    private float       trailFps        = 60f;
    private List<Vector3> trailTop = new List<Vector3>();    // 采样缓存（世界坐标）
    private List<Vector3> trailBot = new List<Vector3>();

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
        if (trailRecording) StopTrailRecord();   // 清理记录订阅
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

        // ── 预览位置（Transform / TRS，默认收起）──
        if (previewGO != null)
        {
            EditorGUILayout.Space(4);
            transformFoldout = EditorGUILayout.Foldout(transformFoldout, "Transform", true, EditorStyles.foldoutHeader);
            if (transformFoldout)
            {
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
            }

            // ── 线框 & 面数 ──
            showWireframe = EditorGUILayout.Toggle("显示线框", showWireframe);

            // ── 隐藏预览 ──
            bool prevHide = hidePreview;
            hidePreview = EditorGUILayout.Toggle("隐藏预览", hidePreview);
            if (hidePreview != prevHide && previewMR != null)
            {
                previewMR.enabled = !hidePreview;
                SceneView.RepaintAll();
            }

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
        meshFixFoldout = EditorGUILayout.Foldout(meshFixFoldout, "法线 / 轴点", true, EditorStyles.foldoutHeader);
        if (meshFixFoldout)
        {
            flipNormals  = EditorGUILayout.Toggle("翻转正反面", flipNormals);
            pivotOffset  = EditorGUILayout.Vector3Field("轴点偏移 (Pivot)", pivotOffset);
        }

        EditorGUILayout.Space(4);
        uvFoldout = EditorGUILayout.Foldout(uvFoldout, "UV 设置", true, EditorStyles.foldoutHeader);
        if (uvFoldout)
        {
            if (GUILayout.Button("打开 UV 预览窗口"))
                VFXUVViewerWindow.Open();

            uvRotate = EditorGUILayout.Slider("UV Rotate", uvRotate, 0f, 360f);
            uTiling  = EditorGUILayout.Slider("U Tiling",  uTiling,  0.1f, 10f);
            vTiling  = EditorGUILayout.Slider("V Tiling",  vTiling,  0.1f, 10f);
            EditorGUILayout.BeginHorizontal();
            flipU = EditorGUILayout.ToggleLeft("翻转 U", flipU, GUILayout.Width(80));
            flipV = EditorGUILayout.ToggleLeft("翻转 V", flipV, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            // Ring 专属：UV 映射模式（仅 Ring 类型显示，矩形 / 圆形）
            if (meshType == MeshType.Ring)
                ringUVMode = (RingUVMode)EditorGUILayout.Popup("UV 模式", (int)ringUVMode, RingUVModeLabels);

            EditorGUILayout.Space(2);
            uv2Enabled = EditorGUILayout.Toggle("启用 UV2", uv2Enabled);
            if (uv2Enabled)
            {
                uv2Rotate  = EditorGUILayout.Slider("UV2 Rotate", uv2Rotate, 0f, 360f);
                uv2UTiling = EditorGUILayout.Slider("UV2 U Tiling", uv2UTiling, 0.1f, 10f);
                uv2VTiling = EditorGUILayout.Slider("UV2 V Tiling", uv2VTiling, 0.1f, 10f);
                EditorGUILayout.BeginHorizontal();
                flipU2 = EditorGUILayout.ToggleLeft("翻转 U2", flipU2, GUILayout.Width(90));
                flipV2 = EditorGUILayout.ToggleLeft("翻转 V2", flipV2, GUILayout.Width(90));
                EditorGUILayout.EndHorizontal();

                // UV2 独立的 Ring UV 模式
                if (meshType == MeshType.Ring)
                    ringUVMode2 = (RingUVMode)EditorGUILayout.Popup("UV2 模式", (int)ringUVMode2, RingUVModeLabels);
            }

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
                planeTwist      = EditorGUILayout.Slider("扭曲(中心)", planeTwist, -360f, 360f);
                planeTwistCurve = EditorGUILayout.CurveField("扭曲衰减(中心→外圈)", planeTwistCurve);
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
                ringTwistCurve = EditorGUILayout.CurveField("扭曲衰减(上半径→下半径)", ringTwistCurve);
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
                helixHeadTwist = EditorGUILayout.Slider("头部扭曲", helixHeadTwist, -360f, 360f);
                helixTailTwist = EditorGUILayout.Slider("尾部扭曲", helixTailTwist, -360f, 360f);
                break;

            case MeshType.Spline:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Spline", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                splineShape      = (SplineShape)EditorGUILayout.Popup("形态", (int)splineShape, SplineShapeLabels);
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
                splineSoftSelect = EditorGUILayout.Toggle("软选", splineSoftSelect);
                if (splineSoftSelect)
                {
                    splineSoftRadius = EditorGUILayout.Slider("软选半径", splineSoftRadius, 0.1f, 10f);
                    splineSoftCurve  = EditorGUILayout.CurveField("软选衰减", splineSoftCurve);
                }
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

                EditorGUILayout.HelpBox("Scene 视图中拖动绿色控制点编辑 Spline 路径\n按住 Shift+左键 在最近的线段上插入新点\n空白处拖矩形框选多个控制点\n右键选中点可删除\n选中点后可调整该点翻转角度", MessageType.Info);
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
                    selectedPoints.Clear();
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
                    selectedPoints.Clear();
                    dirty = true;
                }
                break;

            case MeshType.GhostTrail:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "GhostTrail 刀光", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                EditorGUILayout.HelpBox("在武器/手上挂标记点，按帧范围采样其运动连成刀光带。\n单点+固定长度：挂一个点，沿它的本地轴按固定宽度拉出面片（点会跟随武器朝向旋转）。\n两点：挂上下边两个点，宽度取两点真实间距。", MessageType.Info);
                trailMode = (TrailMode)EditorGUILayout.Popup("模式", (int)trailMode, TrailModeLabels);

                // 自动检测场景内 EffectEditorDirector（特效场景一般只有这一个）
                if (trailDirector == null && !trailDirTried) { trailDirector = AutoFindTrailDirector(); trailDirTried = true; }
                trailDirector = (PlayableDirector)EditorGUILayout.ObjectField("Timeline Director", trailDirector, typeof(PlayableDirector), true);

                if (trailMode == TrailMode.SinglePoint)
                {
                    trailEdgeTop = (Transform)EditorGUILayout.ObjectField("标记点", trailEdgeTop, typeof(Transform), true);
                    trailWidth   = EditorGUILayout.FloatField("刀光宽度", trailWidth);
                    trailAxis    = (TrailAxis)EditorGUILayout.Popup("拉伸轴向", (int)trailAxis, TrailAxisLabels);
                    trailAnchor  = (TrailAnchor)EditorGUILayout.Popup("轴点(对齐)", (int)trailAnchor, TrailAnchorLabels);
                }
                else
                {
                    trailEdgeTop = (Transform)EditorGUILayout.ObjectField("刀刃上边标记", trailEdgeTop, typeof(Transform), true);
                    trailEdgeBot = (Transform)EditorGUILayout.ObjectField("刀刃下边标记", trailEdgeBot, typeof(Transform), true);
                }
                trailStartFrame = EditorGUILayout.IntField("起始帧", trailStartFrame);
                trailEndFrame   = EditorGUILayout.IntField("结束帧", trailEndFrame);

                // 面数：长度方向（重采样统一）
                trailResample = EditorGUILayout.Toggle("统一面数(重采样)", trailResample);
                if (trailResample)
                    trailSegments = Mathf.Max(1, EditorGUILayout.IntField("长度方向段数", trailSegments));

                // 折叠：更多设置（采样密度 / 纵向面数 / 平滑 / 十字面片 / 帧率 / 导出）
                trailAdvFoldout = EditorGUILayout.Foldout(trailAdvFoldout, "更多设置", true);
                if (trailAdvFoldout)
                {
                    EditorGUI.indentLevel++;

                    // 子帧采样：自动播放时每帧细分 N 次评估（dir.time 连续插值），运动过快也平滑、不折线。帧号不变
                    trailSubsteps = Mathf.Max(1, EditorGUILayout.IntField("采样密度(子帧/帧)", trailSubsteps));

                    // 纵向面数分段（沿刀光宽度方向细分）
                    trailWidthSeg = Mathf.Max(1, EditorGUILayout.IntField("纵向面数分段", trailWidthSeg));

                    // 平滑：独立于重采样（非统一面数时也可平滑）
                    trailSmooth = EditorGUILayout.Toggle("平滑路径", trailSmooth);
                    if (trailSmooth)
                    {
                        EditorGUILayout.BeginHorizontal();
                        trailSmoothStrength = EditorGUILayout.Slider("平滑强度", trailSmoothStrength, 0f, Mathf.Max(0.01f, trailSmoothMax));
                        GUILayout.Label("max", GUILayout.Width(28));
                        trailSmoothMax = Mathf.Max(0.01f, EditorGUILayout.FloatField(trailSmoothMax, GUILayout.Width(48)));
                        EditorGUILayout.EndHorizontal();
                    }

                    // 十字面片：再生成一片绕中心线轴向旋转 90° 的带，组成十字截面（更有体积）
                    trailCross = EditorGUILayout.Toggle("十字面片", trailCross);

                    trailFps              = EditorGUILayout.FloatField("帧率(FPS)", trailFps);
                    trailExportWorldSpace = EditorGUILayout.Toggle("导出时保持世界位置", trailExportWorldSpace);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.Space(2);

                // ① 推荐：工具自己按帧范围驱动播放并记录，到结束帧自动停并生成（Scene 视图也有快捷按钮）
                var recBg = GUI.backgroundColor;
                GUI.backgroundColor = (trailRecording && trailAutoPlay) ? new Color(1f, 0.5f, 0.5f) : recBg;
                if (GUILayout.Button((trailRecording && trailAutoPlay)
                        ? $"■ 停止（已到第 {(int)trailAutoFrame} 帧）"
                        : $"▶ 自动播放并记录（{trailStartFrame}~{trailEndFrame} 帧，自动停）"))
                    StartAutoRecord();
                GUI.backgroundColor = recBg;
                if (trailRecording && trailAutoPlay)
                    EditorGUILayout.HelpBox("自动播放采样中……工具正驱动 Director 逐帧走，结束帧自动停。\n若角色不动（动作只在你们的播放器里才摆出来），改用下面的「跟随播放记录」。", MessageType.Warning);

                // ② 跟随播放记录：你照常播放特效 timeline，工具跟着记；指定了 Director 时，到结束帧自动停+生成
                GUI.backgroundColor = (trailRecording && !trailAutoPlay) ? new Color(1f, 0.5f, 0.5f) : recBg;
                if (GUILayout.Button((trailRecording && !trailAutoPlay) ? "■ 停止记录（生成刀光）" : "● 跟随播放记录（到结束帧自动停）"))
                    ToggleTrailRecord();
                GUI.backgroundColor = recBg;
                if (trailRecording && !trailAutoPlay)
                {
                    string tip = trailDirector != null
                        ? "记录中……请播放你的特效 timeline。到「结束帧」会自动停并生成（按 Director 时间判定）。"
                        : "记录中……（未指定 Director，不会自动停）请播放你的 timeline，挥砍结束后手动点「停止记录」。";
                    EditorGUILayout.HelpBox(tip, MessageType.Warning);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("采样当前帧（手动累积）")) SampleCurrentFrame();
                if (GUILayout.Button("清空采样")) { trailTop.Clear(); trailBot.Clear(); dirty = true; }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField($"已采样 {trailTop.Count} 帧" + (trailResample ? $" → 生成 {trailSegments} 段" : ""), EditorStyles.miniLabel);
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
        // GhostTrail：Scene 内「自动播放并记录」快捷按钮——放在视图下方居中
        if (meshType == MeshType.GhostTrail)
        {
            float bw = 200f, bh = 28f;
            float bx = (sv.position.width - bw) * 0.5f;
            float by = sv.position.height - bh - 40f;     // 距底部留白，避开底部状态条
            var recRect = new Rect(bx, by, bw, bh);
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = (trailRecording && trailAutoPlay) ? new Color(1f, 0.5f, 0.5f) : new Color(0.6f, 1f, 0.6f);
            if (GUI.Button(recRect, (trailRecording && trailAutoPlay) ? $"■ 停止采样（第 {(int)trailAutoFrame} 帧）" : "▶ 自动播放并记录刀光"))
                StartAutoRecord();
            GUI.backgroundColor = oldBg;
        }
        Handles.EndGUI();

        // GhostTrail：在标记点处画出刀光宽度/轴点预览
        DrawTrailWidthGizmo();

        // 线框显示
        if (!hidePreview && showWireframe && previewMF != null && previewMF.sharedMesh != null)
            DrawWireframe();

        // Spline 模式下，未选中控制点时才画 Transform Handle，避免冲突
        bool hasSelectedPoint = (meshType == MeshType.Spline && selectedPoints.Count > 0);
        if (!hasSelectedPoint)
            DrawPreviewTransformHandle();

        if (meshType == MeshType.Spline)
            DrawSplineHandles();
    }

    // GhostTrail：在标记点处画出刀光宽度连线与上下边端点，直观预览宽度/轴点（不写日志）
    void DrawTrailWidthGizmo()
    {
        if (meshType != MeshType.GhostTrail || trailEdgeTop == null) return;
        Vector3 top, bot;
        if (trailMode == TrailMode.SinglePoint)
        {
            Vector3 cpt = trailEdgeTop.position;
            Vector3 ax  = TrailAxisVec();
            switch (trailAnchor)
            {
                case TrailAnchor.EndPoint:   bot = cpt; top = cpt + ax * trailWidth; break;
                case TrailAnchor.StartPoint: top = cpt; bot = cpt - ax * trailWidth; break;
                default: { Vector3 d = ax * (trailWidth * 0.5f); top = cpt + d; bot = cpt - d; } break;
            }
        }
        else
        {
            if (trailEdgeBot == null) return;
            top = trailEdgeTop.position; bot = trailEdgeBot.position;
        }

        Vector3 mid = (top + bot) * 0.5f;
        Vector3 nrm = (top - bot).sqrMagnitude > 1e-8f ? (top - bot).normalized : Vector3.up;
        float hs = HandleUtility.GetHandleSize(mid) * 0.06f;
        Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
        Handles.DrawLine(bot, top);
        Handles.DrawSolidDisc(top, nrm, hs);                 // 上边端点
        Handles.color = new Color(0.2f, 0.7f, 1f, 1f);
        Handles.DrawSolidDisc(bot, nrm, hs);                 // 下边端点
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

        // 多点整体移动 Handle（选集质心）+ 软选衰减
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

                // 软选：先按移动前的位置算未选中点的衰减权重
                if (splineSoftSelect && deltaLocal.sqrMagnitude > 0f)
                {
                    for (int j = 0; j < splinePoints.Count; j++)
                    {
                        if (selectedPoints.Contains(j)) continue;
                        float dMin = float.MaxValue;
                        foreach (int idx in selectedPoints)
                            dMin = Mathf.Min(dMin, Vector3.Distance(splinePoints[j], splinePoints[idx]));
                        if (dMin < splineSoftRadius)
                        {
                            float w = Mathf.Clamp01(splineSoftCurve.Evaluate(dMin / splineSoftRadius));
                            splinePoints[j] += deltaLocal * w;
                        }
                    }
                }

                // 选中点全量移动
                foreach (int idx in selectedPoints)
                    splinePoints[idx] += deltaLocal;

                dirty = true;
                Repaint();
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
                selectedPoints.Clear();
                selectedPoints.Add(insertIdx);
                dirty = true;
                Repaint();
                e.Use();
            }
        }

        // 矩形框选（空白处左键拖动，非 Shift）
        int boxCtrlId = GUIUtility.GetControlID(FocusType.Passive);
        if (!e.shift && e.button == 0)
        {
            if (e.type == EventType.MouseDown && !PointHovered(t, e.mousePosition))
            {
                isBoxSelecting = true;
                boxSelectStart = e.mousePosition;
                GUIUtility.hotControl = boxCtrlId;   // 抢占控制权，否则 MouseUp 会被 SceneView 默认工具吃掉，框选无法结束
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isBoxSelecting && GUIUtility.hotControl == boxCtrlId)
            {
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && isBoxSelecting && GUIUtility.hotControl == boxCtrlId)
            {
                isBoxSelecting = false;
                GUIUtility.hotControl = 0;
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

        // 画框选矩形（仅当确实持有控制权时，避免残留鬼影框）
        if (isBoxSelecting && GUIUtility.hotControl == boxCtrlId)
        {
            Handles.BeginGUI();
            Rect box = RectFromPoints(boxSelectStart, Event.current.mousePosition);
            EditorGUI.DrawRect(box, new Color(0.3f, 0.6f, 1f, 0.15f));
            GUI.Box(box, GUIContent.none);
            Handles.EndGUI();
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
                    if (idx < splinePointFlips.Count) splinePointFlips.RemoveAt(idx);
                    selectedPoints.Clear();
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
            case MeshType.GhostTrail: mesh = BuildGhostTrail(); break;
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
        if (previewMR != null) previewMR.enabled = !hidePreview;
        SceneView.RepaintAll();
    }

    void ApplyUV(Mesh mesh)
    {
        Vector2[] rectUV = mesh.uv;   // Build* 产出的矩形基础 UV
        // Ring 才有圆形映射；其它面片类型两种模式都等同于矩形基础
        Vector2[] circUV = (meshType == MeshType.Ring) ? RingCircularUV(mesh) : null;

        // UV2（channel1）：按 ringUVMode2 选矩形/圆形基础，再做独立 Tiling/Rotate
        if (uv2Enabled)
        {
            Vector2[] src2 = (circUV != null && ringUVMode2 == RingUVMode.Circular) ? circUV : rectUV;
            var uv2 = TransformUV(src2, uv2Rotate, uv2UTiling, uv2VTiling, flipU2, flipV2);
            mesh.SetUVs(1, new List<Vector2>(uv2));
        }

        // UV1（channel0）：按 ringUVMode 选矩形/圆形基础
        Vector2[] src1 = (circUV != null && ringUVMode == RingUVMode.Circular) ? circUV : rectUV;
        mesh.uv = TransformUV(src1, uvRotate, uTiling, vTiling, flipU, flipV);
    }

    // Ring 圆形 UV：把顶点 XZ 位置直接映射到 0-1 UV 框（圆盘贴图不变形）
    Vector2[] RingCircularUV(Mesh mesh)
    {
        var v = mesh.vertices;
        float maxR = Mathf.Max(Mathf.Max(ringTopR, ringBotR), 0.0001f);
        var uv = new Vector2[v.Length];
        for (int i = 0; i < v.Length; i++)
            uv[i] = new Vector2(v[i].x / maxR * 0.5f + 0.5f, v[i].z / maxR * 0.5f + 0.5f);
        return uv;
    }

    static Vector2[] TransformUV(Vector2[] src, float rotateDeg, float uTile, float vTile, bool flipU, bool flipV)
    {
        float angle = rotateDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        var dst = new Vector2[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            float bu = flipU ? (1f - src[i].x) : src[i].x;   // 先在 0-1 基础上翻转，再 Tiling/Rotate
            float bv = flipV ? (1f - src[i].y) : src[i].y;
            float u = bu * uTile - 0.5f;
            float v = bv * vTile - 0.5f;
            dst[i] = new Vector2(cos * u - sin * v + 0.5f, sin * u + cos * v + 0.5f);
        }
        return dst;
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
        flipU        = false; flipV = false;
        uv2Enabled = false; uv2Rotate = 0f; uv2UTiling = 1f; uv2VTiling = 1f; flipU2 = false; flipV2 = false;
        flipNormals  = false;
        pivotOffset  = Vector3.zero;

        planeRows = 10; planeCols = 10; planeW = 1f; planeH = 1f;
        planeTwist = 0f; planeTwistCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        ringRows = 2; ringCols = 8; ringTopR = 0.5f; ringBotR = 1.5f;
        ringHeight = 0.5f; ringArc = 360f; ringTwist = 0f; ringIsCross = false;
        ringTwistCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        ringUVMode = RingUVMode.Rect; ringUVMode2 = RingUVMode.Circular;
        crossW = 1f; crossH = 1f; crossCount = 2;
        sphereR = 1f; sphereRows = 13; sphereCols = 24; sphereStyle = SphereStyle.Right;
        helixRows = 20; helixCols = 5; helixTopR = 1f; helixBotR = 0.1f;
        helixLoops = 1; helixH = 1f; helixCount = 1; helixIsCross = false;
        helixW = 0.5f; helixCurve = AnimationCurve.Linear(0, 1, 1, 1);
        helixHeadTwist = 0f; helixTailTwist = 0f;
        splineRows = 2; splineCols = 16; splineW = 0.5f;
        splineShape = SplineShape.Strip; splineTubeSides = 8;
        splineTwist = 0f; splineWidthCurve = AnimationCurve.Linear(0, 1, 1, 1);
        splinePoints = new List<Vector3> {
            new Vector3(-2f, 0f, 0f), new Vector3(-0.5f, 1f, 0f),
            new Vector3(0.5f, -1f, 0f), new Vector3(2f, 0f, 0f)
        };
        splinePointFlips = new List<float> { 0f, 0f, 0f, 0f };
        selectedSplinePoint = -1;
        selectedPoints.Clear();
        splineSoftSelect = false; splineSoftRadius = 1.5f;
        splineSoftCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        isBoxSelecting = false;
        hidePreview = false;

        trailDirector = null; trailEdgeTop = null; trailEdgeBot = null; trailDirTried = false;
        trailStartFrame = 0; trailEndFrame = 30; trailFps = 60f;
        trailMode = TrailMode.SinglePoint; trailAxis = TrailAxis.Forward; trailWidth = 1f;
        trailAnchor = TrailAnchor.StartPoint;
        trailExportWorldSpace = false;
        trailResample = false; trailSegments = 30; trailWidthSeg = 1;
        trailSmooth = false; trailSmoothStrength = 0.5f; trailSmoothMax = 1f; trailSingleData = true; trailCross = false; trailSubsteps = 10;
        if (trailRecording) StopTrailRecord();
        trailTop.Clear(); trailBot.Clear();

        if (previewGO != null)
        {
            previewGO.transform.position = Vector3.zero;
            previewGO.transform.rotation = Quaternion.identity;
            previewGO.transform.localScale = Vector3.one;
        }
        if (previewMR != null) previewMR.enabled = true;
        dirty = true;
    }

    // 导出时重定心：GhostTrail 预览始终在世界位置；若未勾「导出时保持世界位置」，导出前把网格质心移到原点
    void RecenterForExport(Mesh m)
    {
        if (m == null || meshType != MeshType.GhostTrail || trailExportWorldSpace) return;
        var v = m.vertices;
        if (v.Length == 0) return;
        Vector3 c = Vector3.zero;
        for (int i = 0; i < v.Length; i++) c += v[i];
        c /= v.Length;
        for (int i = 0; i < v.Length; i++) v[i] -= c;
        m.vertices = v;
        m.RecalculateBounds();
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

        RecenterForExport(copy);

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

        RecenterForExport(mesh);

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
            var uv2 = mesh.uv2;
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

            // UV2 (UVSet1)：放到独立 Layer 1，避免与 UVSet0 在同 layer 上互相覆盖
            // 注：若目标引擎导入只认到一套 UV，改回 layer.SetUVs(layerUV2, EType.eUV) 试同层方案
            if (uv2 != null && uv2.Length > 0)
            {
                fbxMesh.CreateLayer();
                var layer1 = fbxMesh.GetLayer(1);
                var layerUV2 = FbxLayerElementUV.Create(fbxMesh, "UVSet1");
                layerUV2.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                layerUV2.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                for (int i = 0; i < uv2.Length; i++)
                    layerUV2.GetDirectArray().Add(new FbxVector2(uv2[i].x, uv2[i].y));
                layer1.SetUVs(layerUV2);
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

    // ── GhostTrail：按 timeline 逐帧采样武器标记点的世界坐标 ──
    Vector3 TrailAxisVec()
    {
        switch (trailAxis)
        {
            case TrailAxis.Right:   return trailEdgeTop.right;
            case TrailAxis.Forward: return trailEdgeTop.forward;
            default:                return trailEdgeTop.up;
        }
    }

    // 读当前场景姿态下的一对上/下边世界坐标
    bool TryGetTrailEdge(out Vector3 top, out Vector3 bot)
    {
        top = bot = Vector3.zero;
        if (trailEdgeTop == null) { Debug.LogWarning("[GhostTrail] 请先指定标记点。"); return false; }
        if (trailMode == TrailMode.SinglePoint)
        {
            Vector3 cpt = trailEdgeTop.position;               // 世界坐标（标记点）
            Vector3 ax  = TrailAxisVec().normalized;           // 单位本地轴（已含世界朝向）
            // 存储为「以标记点为中心、单位宽度」的两点：宽度与轴点不烘进采样，
            // 生成时实时套用 trailWidth/轴点 → 拖宽度、换轴点即重建（无需重新采样）。
            Vector3 h = ax * 0.5f;
            top = cpt + h; bot = cpt - h;
            trailSingleData = true;
            return true;
        }
        if (trailEdgeBot == null) { Debug.LogWarning("[GhostTrail] 两点模式需指定刀刃上/下边两个标记点。"); return false; }
        top = trailEdgeTop.position; bot = trailEdgeBot.position;
        trailSingleData = false;
        return true;
    }

    // 自动找场景里的特效 Director：优先名字含 EffectEditorDirector，其次场景里唯一的 PlayableDirector
    PlayableDirector AutoFindTrailDirector()
    {
        var all = Object.FindObjectsOfType<PlayableDirector>();
        if (all == null || all.Length == 0) return null;
        foreach (var d in all)
        {
            if (d == null) continue;
            string nm = (d.gameObject != null ? d.gameObject.name : "")
                      + "|" + (d.playableAsset != null ? d.playableAsset.name : "");
            if (nm.IndexOf("EffectEditorDirector", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return d;
        }
        return all.Length == 1 ? all[0] : null;   // 退而求其次：只有一个就用它
    }

    // 手动累积当前帧（兜底：自定义播放取不到 PlayableDirector 时用）
    void SampleCurrentFrame()
    {
        if (!TryGetTrailEdge(out var top, out var bot)) return;
        trailTop.Add(top);
        trailBot.Add(bot);
        dirty = true;
    }

    // ① 自动播放并记录：工具自己按帧范围驱动 director 走帧，逐(子)帧采样，到结束帧自动停并生成。
    //    每一(子)帧都「先播放（设帧+Evaluate）→ 下个 tick 再采样」，含起始帧的预热 tick，避免读到还没更新的旧姿态。
    void StartAutoRecord()
    {
        if (trailRecording) { StopTrailRecord(); return; }     // 再点一次=停止
        var dir = trailDirector != null ? trailDirector : AutoFindTrailDirector();
        if (dir == null) { Debug.LogWarning("[GhostTrail] 自动播放需要 PlayableDirector，请拖入 Director；或改用「跟随播放记录」。"); return; }
        if (trailEdgeTop == null) { Debug.LogWarning("[GhostTrail] 请先指定标记点。"); return; }
        if (trailEndFrame <= trailStartFrame || trailFps <= 0f) { Debug.LogWarning("[GhostTrail] 帧范围/FPS 不合法。"); return; }

        trailDirector    = dir;
        trailRestoreTime = dir.time;
        trailTop.Clear(); trailBot.Clear();
        trailAutoFrame   = trailStartFrame;
        trailAutoPrimed  = false;          // 首帧先预热（先播放，下一 tick 才采样）
        trailAutoPlay    = true;
        trailRecording   = true;
        EditorApplication.update += AutoRecordTick;
        Debug.Log($"[GhostTrail] 自动播放并记录：{trailStartFrame}~{trailEndFrame} 帧，子帧 ×{trailSubsteps}。");
    }

    void AutoRecordTick()
    {
        if (!trailAutoPlay) { EditorApplication.update -= AutoRecordTick; return; }
        var dir = trailDirector;
        if (dir == null) { StopTrailRecord(); return; }

        if (!trailAutoPrimed)
        {
            // 预热：先把场景播放到起始(子)帧，本 tick 不采样——下个 tick 再读，确保姿态已更新
            dir.time = trailAutoFrame / trailFps;
            dir.Evaluate();
            trailAutoPrimed = true;
            Repaint();
            return;
        }

        // 读取上一 tick 已设置并 settle 的当前(子)帧
        if (TryGetTrailEdge(out var top, out var bot)) { trailTop.Add(top); trailBot.Add(bot); }

        if (trailAutoFrame >= trailEndFrame - 1e-6) { StopTrailRecord(); return; }  // 已采到结束帧→先结束采样，再(延迟)还原

        double step = 1.0 / Mathf.Max(1, trailSubsteps);
        trailAutoFrame = System.Math.Min(trailAutoFrame + step, (double)trailEndFrame);
        dir.time = trailAutoFrame / trailFps;                 // 设下一(子)帧，下个 tick 再读
        dir.Evaluate();
        Repaint();
    }

    // ② 跟随播放记录：你照常播放特效 timeline，工具每个 update 记一帧标记点世界坐标。
    void ToggleTrailRecord()
    {
        if (!trailRecording)
        {
            if (trailEdgeTop == null) { Debug.LogWarning("[GhostTrail] 请先指定标记点。"); return; }
            trailTop.Clear(); trailBot.Clear();
            trailAutoPlay  = false;
            trailRecording = true;
            EditorApplication.update += TrailRecordTick;
            Debug.Log("[GhostTrail] 开始记录——请播放你的特效 timeline" +
                      (trailDirector != null ? "，到结束帧会自动停并生成。" : "，结束后点「停止记录」。"));
        }
        else StopTrailRecord();
    }

    void TrailRecordTick()
    {
        if (!trailRecording || trailAutoPlay) { EditorApplication.update -= TrailRecordTick; return; }
        // 指定了 Director 时按其时间做帧门控：未到起始帧不记，过了结束帧自动停
        var dir = trailDirector;
        if (dir != null && trailFps > 0f)
        {
            double f = dir.time * trailFps;
            if (f < trailStartFrame - 0.5) { Repaint(); return; }        // 还没到起始帧
            if (f > trailEndFrame + 0.5)   { StopTrailRecord(); return; } // 过结束帧→自动停+生成
        }
        if (TryGetTrailEdge(out var top, out var bot)) { trailTop.Add(top); trailBot.Add(bot); }
        Repaint();
    }

    void StopTrailRecord()
    {
        bool wasAuto = trailAutoPlay;
        trailRecording = false;
        trailAutoPlay  = false;
        EditorApplication.update -= TrailRecordTick;
        EditorApplication.update -= AutoRecordTick;
        // 先结束采样（上面已停订阅），再延迟还原播放位置——避免还原的那一帧被采进数据
        if (wasAuto && trailDirector != null)
        {
            var d = trailDirector; double rt = trailRestoreTime;
            EditorApplication.delayCall += () => { if (d != null) { d.time = rt; d.Evaluate(); } };
        }
        LogTrailDiag();
        dirty = true;
        Repaint();
    }

    // 诊断：打印采样到的世界坐标范围。若 min≈max 说明运动没被采到（Evaluate 没驱动姿态）
    void LogTrailDiag()
    {
        int n = trailTop.Count;
        if (n == 0) { Debug.Log("[GhostTrail] 采样 0 帧。"); return; }
        Vector3 mn = trailTop[0], mx = trailTop[0];
        for (int i = 0; i < n; i++)
        {
            mn = Vector3.Min(mn, trailTop[i]); mn = Vector3.Min(mn, trailBot[i]);
            mx = Vector3.Max(mx, trailTop[i]); mx = Vector3.Max(mx, trailBot[i]);
        }
        Vector3 size = mx - mn;
        Debug.Log($"[GhostTrail] 采样 {n} 帧。首帧上点世界坐标={trailTop[0]}，路径世界包围盒尺寸={size}。" +
                  (size.magnitude < 1e-3f ? " ⚠ 尺寸≈0，说明标记点几乎没动——大概率是 Evaluate 没驱动到这套骨骼/挂点，建议改用「采样当前帧」边播边点，或确认 Director 绑定。" : ""));
    }

    // 按中心线弧长把 中心线/方向/宽度 一起线性重采样到 (segments+1) 个点，统一长度方向面数密度
    static void ResampleTrail(List<Vector3> ctr, List<Vector3> dir, List<float> wid, int segments)
    {
        int n = ctr.Count;
        if (n < 2 || segments < 1) return;
        var cum = new float[n];
        cum[0] = 0f;
        for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector3.Distance(ctr[i - 1], ctr[i]);
        float total = cum[n - 1];
        var nc = new List<Vector3>(segments + 1);
        var nd = new List<Vector3>(segments + 1);
        var nw = new List<float>(segments + 1);
        for (int s = 0; s <= segments; s++)
        {
            float target = total > 1e-6f ? total * s / segments : 0f;
            int k = 1;
            while (k < n && cum[k] < target) k++;
            if (k >= n) k = n - 1;
            float seg = cum[k] - cum[k - 1];
            float a = seg > 1e-6f ? (target - cum[k - 1]) / seg : 0f;
            nc.Add(Vector3.Lerp(ctr[k - 1], ctr[k], a));
            Vector3 d = Vector3.Slerp(dir[k - 1], dir[k], a);
            nd.Add(d.sqrMagnitude > 1e-8f ? d.normalized : dir[k - 1]);
            nw.Add(Mathf.Lerp(wid[k - 1], wid[k], a));
        }
        ctr.Clear(); ctr.AddRange(nc);
        dir.Clear(); dir.AddRange(nd);
        wid.Clear(); wid.AddRange(nw);
    }

    // 拉普拉斯平滑：只平滑中心线（每点向左右邻居中点靠拢），宽度与方向不动 →
    // 形态变平滑、但每个截面（起末两点间距=刀光宽度）不缩小。strength 越大迭代越多，两端固定。
    static void SmoothCenter(List<Vector3> ctr, float strength)
    {
        int n = ctr.Count;
        if (n < 3 || strength <= 0f) return;
        int iterations = Mathf.Clamp(Mathf.RoundToInt(strength * 8f), 1, 2000);
        float lambda = 0.5f * Mathf.Clamp01(strength);   // 自定义 max>1 时靠迭代次数增强，lambda 仍保持稳定
        if (lambda <= 0f) lambda = 0.5f;
        for (int it = 0; it < iterations; it++) SmoothPass(ctr, n, lambda);
    }

    static void SmoothPass(List<Vector3> p, int n, float lambda)
    {
        Vector3 prev = p[0];                              // 平滑前的左邻居
        for (int i = 1; i < n - 1; i++)
        {
            Vector3 cur = p[i];
            Vector3 mid = (prev + p[i + 1]) * 0.5f;       // 右邻居仍是原值（尚未改到）
            p[i] = Vector3.Lerp(cur, mid, lambda);
            prev = cur;
        }
    }

    Mesh BuildGhostTrail()
    {
        int n0 = Mathf.Min(trailTop.Count, trailBot.Count);
        if (n0 < 2) return BuildMesh("GhostTrail", new List<Vector3>(), new List<Vector2>(), new List<int>());

        // 分解为 中心线 + 单位方向(下→上) + 宽度。
        //   单点数据：宽度实时取 trailWidth（拖「刀光宽度」即重建，像段数那样实时）；
        //   两点数据：宽度=两标记真实间距（每帧不同）。
        var ctr = new List<Vector3>(n0);
        var dir = new List<Vector3>(n0);
        var wid = new List<float>(n0);
        for (int i = 0; i < n0; i++)
        {
            Vector3 t = trailTop[i], b = trailBot[i];
            Vector3 d = t - b;
            float len = d.magnitude;
            ctr.Add((t + b) * 0.5f);
            dir.Add(len > 1e-6f ? d / len : Vector3.up);
            wid.Add(trailSingleData ? trailWidth : len);
        }

        // 平滑：只动中心线，宽度保持 → 微调平滑度不缩小整体形态
        if (trailSmooth) SmoothCenter(ctr, trailSmoothStrength);
        // 统一面数：按中心线弧长重采样 中心线/方向/宽度
        if (trailResample && trailSegments >= 1) ResampleTrail(ctr, dir, wid, trailSegments);

        int n = ctr.Count;
        if (n < 2) return BuildMesh("GhostTrail", new List<Vector3>(), new List<Vector2>(), new List<int>());

        // 由 中心线+方向+宽度(+轴点) 还原上/下边
        var top = new List<Vector3>(n);
        var bot = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            Vector3 cc = ctr[i];
            Vector3 dd = dir[i].sqrMagnitude > 1e-8f ? dir[i].normalized : Vector3.up;
            float w = wid[i];
            Vector3 tp, bt;
            if (trailSingleData)
            {
                switch (trailAnchor)
                {
                    case TrailAnchor.EndPoint:   bt = cc; tp = cc + dd * w; break;            // 标记点=端点
                    case TrailAnchor.StartPoint: tp = cc; bt = cc - dd * w; break;            // 标记点=起始点
                    default:                     tp = cc + dd * (w * 0.5f); bt = cc - dd * (w * 0.5f); break; // 居中
                }
            }
            else { tp = cc + dd * (w * 0.5f); bt = cc - dd * (w * 0.5f); }
            top.Add(tp); bot.Add(bt);
        }

        // 预览始终保持世界位置（便于和武器挥砍核对）。导出时再按「导出时保持世界位置」决定是否重定心。
        Vector3 c = Vector3.zero;

        int wseg = Mathf.Max(1, trailWidthSeg);          // 纵向（宽度方向）分段
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        // 主片
        EmitTrailRibbon(top, bot, n, wseg, c, verts, uvs, tris);

        // 十字面片：绕中心线切线再生成一片垂直的带，与主片组成十字截面（更有体积）
        if (trailCross)
        {
            var ct = new List<Vector3>(n);
            var cb = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 mid  = (top[i] + bot[i]) * 0.5f;     // 主片该截面中心
                Vector3 wvec = top[i] - bot[i];
                float w = wvec.magnitude;
                Vector3 wu = w > 1e-6f ? wvec / w : Vector3.up;     // 主片宽度方向
                // 中心线切线（向前+向后取平均，端点单边）
                Vector3 back = (i > 0)     ? mid - (top[i - 1] + bot[i - 1]) * 0.5f : Vector3.zero;
                Vector3 fwd  = (i < n - 1) ? (top[i + 1] + bot[i + 1]) * 0.5f - mid : Vector3.zero;
                Vector3 tan = back + fwd;
                if (tan.sqrMagnitude < 1e-10f) tan = fwd.sqrMagnitude > 1e-10f ? fwd : back;
                tan = tan.sqrMagnitude > 1e-10f ? tan.normalized : Vector3.forward;
                Vector3 perp = Vector3.Cross(tan, wu);             // 垂直于宽度与切线 → 十字另一片方向
                perp = perp.sqrMagnitude > 1e-10f ? perp.normalized : wu;
                ct.Add(mid + perp * (w * 0.5f));
                cb.Add(mid - perp * (w * 0.5f));
            }
            EmitTrailRibbon(ct, cb, n, wseg, c, verts, uvs, tris);
        }

        return BuildMesh("GhostTrail", verts, uvs, tris);
    }

    // 把一对 上/下边 边线连成带状网格，追加到共享的 verts/uvs/tris（支持十字面片多次调用）
    static void EmitTrailRibbon(List<Vector3> top, List<Vector3> bot, int n, int wseg, Vector3 c,
                                List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        int cols = wseg + 1;
        int baseIdx = verts.Count;
        for (int i = 0; i < n; i++)
        {
            float v = (float)i / (n - 1);                 // 沿长度
            Vector3 b = bot[i] - c, t = top[i] - c;
            for (int j = 0; j < cols; j++)
            {
                float u = (float)j / wseg;                // 沿宽度 0(下)→1(上)
                verts.Add(Vector3.Lerp(b, t, u));
                uvs.Add(new Vector2(u, v));
            }
        }
        for (int i = 0; i < n - 1; i++)
        for (int j = 0; j < wseg; j++)
        {
            int a  = baseIdx + i * cols + j; // 当前帧 j
            int bb = a + 1;                  // 当前帧 j+1
            int cc = a + cols;               // 下一帧 j
            int dd = cc + 1;                 // 下一帧 j+1
            tris.Add(a);  tris.Add(cc); tris.Add(bb);
            tris.Add(bb); tris.Add(cc); tris.Add(dd);
        }
    }

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

        // 扭曲：绕面法线(Y轴)从中心旋转，按半径权重向外圈衰减（旋涡效果，点保持在面内）
        if (Mathf.Abs(planeTwist) > 0.001f)
        {
            float maxR = 0.5f * Mathf.Sqrt(planeW * planeW + planeH * planeH);
            if (maxR > 0.0001f)
            {
                float twistRad = planeTwist * Mathf.Deg2Rad;
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 p = verts[i];
                    float t = Mathf.Clamp01(Mathf.Sqrt(p.x * p.x + p.z * p.z) / maxR);
                    float a = twistRad * planeTwistCurve.Evaluate(t);
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    verts[i] = new Vector3(p.x * cos - p.z * sin, 0f, p.x * sin + p.z * cos);
                }
            }
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
            float twist  = ringTwist * ringTwistCurve.Evaluate(t) * Mathf.Deg2Rad;   // 上半径(t=0)为主，按曲线向下衰减

            for (int c = 0; c <= ringCols; c++)
            {
                float ct    = (float)c / ringCols;
                float angle = ct * ringArc * Mathf.Deg2Rad + twist;
                verts.Add(new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
                uvs.Add(new Vector2(ct, t));
            }
        }

        // 注：圆形 UV 已移到 ApplyUV()，按 UV1/UV2 各自的模式分别计算（见 RingCircularUV）。
        //     此处只产出矩形基础 UV。

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
                    float twist = ringTwist * ringTwistCurve.Evaluate(t) * Mathf.Deg2Rad;   // 与普通环一致：上半径为主，按曲线衰减
                    float angle = ct * ringArc * Mathf.Deg2Rad + twist;

                    // 该列的顶点和底点（含扭曲）：端点按曲线端值，上半径(curve(0))、下半径(curve(1))
                    float topAngle = ct * ringArc * Mathf.Deg2Rad + ringTwist * ringTwistCurve.Evaluate(0f) * Mathf.Deg2Rad;
                    float botAngle = ct * ringArc * Mathf.Deg2Rad + ringTwist * ringTwistCurve.Evaluate(1f) * Mathf.Deg2Rad;
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

                // 头尾扭曲：绕 tangent 旋转 bitang，端点强、中间(t=0.5)为 0
                float wHead = 1f - Mathf.SmoothStep(0f, 0.5f, t);
                float wTail = Mathf.SmoothStep(0.5f, 1f, t);
                float extraTwist = helixHeadTwist * wHead + helixTailTwist * wTail;
                if (Mathf.Abs(extraTwist) > 0.01f)
                    bitang = Quaternion.AngleAxis(extraTwist, tangent) * bitang;

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
        int passes   = (splineShape == SplineShape.Cross) ? 2 : 1;

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
        _UVChannel (""UV Channel"", Float) = 0
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
            float _UVChannel;
            struct appdata { float4 vertex : POSITION; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = (_UVChannel > 0.5) ? v.uv1 : v.uv0; return o; }
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

    // ── 供 UV 预览窗口读取（只读）──
    public Mesh GetPreviewMeshForUV() => (previewMF != null) ? previewMF.sharedMesh : null;
    public Texture GetUVBackgroundTexture() => (uvPreviewTex != null) ? (Texture)uvPreviewTex : checkerTex;

    // 让预览模型按指定 UV 通道采样贴图（0=UV1, 1=UV2）；UV2 未启用时回退 UV1。幂等，仅在变化时刷新
    public void SetPreviewUVChannel(int ch)
    {
        if (checkerMat == null) return;
        float target = (ch == 1 && uv2Enabled) ? 1f : 0f;
        if (!Mathf.Approximately(checkerMat.GetFloat("_UVChannel"), target))
        {
            checkerMat.SetFloat("_UVChannel", target);
            SceneView.RepaintAll();
        }
    }
}

// ============================================================
//  UV 预览窗口（只读可视化）—— 类 3dsMax UV 编辑器的查看面板
//  与主窗口同文件，便于单文件部署；通过 Resources.FindObjectsOfTypeAll
//  实时读取主窗口当前预览 Mesh 的 UV 布局并绘制。
// ============================================================
public class VFXUVViewerWindow : EditorWindow
{
    int      uvChannel   = 0;        // 0 = UV1（channel0）, 1 = UV2（channel1）
    float    zoom        = 1f;
    Vector2  pan         = Vector2.zero;
    bool     showTexture = true;
    bool     showWire    = true;
    bool     showGrid    = true;
    Material lineMat;

    readonly List<Vector2> uvs = new List<Vector2>();

    [MenuItem("Tools/VFX UV Viewer")]
    public static void Open()
    {
        var w = GetWindow<VFXUVViewerWindow>("UV 预览");
        w.minSize = new Vector2(320, 360);
        w.Show();
    }

    void OnEnable()
    {
        var sh = Shader.Find("Hidden/Internal-Colored");
        lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        lineMat.SetInt("_ZTest",   (int)UnityEngine.Rendering.CompareFunction.Always);
        lineMat.SetInt("_ZWrite",  0);
        lineMat.SetInt("_Cull",    (int)UnityEngine.Rendering.CullMode.Off);
        lineMat.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMat.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    }

    void OnDisable()
    {
        if (lineMat != null) DestroyImmediate(lineMat);
        var s = FindSource();
        if (s != null) s.SetPreviewUVChannel(0);   // 关闭窗口时预览模型恢复 UV1
    }

    void OnInspectorUpdate() => Repaint();   // 随主窗口参数变化实时刷新

    static VFXMeshGeneratorWindow FindSource()
    {
        var arr = Resources.FindObjectsOfTypeAll<VFXMeshGeneratorWindow>();
        return (arr != null && arr.Length > 0) ? arr[0] : null;
    }

    void OnGUI()
    {
        var src   = FindSource();
        Mesh mesh = (src != null) ? src.GetPreviewMeshForUV() : null;

        bool hasUV2 = false;
        if (mesh != null)
        {
            var tmp = new List<Vector2>();
            mesh.GetUVs(1, tmp);
            hasUV2 = tmp.Count > 0;
            mesh.GetUVs((uvChannel == 1 && hasUV2) ? 1 : 0, uvs);
        }

        // —— 工具栏 ——
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        using (new EditorGUI.DisabledScope(mesh == null))
        {
            uvChannel   = GUILayout.Toolbar(uvChannel, new[] { "UV1", "UV2" }, EditorStyles.toolbarButton, GUILayout.Width(110));
            showTexture = GUILayout.Toggle(showTexture, "贴图", EditorStyles.toolbarButton, GUILayout.Width(46));
            showWire    = GUILayout.Toggle(showWire,    "线框", EditorStyles.toolbarButton, GUILayout.Width(46));
            showGrid    = GUILayout.Toggle(showGrid,    "网格", EditorStyles.toolbarButton, GUILayout.Width(46));
            if (GUILayout.Button("适配", EditorStyles.toolbarButton, GUILayout.Width(46))) { zoom = 1f; pan = Vector2.zero; }
        }
        GUILayout.FlexibleSpace();
        if (mesh != null)
            GUILayout.Label($"{mesh.name}  顶点{uvs.Count}  三角{mesh.triangles.Length / 3}" +
                            (uvChannel == 1 && !hasUV2 ? "  (无UV2,显示UV1)" : ""), EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        // 预览模型同步显示当前选中通道的 UV（幂等，仅变化时刷新）
        if (src != null) src.SetPreviewUVChannel(uvChannel);

        if (mesh == null)
        {
            EditorGUILayout.HelpBox("未找到 VFX Mesh Generator 的预览网格。\n请先打开主窗口（Tools ▸ VFX Mesh Generator）并生成网格。", MessageType.Info);
            return;
        }

        Rect canvas = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        HandleInput(canvas);
        if (Event.current.type == EventType.Repaint)
            DrawCanvas(canvas, mesh, showTexture ? src.GetUVBackgroundTexture() : null);

        EditorGUILayout.LabelField("滚轮缩放 · 拖拽平移 · 适配复位", EditorStyles.centeredGreyMiniLabel);
    }

    void HandleInput(Rect canvas)
    {
        var e = Event.current;
        if (!canvas.Contains(e.mousePosition)) return;
        if (e.type == EventType.ScrollWheel)
        {
            zoom = Mathf.Clamp(zoom * (1f - e.delta.y * 0.03f), 0.1f, 30f);
            e.Use(); Repaint();
        }
        else if (e.type == EventType.MouseDrag && (e.button == 0 || e.button == 2))
        {
            pan += e.delta;
            e.Use(); Repaint();
        }
    }

    void DrawCanvas(Rect canvas, Mesh mesh, Texture bg)
    {
        GUI.BeginClip(canvas);
        float w = canvas.width, h = canvas.height;

        EditorGUI.DrawRect(new Rect(0, 0, w, h), new Color(0.16f, 0.16f, 0.16f));

        float size = Mathf.Min(w, h) * 0.8f * zoom;
        Vector2 c  = new Vector2(w * 0.5f, h * 0.5f) + pan;
        Rect box   = new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size);

        if (bg != null)
            GUI.DrawTextureWithTexCoords(box, bg, new Rect(0, 0, 1, 1));

        GL.PushMatrix();
        GL.LoadPixelMatrix();
        lineMat.SetPass(0);

        // 0.25 间隔细分网格
        if (showGrid)
        {
            GL.Begin(GL.LINES);
            GL.Color(new Color(1, 1, 1, 0.12f));
            for (int i = 1; i < 4; i++)
            {
                float gx = box.x + size * i / 4f;
                float gy = box.y + size * i / 4f;
                GL.Vertex3(gx, box.y, 0); GL.Vertex3(gx, box.y + size, 0);
                GL.Vertex3(box.x, gy, 0); GL.Vertex3(box.x + size, gy, 0);
            }
            GL.End();
        }

        // 0-1 边界框
        GL.Begin(GL.LINES);
        GL.Color(new Color(1f, 0.8f, 0.2f, 0.9f));
        RectLines(box);
        GL.End();

        // UV 线框
        if (showWire && uvs.Count > 0)
        {
            int[] tris = mesh.triangles;
            GL.Begin(GL.LINES);
            GL.Color(new Color(0.35f, 0.95f, 0.45f, 0.85f));
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], cc = tris[i + 2];
                if (a >= uvs.Count || b >= uvs.Count || cc >= uvs.Count) continue;
                Vector2 pa = UVToScreen(uvs[a], box), pb = UVToScreen(uvs[b], box), pc = UVToScreen(uvs[cc], box);
                GL.Vertex3(pa.x, pa.y, 0); GL.Vertex3(pb.x, pb.y, 0);
                GL.Vertex3(pb.x, pb.y, 0); GL.Vertex3(pc.x, pc.y, 0);
                GL.Vertex3(pc.x, pc.y, 0); GL.Vertex3(pa.x, pa.y, 0);
            }
            GL.End();
        }

        GL.PopMatrix();
        GUI.EndClip();
    }

    static Vector2 UVToScreen(Vector2 uv, Rect box)
        => new Vector2(box.x + uv.x * box.width, box.y + (1f - uv.y) * box.height);   // y 翻转：v=0 在底

    static void RectLines(Rect r)
    {
        GL.Vertex3(r.x,           r.y,            0); GL.Vertex3(r.x + r.width, r.y,            0);
        GL.Vertex3(r.x + r.width, r.y,            0); GL.Vertex3(r.x + r.width, r.y + r.height, 0);
        GL.Vertex3(r.x + r.width, r.y + r.height, 0); GL.Vertex3(r.x,           r.y + r.height, 0);
        GL.Vertex3(r.x,           r.y + r.height, 0); GL.Vertex3(r.x,           r.y,            0);
    }
}
