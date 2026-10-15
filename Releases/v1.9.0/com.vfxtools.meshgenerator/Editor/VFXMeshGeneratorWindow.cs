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

public partial class VFXMeshGeneratorWindow : EditorWindow
{
    public enum MeshType { Plane, Ring, Cross, Sphere, Helix, Spline, GhostTrail, Custom }

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
    private float    uvTileMax    = 10f;      // U/V Tiling 滑条上限（可自定义）
    // 预览流动速度（仅用于 Scene 预览滚动动画，不烘焙进模型 UV）
    private Vector2  uvPreviewSpeed  = Vector2.zero;   // UV1 预览流速
    private Vector2  uv2PreviewSpeed = Vector2.zero;   // UV2 预览流速
    // 预览平铺（仅 Scene 预览与面板缩略图，不烘焙进模型 UV）——与 Noise 的 Tiling 一样自由输入
    private Vector2  uvPreviewTile   = Vector2.one;    // UV1 预览平铺
    private Vector2  uv2PreviewTile  = Vector2.one;    // UV2 预览平铺
    private bool     uvTilePreview   = true;           // 面板内显示平铺缩略图
    private Vector2  _uvScrollAccum  = Vector2.zero;   // 累计滚动偏移
    private double   _lastScrollTime = 0;
    private bool     previewCullBack = false;          // 预览时剔除背面（Noise 造成翻折时隐藏背面）
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
    private RingUVMode planeUVMode  = RingUVMode.Rect;       // Plane UV1 模式（矩形/圆形）
    private RingUVMode planeUVMode2 = RingUVMode.Circular;   // Plane UV2 模式

    // ── Ring ──────────────────────────────────────────────────
    private int   ringRows   = 2;
    private int   ringCols   = 8;
    private float ringTopR   = 0.5f;
    private float ringBotR   = 1.5f;
    private float ringHeight = 0.5f;
    private float ringArc    = 360f;
    private float ringTwist  = 0f;
    private AnimationCurve ringTwistCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f); // 上半径(0)→下半径(1) 的扭曲权重
    private AnimationCurve ringRadiusCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f); // 半径权重(上→下)，1=不改变，>1膨胀，<1收缩
    private AnimationCurve ringHeightCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f); // 高度权重(上→下)，1=线性，可做非线性分布
    private float ringTiltX = 0f;    // 整体绕 X 轴倾斜角度（度）
    private float ringTiltZ = 0f;    // 整体绕 Z 轴倾斜角度（度）
    private bool  ringIsCross = false;
    private float ringCrossW    = 1f;    // 十字面宽度倍数（1 = 与环面斜边等宽）
    private float ringCrossWMax = 5f;    // 该滑条上限（可自定义）
    public enum RingUVMode { Rect, Circular, Polar }   // 矩形 / 圆形 / 极坐标
    private RingUVMode ringUVMode  = RingUVMode.Rect;       // UV1 模式
    private RingUVMode ringUVMode2 = RingUVMode.Circular;   // UV2 模式（独立，默认圆形让 UV2 有意义）
    static readonly string[] RingUVModeLabels = { "矩形", "圆形", "极坐标" };

    // ── Cross ─────────────────────────────────────────────────
    private float crossW     = 1f;
    private float crossH     = 1f;
    private int   crossCount = 2;
    private float crossExtraW    = 1f;   // 第 2 片起（新增的十字面）宽度倍数
    private float crossExtraWMax = 5f;   // 该滑条上限（可自定义）

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
    private float          helixCrossW    = 1f;   // 十字面宽度倍数（1 = 与主面等宽）
    private float          helixCrossWMax = 5f;   // 该滑条上限（可自定义）
    private float          helixW        = 0.5f;
    private AnimationCurve helixCurve    = AnimationCurve.Linear(0, 1, 1, 1);
    // 按归一化高度 Y 缩放整个 XZ 截面；默认 1 保持旧版形状。
    private AnimationCurve helixYProfileCurve = AnimationCurve.Linear(0, 1, 1, 1);
    private float          helixHeadTwist = 0f;
    private float          helixTailTwist = 0f;



    // ── Spline ────────────────────────────────────────────────
    private int            splineRows        = 2;
    private int            splineCols        = 16;
    private float          splineW           = 0.5f;
    private float          splineWMax        = 5f;    // 宽度滑条上限（可自定义，原先硬编码 5）
    private float          splineCrossW      = 1f;    // 十字形态：第二片宽度倍数（1 = 与主片等宽）
    private float          splineCrossWMax   = 5f;    // 该滑条上限（可自定义）
    public enum SplineShape { Strip, Cross, Tube }
    private SplineShape    splineShape       = SplineShape.Strip;
    static readonly string[] SplineShapeLabels = { "条带", "十字", "管状" };
    private int            splineTubeSides   = 8;
    private float          splineTwist       = 0f;
    private AnimationCurve splineWidthCurve  = AnimationCurve.Linear(0, 1, 1, 1);
    [System.Serializable]
    private class SplinePath
    {
        public List<Vector3> points = new List<Vector3>
        {
            new Vector3(-2f, 0f, 0f), new Vector3(-0.5f, 1f, 0f),
            new Vector3(0.5f, -1f, 0f), new Vector3(2f, 0f, 0f)
        };
        public List<float> flips = new List<float> { 0f, 0f, 0f, 0f };
    }
    [SerializeField] private List<SplinePath> splinePaths = new List<SplinePath> { new SplinePath() };
    [SerializeField] private int activeSplineIndex;
    private SplinePath ActiveSpline
    {
        get
        {
            if (splinePaths == null) splinePaths = new List<SplinePath>();
            if (splinePaths.Count == 0) splinePaths.Add(new SplinePath());
            activeSplineIndex = Mathf.Clamp(activeSplineIndex, 0, splinePaths.Count - 1);
            if (splinePaths[activeSplineIndex] == null) splinePaths[activeSplineIndex] = new SplinePath();
            return splinePaths[activeSplineIndex];
        }
    }
    private List<Vector3> splinePoints => ActiveSpline.points;
    private List<float> splinePointFlips => ActiveSpline.flips;
    private int  selectedSplinePoint = -1;
    private bool splineFoldout       = true;
    private List<int>   selectedPoints   = new List<int>();
    private bool        splineSoftSelect = false;
    private float       splineSoftRadius = 1.5f;
    private AnimationCurve splineSoftCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    private bool        isBoxSelecting   = false;
    private Vector2     boxSelectStart;
    private bool        hidePreview      = false;

    // ── 控制点编辑工具（Spline / Custom 共用）─────────────────
    public enum PointTool   { Move, Rotate, Scale }
    public enum HandleSpace { World, Local }
    public enum SplinePositionSpace { Local, World }
    static readonly string[] PointToolLabels   = { "移动", "旋转", "缩放" };
    static readonly string[] HandleSpaceLabels = { "世界坐标", "本地坐标" };
    static readonly string[] SplinePositionLabels = { "Local Position", "World Position" };
    private PointTool   pointTool  = PointTool.Move;
    private HandleSpace pointSpace = HandleSpace.World;
    private SplinePositionSpace splinePositionSpace = SplinePositionSpace.Local;
    private Quaternion  _ptRot     = Quaternion.identity;   // 旋转 Handle 累积姿态
    private Vector3     _ptScale   = Vector3.one;           // 缩放 Handle 累积值
    private int         _ptStateHash = 0;                   // 选集/工具/坐标系变化检测 → 复位 Handle

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
    private float       trailCrossW     = 1f;              // 十字面宽度倍数（1 = 与主片等宽）
    private float       trailCrossWMax  = 5f;              // 该滑条上限（可自定义）
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

    // ── Custom（自定义多边形模型）──────────────────────────────
    public enum CustomUVFlowDir { WorldX, WorldZ, LocalX, LocalZ, BoundingBox, Polar }
    static readonly string[] CustomUVFlowDirLabels = { "世界 X", "世界 Z", "本地 X", "本地 Z", "包围盒", "极坐标" };
    private List<Vector3> customPoints = new List<Vector3>
    {
        new Vector3(-1f, 0f, -1f),
        new Vector3( 1f, 0f, -1f),
        new Vector3( 1f, 0f,  1f),
        new Vector3(-1f, 0f,  1f),
    };
    private int   customSelectedPoint = -1;
    private List<int> customSelectedPoints = new List<int>();
    private bool  customSoftSelect = false;
    private float customSoftRadius = 1.5f;
    private AnimationCurve customSoftCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    private bool  customIsBoxSelecting = false;
    private Vector2 customBoxSelectStart;
    private int   customFillDensity = 4;             // 内部填充密度（网格分辨率，越大越密）
    private CustomUVFlowDir customUVFlowDir = CustomUVFlowDir.BoundingBox;
    private bool  customFlipUV = false;
    private bool  customFoldout = true;

    // ── Custom 高度控制点（把平面雕成立体面，类似重拓扑的顶点抬升）──
    private bool          customHeightEnabled = false;
    private List<Vector3> customHeightPoints  = new List<Vector3>(); // 本地坐标，y = 高度偏移
    private List<float>   customHeightRadii   = new List<float>();    // 每个点的影响半径
    private int           customSelectedHeightPoint = -1;
    private AnimationCurve customHeightFalloff = AnimationCurve.EaseInOut(0, 1, 1, 0);
    private bool          customHeightFoldout = true;
    private float         customHeightMax       = 5f;    // 高度滑条上下限（±max，可自定义）
    private float         customHeightRadiusMax = 10f;   // 影响半径滑条上限（可自定义）

    // ── Custom 边界控制点高度（均值坐标插值）──
    // 边界控制点的 Y 会通过 MVC（Mean Value Coordinates）平滑插值到面内每个顶点，
    // 因此拖动边界点上下即整片跟着起伏，不再永远是一张平面。
    private bool    customBoundaryHeight = true;   // 关闭 = 旧行为（边界 Y 被忽略，强制平面）
    private bool    customIrregularEdge = false;
    private float   customEdgeAmplitude = 0.15f;
    private float   customEdgeAmplitudeMax = 1f;
    private int     customEdgeSegments = 8;
    private float   customEdgeFrequency = 3f;
    private int     customEdgeSeed = 0;
    private List<Vector3> _customBoundaryCache;
    private int _customBoundaryCacheHash;
    private float[] _mvcDx, _mvcDz, _mvcR, _mvcTan;   // MVC 计算复用的暂存数组

    // ── Noise 顶点偏移（全局，对所有面片类型生效）────────────────
    public enum NoiseAxis { Normal, X, Y, Z, Custom }
    static readonly string[] NoiseAxisLabels = { "法线", "X 轴", "Y 轴", "Z 轴", "自定义" };
    private bool        noiseEnabled       = false;
    private Texture2D   noiseTex           = null;
    private float       noiseStrength      = 0.1f;
    private Vector2     noiseTiling        = Vector2.one;    // UV 平铺次数（U,V 独立）
    private Vector2     noiseOffset        = Vector2.zero;   // UV 偏移
    private NoiseAxis   noiseAxis          = NoiseAxis.Normal;
    private Vector3     noiseAxisCustom    = new Vector3(0f, 1f, 0f);
    // 强度遮罩（与顶点色完全解耦，Noise 专用）：UV 渐变 + 高度渐变，各自独立参数
    private bool          noiseMaskUVEnabled     = false;
    private float         noiseMaskUVFalloffU    = 0.5f;
    private float         noiseMaskUVFalloffV    = 0.5f;
    private bool          noiseMaskHeightEnabled = false;
    private AnimationCurve noiseMaskHeightCurve  = AnimationCurve.Linear(0, 0, 1, 1); // 归一化高度→遮罩(0~1)
    private bool        noiseFoldout       = false;
    private Color[]     _noisePixels;                    // 缓存噪声像素
    private int         _noiseTexW, _noiseTexH;
    private Texture2D   _noiseTexRef;                    // 记录当前缓存的贴图引用

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
        EditorApplication.update += PreviewScrollTick;
        _lastScrollTime = EditorApplication.timeSinceStartup;
        EnsurePreview();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= PreviewScrollTick;
        if (trailRecording) StopTrailRecord();   // 清理记录订阅
        DestroyPreview();
    }

    // 预览 UV 滚动：把当前预览通道的流速累加成偏移写进预览材质（仅预览，不改模型 UV）
    void PreviewScrollTick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastScrollTime);
        _lastScrollTime = now;

        if (checkerMat == null) return;
        // 按当前预览的 UV 通道取对应流速 / 预览平铺
        bool ch2 = uv2Enabled && checkerMat.GetFloat("_UVChannel") > 0.5f;
        PushPreviewTile(ch2);
        Vector2 speed = ch2 ? uv2PreviewSpeed : uvPreviewSpeed;
        if (speed == Vector2.zero) return;

        _uvScrollAccum += speed * dt;
        // 取模避免长时间累加导致精度问题
        _uvScrollAccum = new Vector2(Mathf.Repeat(_uvScrollAccum.x, 1f), Mathf.Repeat(_uvScrollAccum.y, 1f));
        checkerMat.SetVector("_Scroll", _uvScrollAccum);
        SceneView.RepaintAll();
        if (uvFoldout && uvTilePreview) Repaint();   // 面板里的平铺缩略图跟着动
    }

    // 把「预览平铺」写进预览材质（仅预览，不烘焙进模型 UV）
    void PushPreviewTile(bool channel2)
    {
        if (checkerMat == null) return;
        Vector2 tile = channel2 ? uv2PreviewTile : uvPreviewTile;
        if (Mathf.Abs(tile.x) < 1e-4f) tile.x = 1e-4f;
        if (Mathf.Abs(tile.y) < 1e-4f) tile.y = 1e-4f;
        var cur = checkerMat.GetVector("_PreviewTile");
        if (!Mathf.Approximately(cur.x, tile.x) || !Mathf.Approximately(cur.y, tile.y))
        {
            checkerMat.SetVector("_PreviewTile", new Vector4(tile.x, tile.y, 0f, 0f));
            SceneView.RepaintAll();
        }
    }

    // 预览通道当前是否为 UV2（面板缩略图与预览材质保持一致）
    bool PreviewIsChannel2() => uv2Enabled && checkerMat != null && checkerMat.GetFloat("_UVChannel") > 0.5f;

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
        if (GUILayout.Button("打开破碎工具")) VFXFractureWindow.Open();
        if (GUILayout.Button("打开地刺工具")) VFXGroundSpikeWindow.Open();

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

        // Noise 顶点偏移
        noiseFoldout = EditorGUILayout.Foldout(noiseFoldout, "Noise 形变", true, EditorStyles.foldoutHeader);
        if (noiseFoldout)
        {
            noiseEnabled = EditorGUILayout.Toggle("启用 Noise", noiseEnabled);
            if (noiseEnabled)
            {
                noiseTex = (Texture2D)EditorGUILayout.ObjectField("噪声贴图", noiseTex, typeof(Texture2D), false);
                noiseStrength = EditorGUILayout.Slider("偏移强度", noiseStrength, 0f, 2f);
                noiseTiling   = EditorGUILayout.Vector2Field("UV 平铺 (Tiling)", noiseTiling);
                noiseOffset   = EditorGUILayout.Vector2Field("UV 偏移 (Offset)", noiseOffset);
                noiseAxis     = (NoiseAxis)EditorGUILayout.Popup("偏移方向", (int)noiseAxis, NoiseAxisLabels);
                if (noiseAxis == NoiseAxis.Custom)
                    noiseAxisCustom = EditorGUILayout.Vector3Field("自定义方向", noiseAxisCustom);

                // 强度遮罩（Noise 专用，与顶点色解耦）
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("强度遮罩", EditorStyles.miniBoldLabel);
                noiseMaskUVEnabled = EditorGUILayout.Toggle(new GUIContent("UV 渐变遮罩", "按 UV 到边缘的距离衰减 Noise 强度（独立于顶点色）"), noiseMaskUVEnabled);
                if (noiseMaskUVEnabled)
                {
                    noiseMaskUVFalloffU = EditorGUILayout.Slider("U 向渐变", noiseMaskUVFalloffU, 0f, 1f);
                    noiseMaskUVFalloffV = EditorGUILayout.Slider("V 向渐变", noiseMaskUVFalloffV, 0f, 1f);
                }
                noiseMaskHeightEnabled = EditorGUILayout.Toggle(new GUIContent("高度渐变遮罩", "按归一化高度用曲线衰减 Noise 强度（曲线值即遮罩 0~1）"), noiseMaskHeightEnabled);
                if (noiseMaskHeightEnabled)
                    noiseMaskHeightCurve = EditorGUILayout.CurveField("高度遮罩曲线", noiseMaskHeightCurve);
            }
        }

        // MeshType 切换时额外重置 pivotOffset
        var prevType = meshType;
        meshType = (MeshType)EditorGUILayout.EnumPopup("Mesh Type", meshType);
        if (meshType != prevType) { pivotOffset = Vector3.zero; dirty = true; }

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
            uTiling  = SliderWithMax(new GUIContent("U Tiling",  "会烘焙进模型 UV。右侧 max 可自定义滑条上限"), uTiling, 0.01f, ref uvTileMax);
            vTiling  = SliderWithMax(new GUIContent("V Tiling",  "会烘焙进模型 UV。右侧 max 可自定义滑条上限"), vTiling, 0.01f, ref uvTileMax);
            EditorGUILayout.BeginHorizontal();
            flipU = EditorGUILayout.ToggleLeft("翻转 U", flipU, GUILayout.Width(80));
            flipV = EditorGUILayout.ToggleLeft("翻转 V", flipV, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            uvPreviewSpeed = EditorGUILayout.Vector2Field(new GUIContent("预览流速 U/V", "仅在 Scene 预览里滚动 UV 观察流动方向，不会烘焙进模型"), uvPreviewSpeed);
            uvPreviewTile  = EditorGUILayout.Vector2Field(new GUIContent("预览平铺 U/V", "仅预览：在 Scene 与下方缩略图里额外平铺贴图，用来试看密度；不会烘焙进模型 UV（与 Noise 的 UV 平铺一样自由输入，无上限）"), uvPreviewTile);

            // Ring：矩形 / 圆形 / 极坐标；Plane：矩形 / 极坐标（圆形对平面无意义，已移除）
            if (meshType == MeshType.Ring)
                ringUVMode = (RingUVMode)EditorGUILayout.Popup("UV 模式", (int)ringUVMode, RingUVModeLabels);
            else if (meshType == MeshType.Plane)
                planeUVMode = PlaneUVModePopup("UV 模式", planeUVMode);

            EditorGUI.BeginChangeCheck();
            previewCullBack = EditorGUILayout.Toggle(new GUIContent("预览剔除背面", "仅预览：隐藏背面。Noise/高度形变造成翻折时背面会露出（默认蓝色），勾选后剔除"), previewCullBack);
            if (EditorGUI.EndChangeCheck() && checkerMat != null)
            {
                checkerMat.SetFloat("_Cull", previewCullBack ? 2f : 0f);   // 2=Back, 0=Off
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(2);
            uv2Enabled = EditorGUILayout.Toggle("启用 UV2", uv2Enabled);
            if (uv2Enabled)
            {
                uv2Rotate  = EditorGUILayout.Slider("UV2 Rotate", uv2Rotate, 0f, 360f);
                uv2UTiling = SliderWithMax(new GUIContent("UV2 U Tiling", "会烘焙进模型 UV2。右侧 max 可自定义滑条上限"), uv2UTiling, 0.01f, ref uvTileMax);
                uv2VTiling = SliderWithMax(new GUIContent("UV2 V Tiling", "会烘焙进模型 UV2。右侧 max 可自定义滑条上限"), uv2VTiling, 0.01f, ref uvTileMax);
                EditorGUILayout.BeginHorizontal();
                flipU2 = EditorGUILayout.ToggleLeft("翻转 U2", flipU2, GUILayout.Width(90));
                flipV2 = EditorGUILayout.ToggleLeft("翻转 V2", flipV2, GUILayout.Width(90));
                EditorGUILayout.EndHorizontal();
                uv2PreviewSpeed = EditorGUILayout.Vector2Field(new GUIContent("UV2 预览流速 U/V", "仅预览滚动，不烘焙"), uv2PreviewSpeed);
                uv2PreviewTile  = EditorGUILayout.Vector2Field(new GUIContent("UV2 预览平铺 U/V", "仅预览平铺，不烘焙"), uv2PreviewTile);

                // UV2 独立的 Ring/Plane UV 模式
                if (meshType == MeshType.Ring)
                    ringUVMode2 = (RingUVMode)EditorGUILayout.Popup("UV2 模式", (int)ringUVMode2, RingUVModeLabels);
                else if (meshType == MeshType.Plane)
                    planeUVMode2 = PlaneUVModePopup("UV2 模式", planeUVMode2);
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

            // UV Tiling 平铺预览（所有面片类型通用）
            EditorGUILayout.Space(2);
            uvTilePreview = EditorGUILayout.Toggle(new GUIContent("显示平铺预览", "按当前 Tiling × 预览平铺 铺贴预览贴图/棋盘格，直观看到平铺密度与流动方向"), uvTilePreview);
            if (uvTilePreview) DrawUVTilePreview();
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

    // 滑条 + 右侧「max」输入框：上限随时可改，不再被硬编码卡死。
    // 上限调小时会把当前值一起夹回范围内，避免出现滑条外的隐形数值。
    static float SliderWithMax(GUIContent label, float value, float min, ref float max, float maxFloor = 0.01f)
    {
        max = Mathf.Max(maxFloor, Mathf.Max(min + maxFloor, max));
        EditorGUILayout.BeginHorizontal();
        value = EditorGUILayout.Slider(label, Mathf.Clamp(value, min, max), min, max);
        GUILayout.Label("max", GUILayout.Width(26));
        max = Mathf.Max(maxFloor, Mathf.Max(min + maxFloor, EditorGUILayout.FloatField(max, GUILayout.Width(46))));
        EditorGUILayout.EndHorizontal();
        return Mathf.Clamp(value, min, max);
    }

    static float SliderWithMax(string label, float value, float min, ref float max, float maxFloor = 0.01f)
        => SliderWithMax(new GUIContent(label), value, min, ref max, maxFloor);

    // UV Tiling 平铺预览：按「Tiling × 预览平铺」把预览贴图（或棋盘格）铺贴在缩略图里，
    // 偏移跟随预览流速一起滚动 —— 不进 Scene 也能先看清平铺密度和流动方向。
    void DrawUVTilePreview()
    {
        Texture tex = (uvPreviewTex != null) ? (Texture)uvPreviewTex : checkerTex;
        if (tex == null) return;

        bool ch2 = PreviewIsChannel2();
        float tu = (ch2 ? uv2UTiling : uTiling) * (ch2 ? uv2PreviewTile.x : uvPreviewTile.x);
        float tv = (ch2 ? uv2VTiling : vTiling) * (ch2 ? uv2PreviewTile.y : uvPreviewTile.y);
        if (Mathf.Abs(tu) < 1e-4f) tu = 1e-4f;
        if (Mathf.Abs(tv) < 1e-4f) tv = 1e-4f;

        const float SZ = 148f;
        Rect box = GUILayoutUtility.GetRect(SZ, SZ, GUILayout.ExpandWidth(false));
        box.x = Mathf.Max(4f, (EditorGUIUtility.currentViewWidth - SZ) * 0.5f);
        box.width = SZ; box.height = SZ;
        EditorGUI.DrawRect(new Rect(box.x - 1, box.y - 1, SZ + 2, SZ + 2), new Color(0f, 0f, 0f, 0.6f));
        // texcoord = 滚动偏移 + uv × 平铺，与预览 Shader 完全一致
        GUI.DrawTextureWithTexCoords(box, tex, new Rect(_uvScrollAccum.x, _uvScrollAccum.y, tu, tv));

        var info = $"{(ch2 ? "UV2" : "UV1")}  平铺 {tu:0.##} × {tv:0.##}"
                 + (Mathf.Abs(ch2 ? uv2Rotate : uvRotate) > 0.01f ? "（缩略图不含 Rotate）" : "");
        EditorGUILayout.LabelField(info, EditorStyles.centeredGreyMiniLabel);
    }

    // 控制点编辑工具条（Spline / Custom 共用）：移动/旋转/缩放 × 世界/本地坐标
    void DrawPointToolBar()
    {
        float lw = Mathf.Max(60f, EditorGUIUtility.labelWidth - 4f);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(new GUIContent("控制点工具", "移动 / 旋转 / 缩放选中的控制点（绕选集质心）"), GUILayout.Width(lw));
        pointTool = (PointTool)GUILayout.Toolbar((int)pointTool, PointToolLabels, EditorStyles.miniButton);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(new GUIContent("坐标空间", "世界坐标 = Gizmo 轴向对齐世界轴；本地坐标 = 对齐预览模型自身朝向"), GUILayout.Width(lw));
        pointSpace = (HandleSpace)GUILayout.Toolbar((int)pointSpace, HandleSpaceLabels, EditorStyles.miniButton);
        EditorGUILayout.EndHorizontal();
    }

    // 选中控制点的坐标数值编辑：本地坐标 + 世界坐标双向可改
    void DrawPointCoordFields(List<Vector3> pts, int idx, string tag)
    {
        if (previewGO == null || pts == null || idx < 0 || idx >= pts.Count) return;
        var t = previewGO.transform;

        EditorGUI.BeginChangeCheck();
        Vector3 lp = EditorGUILayout.Vector3Field($"{tag}{idx} 本地坐标", pts[idx]);
        if (EditorGUI.EndChangeCheck()) { pts[idx] = lp; dirty = true; }

        EditorGUI.BeginChangeCheck();
        Vector3 wp = EditorGUILayout.Vector3Field($"{tag}{idx} 世界坐标", t.TransformPoint(pts[idx]));
        if (EditorGUI.EndChangeCheck()) { pts[idx] = t.InverseTransformPoint(wp); dirty = true; }
    }

    void SelectSplinePath(int index)
    {
        if (splinePaths == null || index < 0 || index >= splinePaths.Count) return;
        if (activeSplineIndex == index) return;
        activeSplineIndex = index;
        selectedSplinePoint = -1;
        selectedPoints.Clear();
        isBoxSelecting = false;
        _ptStateHash = 0;
        Repaint();
        SceneView.RepaintAll();
    }

    void AddSplinePath()
    {
        var source = ActiveSpline;
        SyncSplineFlips(source);
        var next = new SplinePath
        {
            points = new List<Vector3>(source.points.Count),
            flips = new List<float>(source.flips)
        };
        // 从当前路径复制形状并在本地 Z 错开，便于直接编辑第二条路径。
        foreach (var point in source.points)
            next.points.Add(point + Vector3.forward * Mathf.Max(1f, splineW * 2f));
        splinePaths.Add(next);
        SelectSplinePath(splinePaths.Count - 1);
        dirty = true;
    }

    // Spline 的位置坐标系只影响数值输入；切换时自动换算显示值，控制点世界位置不变。
    void DrawSplinePositionField(int index)
    {
        if (previewGO == null || index < 0 || index >= splinePoints.Count) return;
        var t = previewGO.transform;
        Vector3 value = splinePositionSpace == SplinePositionSpace.World
            ? t.TransformPoint(splinePoints[index]) : splinePoints[index];
        EditorGUI.BeginChangeCheck();
        value = EditorGUILayout.Vector3Field($"P{index} 位置", value);
        if (EditorGUI.EndChangeCheck())
        {
            splinePoints[index] = splinePositionSpace == SplinePositionSpace.World
                ? t.InverseTransformPoint(value) : value;
            dirty = true;
            SceneView.RepaintAll();
        }
    }

    // 高度点内部保存相对高度；实际 Scene/数值位置叠加边界基准面。
    Vector3 CustomHeightPointActualLocal(int index)
    {
        Vector3 hp = customHeightPoints[index];
        return new Vector3(hp.x, hp.y + EvalCustomBoundaryYSafe(hp.x, hp.z), hp.z);
    }

    void SetCustomHeightPointFromActualLocal(int index, Vector3 local)
    {
        customHeightPoints[index] = new Vector3(local.x,
            local.y - EvalCustomBoundaryYSafe(local.x, local.z), local.z);
        dirty = true;
    }

    // 高度点的 Y 保存为相对边界曲面的偏移；位置框显示实际坐标。
    void DrawCustomHeightCoordFields(int index)
    {
        if (previewGO == null || index < 0 || index >= customHeightPoints.Count) return;
        var t = previewGO.transform;
        Vector3 actualLocal = CustomHeightPointActualLocal(index);

        EditorGUI.BeginChangeCheck();
        Vector3 localInput = EditorGUILayout.Vector3Field($"H{index} 本地位置", actualLocal);
        if (EditorGUI.EndChangeCheck())
        {
            SetCustomHeightPointFromActualLocal(index, localInput);
            actualLocal = localInput;
        }

        EditorGUI.BeginChangeCheck();
        Vector3 worldInput = EditorGUILayout.Vector3Field($"H{index} 世界位置", t.TransformPoint(actualLocal));
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 local = t.InverseTransformPoint(worldInput);
            SetCustomHeightPointFromActualLocal(index, local);
        }
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
                ringRadiusCurve = EditorGUILayout.CurveField("半径曲线(上→下)", ringRadiusCurve);
                ringHeightCurve = EditorGUILayout.CurveField("高度曲线(上→下)", ringHeightCurve);
                ringTiltX  = EditorGUILayout.Slider("倾斜 X",     ringTiltX,  -45f, 45f);
                ringTiltZ  = EditorGUILayout.Slider("倾斜 Z",     ringTiltZ,  -45f, 45f);
                ringIsCross = EditorGUILayout.Toggle("是否为十字片", ringIsCross);
                if (ringIsCross)
                    ringCrossW = SliderWithMax(new GUIContent("十字面宽度(×)",
                        "新增的那片十字面的宽度倍数。1 = 与环面斜边等宽；右侧 max 可自定义滑条上限"),
                        ringCrossW, 0f, ref ringCrossWMax);
                break;

            case MeshType.Cross:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Cross", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                crossW     = EditorGUILayout.Slider("宽度",     crossW,     0.01f, 10);
                crossH     = EditorGUILayout.Slider("高度",     crossH,     0.01f, 10);
                crossCount = EditorGUILayout.IntSlider("交叉片数", crossCount, 2, 6);
                if (crossCount > 1)
                    crossExtraW = SliderWithMax(new GUIContent("十字面宽度(×)",
                        "第 2 片起（新增的十字面）的宽度倍数，1 = 与第 1 片等宽；右侧 max 可自定义滑条上限"),
                        crossExtraW, 0f, ref crossExtraWMax);
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
                if (helixIsCross)
                    helixCrossW = SliderWithMax(new GUIContent("十字面宽度(×)",
                        "新增的那片十字面的宽度倍数。1 = 与主面等宽；右侧 max 可自定义滑条上限"),
                        helixCrossW, 0f, ref helixCrossWMax);
                helixCurve   = EditorGUILayout.CurveField("细节调整",  helixCurve);
                helixYProfileCurve = EditorGUILayout.CurveField(
                    new GUIContent("沿 Y 轮廓缩放", "横轴=模型高度（底部到顶部），纵轴=整个模型 XZ 截面缩放倍数；中间调低可收细腰部"),
                    helixYProfileCurve);
                helixHeadTwist = EditorGUILayout.Slider("头部扭曲", helixHeadTwist, -360f, 360f);
                helixTailTwist = EditorGUILayout.Slider("尾部扭曲", helixTailTwist, -360f, 360f);
                break;

            case MeshType.Spline:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Spline", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                DrawSplinePresetControls();
                var currentPath = ActiveSpline;
                var pathLabels = new string[splinePaths.Count];
                for (int pathIndex = 0; pathIndex < pathLabels.Length; pathIndex++)
                    pathLabels[pathIndex] = $"Spline {pathIndex + 1}";
                int selectedPath = EditorGUILayout.Popup("当前路径", activeSplineIndex, pathLabels);
                if (selectedPath != activeSplineIndex) SelectSplinePath(selectedPath);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ 新增 Spline")) AddSplinePath();
                GUI.enabled = splinePaths.Count > 1;
                if (GUILayout.Button("- 删除当前 Spline"))
                {
                    splinePaths.RemoveAt(activeSplineIndex);
                    activeSplineIndex = Mathf.Clamp(activeSplineIndex, 0, splinePaths.Count - 1);
                    selectedSplinePoint = -1;
                    selectedPoints.Clear();
                    _ptStateHash = 0;
                    dirty = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField($"总路径: {splinePaths.Count} 条（全部合并成一个 Mesh）", EditorStyles.miniLabel);
                splineShape      = (SplineShape)EditorGUILayout.Popup("形态", (int)splineShape, SplineShapeLabels);
                if (splineShape == SplineShape.Tube)
                {
                    splineTubeSides = EditorGUILayout.IntSlider("截面边数", splineTubeSides, 3, 32);
                    splineCols      = EditorGUILayout.IntSlider("路径面数", splineCols, 4, 256);
                    splineW         = SliderWithMax(new GUIContent("管半径(宽度)", "右侧 max 可自定义滑条上限，不再卡在 5"), splineW, 0.01f, ref splineWMax);
                }
                else
                {
                    splineRows   = EditorGUILayout.IntSlider("横截面数", splineRows, 1, 20);
                    splineCols   = EditorGUILayout.IntSlider("路径面数", splineCols, 4, 256);
                    splineW      = SliderWithMax(new GUIContent("宽度", "右侧 max 可自定义滑条上限，不再卡在 5"), splineW, 0.01f, ref splineWMax);
                    if (splineShape == SplineShape.Cross)
                        splineCrossW = SliderWithMax(new GUIContent("十字面宽度(×)",
                            "十字形态里新增的第二片的宽度倍数。1 = 与主片等宽；右侧 max 可自定义滑条上限"),
                            splineCrossW, 0f, ref splineCrossWMax);
                }
                splineTwist      = EditorGUILayout.Slider("扭曲",        splineTwist,   -360f, 360f);
                splineWidthCurve = EditorGUILayout.CurveField("宽度曲线", splineWidthCurve);
                EditorGUILayout.Space(2);
                DrawPointToolBar();
                splinePositionSpace = (SplinePositionSpace)EditorGUILayout.Popup(
                    new GUIContent("点位置坐标系", "切换 Local / World Position 后，位置数值自动换算，场景中的点不会跳动"),
                    (int)splinePositionSpace, SplinePositionLabels);
                splineSoftSelect = EditorGUILayout.Toggle("软选", splineSoftSelect);
                if (splineSoftSelect)
                {
                    splineSoftRadius = EditorGUILayout.Slider("软选半径", splineSoftRadius, 0.1f, 10f);
                    splineSoftCurve  = EditorGUILayout.CurveField("软选衰减", splineSoftCurve);
                }
                EditorGUILayout.Space(4);

                // 选中点的翻转控制 + 坐标数值编辑（本地 / 世界）
                SyncSplineFlips(ActiveSpline);
                if (selectedSplinePoint >= 0 && selectedSplinePoint < splinePoints.Count)
                {
                    EditorGUI.BeginChangeCheck();
                    splinePointFlips[selectedSplinePoint] = EditorGUILayout.Slider(
                        $"P{selectedSplinePoint} 翻转", splinePointFlips[selectedSplinePoint], -180f, 180f);
                    if (EditorGUI.EndChangeCheck()) dirty = true;
                    DrawSplinePositionField(selectedSplinePoint);
                }

                EditorGUILayout.HelpBox("可添加多条 Spline 合并为一个模型；点击其它路径的控制点即可切换当前路径。\nShift+左键插入点，框选多个点，右键删除。\n「坐标空间」控制 Gizmo 轴向；「点位置坐标系」控制位置数值的 Local/World 换算。", MessageType.Info);
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
                if (GUILayout.Button("重置当前 Spline"))
                {
                    splinePaths[activeSplineIndex] = new SplinePath();
                    selectedSplinePoint = -1;
                    selectedPoints.Clear();
                    _ptStateHash = 0;
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
                    if (trailCross)
                        trailCrossW = SliderWithMax(new GUIContent("十字面宽度(×)",
                            "新增的那片十字面的宽度倍数。1 = 与主片等宽；右侧 max 可自定义滑条上限"),
                            trailCrossW, 0f, ref trailCrossWMax);

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

            case MeshType.Custom:
                meshTypeFoldout = EditorGUILayout.Foldout(meshTypeFoldout, "Custom 自定义", true, EditorStyles.foldoutHeader);
                if (!meshTypeFoldout) break;
                EditorGUILayout.HelpBox("Scene 视图中拖动绿色控制点编辑多边形边界\n按住 Shift+左键 在最近的边上插入新点\n空白处拖矩形框选多个控制点\n至少 3 个点才能生成面\n控制点沿 Y 上下拖动即整片跟着起伏（下方「边界高度插值」）", MessageType.Info);
                customFillDensity = EditorGUILayout.IntSlider(new GUIContent("填充密度", "同时决定高度过渡的平滑度：密度越高，边界起伏的插值越顺"), customFillDensity, 1, 32);
                customBoundaryHeight = EditorGUILayout.Toggle(new GUIContent("边界高度插值",
                    "开启：边界控制点的 Y 用均值坐标插值到面内每个顶点，拖动控制点上下即把平面拉成起伏曲面。\n关闭：忽略边界点 Y，强制生成平面（旧行为）"), customBoundaryHeight);
                customIrregularEdge = EditorGUILayout.Toggle(
                    new GUIContent("不规则边缘", "沿每条边插入细分点并做平面内扰动；原始绿色控制点保持不动"), customIrregularEdge);
                if (customIrregularEdge)
                {
                    customEdgeAmplitude = SliderWithMax(new GUIContent("边缘起伏幅度",
                        "边缘在 XZ 平面的最大偏移；太大时自动缩小以避免边界自交"), customEdgeAmplitude, 0f, ref customEdgeAmplitudeMax);
                    customEdgeSegments = EditorGUILayout.IntSlider("每边细分数", customEdgeSegments, 2, 24);
                    customEdgeFrequency = EditorGUILayout.Slider("起伏频率", customEdgeFrequency, 0.25f, 10f);
                    customEdgeSeed = EditorGUILayout.IntField("随机种子", customEdgeSeed);
                }
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("UV 流动设置", EditorStyles.miniBoldLabel);
                customUVFlowDir   = (CustomUVFlowDir)EditorGUILayout.Popup("流动方向", (int)customUVFlowDir, CustomUVFlowDirLabels);
                customFlipUV      = EditorGUILayout.Toggle("翻转 UV", customFlipUV);
                EditorGUILayout.Space(4);
                DrawPointToolBar();
                customSoftSelect  = EditorGUILayout.Toggle("软选", customSoftSelect);
                if (customSoftSelect)
                {
                    customSoftRadius = EditorGUILayout.Slider("软选半径", customSoftRadius, 0.1f, 10f);
                    customSoftCurve  = EditorGUILayout.CurveField("软选衰减", customSoftCurve);
                }
                if (customSelectedPoint >= 0 && customSelectedPoint < customPoints.Count)
                {
                    EditorGUILayout.Space(2);
                    DrawPointCoordFields(customPoints, customSelectedPoint, "P");
                }
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ 添加点"))
                {
                    var last = customPoints[customPoints.Count - 1];
                    customPoints.Add(last + Vector3.right * 1f);
                    dirty = true;
                }
                GUI.enabled = customPoints.Count > 3;
                if (GUILayout.Button("- 删除选中点") && customSelectedPoint >= 0)
                {
                    customPoints.RemoveAt(customSelectedPoint);
                    customSelectedPoint = -1;
                    customSelectedPoints.Clear();
                    dirty = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("重置为矩形"))
                {
                    customPoints = new List<Vector3>
                    {
                        new Vector3(-1f, 0f, -1f),
                        new Vector3( 1f, 0f, -1f),
                        new Vector3( 1f, 0f,  1f),
                        new Vector3(-1f, 0f,  1f),
                    };
                    customSelectedPoint = -1;
                    customSelectedPoints.Clear();
                    dirty = true;
                }
                if (GUILayout.Button("边界高度归零（压平）"))
                {
                    for (int i = 0; i < customPoints.Count; i++)
                        customPoints[i] = new Vector3(customPoints[i].x, 0f, customPoints[i].z);
                    dirty = true;
                }
                EditorGUILayout.LabelField($"控制点: {customPoints.Count} 个" + (customSelectedPoints.Count > 0 ? $" | 选中: {customSelectedPoints.Count}" : ""), EditorStyles.miniLabel);

                // ── 高度控制点（立体化）──
                EditorGUILayout.Space(6);
                customHeightFoldout = EditorGUILayout.Foldout(customHeightFoldout, "立体高度控制", true);
                if (customHeightFoldout)
                {
                    customHeightEnabled = EditorGUILayout.Toggle("启用高度形变", customHeightEnabled);
                    if (customHeightEnabled)
                    {
                        EditorGUILayout.HelpBox("在面内添加高度控制点，Scene 中拖动即可上下抬升，把平面雕成立体面\n每个点按影响半径 + 衰减曲线平滑影响周围顶点", MessageType.Info);
                        customHeightFalloff = EditorGUILayout.CurveField("高度衰减", customHeightFalloff);

                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button("+ 添加高度点"))
                        {
                            Vector3 center = Vector3.zero;
                            foreach (var p in customPoints) center += p;
                            if (customPoints.Count > 0) center /= customPoints.Count;
                            customHeightPoints.Add(new Vector3(center.x, 0.5f, center.z));
                            customHeightRadii.Add(1.5f);
                            customSelectedHeightPoint = customHeightPoints.Count - 1;
                            dirty = true;
                        }
                        GUI.enabled = customSelectedHeightPoint >= 0;
                        if (GUILayout.Button("- 删除选中高度点"))
                        {
                            customHeightPoints.RemoveAt(customSelectedHeightPoint);
                            customHeightRadii.RemoveAt(customSelectedHeightPoint);
                            customSelectedHeightPoint = -1;
                            dirty = true;
                        }
                        GUI.enabled = true;
                        EditorGUILayout.EndHorizontal();

                        if (customSelectedHeightPoint >= 0 && customSelectedHeightPoint < customHeightPoints.Count)
                        {
                            // 两个列表一一对应，防御性补齐，避免历史数据不齐时越界
                            while (customHeightRadii.Count < customHeightPoints.Count) customHeightRadii.Add(1.5f);
                            while (customHeightRadii.Count > customHeightPoints.Count) customHeightRadii.RemoveAt(customHeightRadii.Count - 1);

                            // 上限自适应：Scene 里把点拖得比上限还高时自动抬高上限，
                            // 否则滑条会把值默默夹回 ±5，看起来就像「拖不动」
                            Vector3 hp = customHeightPoints[customSelectedHeightPoint];
                            customHeightMax = Mathf.Max(customHeightMax, Mathf.Abs(hp.y));
                            customHeightRadiusMax = Mathf.Max(customHeightRadiusMax, customHeightRadii[customSelectedHeightPoint]);

                            float newH = SliderWithMax(new GUIContent("高度", "右侧 max 可自定义上下限（±max）"), hp.y, -customHeightMax, ref customHeightMax);
                            float newR = SliderWithMax(new GUIContent("影响半径", "右侧 max 可自定义滑条上限"), customHeightRadii[customSelectedHeightPoint], 0.01f, ref customHeightRadiusMax);
                            if (!Mathf.Approximately(newH, hp.y))
                            {
                                customHeightPoints[customSelectedHeightPoint] = new Vector3(hp.x, newH, hp.z);
                                dirty = true;
                            }
                            if (!Mathf.Approximately(newR, customHeightRadii[customSelectedHeightPoint]))
                            {
                                customHeightRadii[customSelectedHeightPoint] = newR;
                                dirty = true;
                            }
                            DrawCustomHeightCoordFields(customSelectedHeightPoint);
                        }
                        EditorGUILayout.LabelField($"高度点: {customHeightPoints.Count} 个" + (customSelectedHeightPoint >= 0 ? $" | 选中 #{customSelectedHeightPoint}" : ""), EditorStyles.miniLabel);
                    }
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

        // Spline / Custom 模式下，未选中控制点时才画 Transform Handle，避免冲突
        bool hasSelectedPoint = (meshType == MeshType.Spline && selectedPoints.Count > 0)
                             || (meshType == MeshType.Custom && customSelectedPoints.Count > 0)
                             || (meshType == MeshType.Custom && customHeightEnabled && customSelectedHeightPoint >= 0);
        if (!hasSelectedPoint)
            DrawPreviewTransformHandle();

        if (meshType == MeshType.Spline)
            DrawSplineHandles();

        if (meshType == MeshType.Custom)
            DrawCustomHandles();
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

        // 所有路径都显示控制点；点击非当前路径的点时立即切换编辑对象。
        for (int pathIndex = 0; pathIndex < splinePaths.Count; pathIndex++)
        {
            var path = splinePaths[pathIndex];
            if (path == null || path.points == null) continue;
            for (int i = 0; i < path.points.Count; i++)
            {
                Vector3 wp = t.TransformPoint(path.points[i]);
                float sz = HandleUtility.GetHandleSize(wp) * 0.12f;
                bool isActive = pathIndex == activeSplineIndex;
                Handles.color = isActive && selectedPoints.Contains(i) ? Color.yellow
                              : isActive ? Color.green : new Color(0.2f, 0.8f, 1f, 0.75f);
                if (Handles.Button(wp, Quaternion.identity, sz, sz * 1.2f, Handles.SphereHandleCap))
                {
                    SelectSplinePath(pathIndex);
                    selectedPoints.Clear();
                    selectedPoints.Add(i);
                    selectedSplinePoint = i;
                    Repaint();
                }
            }
        }

        // 多点整体变换 Handle（选集质心）：移动 / 旋转 / 缩放 × 世界/本地坐标 + 软选衰减
        if (selectedPoints.Count > 0)
            DoPointsTransformHandle(t, splinePoints, selectedPoints,
                                    splineSoftSelect, splineSoftRadius, splineSoftCurve);

        // 当前路径为绿色，其他路径为蓝色；预览 Mesh 始终包含全部路径。
        for (int pathIndex = 0; pathIndex < splinePaths.Count; pathIndex++)
        {
            var path = splinePaths[pathIndex];
            if (path == null || path.points == null || path.points.Count < 2) continue;
            Handles.color = pathIndex == activeSplineIndex
                ? new Color(0.3f, 1f, 0.3f, 0.8f) : new Color(0.2f, 0.8f, 1f, 0.55f);
            int sampleCount = path.points.Count * 10;
            Vector3 prev = t.TransformPoint(CatmullRom(path.points, 0));
            for (int sample = 1; sample <= sampleCount; sample++)
            {
                float st = (float)sample / sampleCount * (path.points.Count - 1);
                Vector3 cur = t.TransformPoint(CatmullRom(path.points, st));
                Handles.DrawLine(prev, cur);
                prev = cur;
            }
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
                splinePointFlips.Add(0f);
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
        foreach (var path in splinePaths)
        {
            if (path == null || path.points == null) continue;
            foreach (var point in path.points)
            {
                Vector2 gp = HandleUtility.WorldToGUIPoint(t.TransformPoint(point));
                if (Vector2.Distance(gp, mouse) < 12f) return true;
            }
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
        checkerMat.SetFloat("_Cull", previewCullBack ? 2f : 0f);
        checkerMat.SetVector("_PreviewTile", new Vector4(uvPreviewTile.x, uvPreviewTile.y, 0f, 0f));

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
            case MeshType.Custom:    mesh = BuildCustom();    break;
            default:              mesh = new Mesh();    break;
        }

        ApplyUV(mesh);

        // Noise 顶点偏移（在 UV 之后、顶点色之前，可用 UV 采样噪声）
        if (noiseEnabled) ApplyNoise(mesh);

        // 顶点色：边缘渐变 + 高度渐变
        bool useVertColor = edgeTransp || heightGradient;
        if (useVertColor) ApplyVertexColors(mesh);

        // 正反面翻转：除 Plane/Spline/Custom 外默认需要翻转，flipNormals 再次切换
        bool needFlip = (meshType != MeshType.Plane && meshType != MeshType.Spline && meshType != MeshType.Custom);
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

        RingUVMode mode1 = (meshType == MeshType.Ring) ? ringUVMode
                         : (meshType == MeshType.Plane) ? planeUVMode : RingUVMode.Rect;
        RingUVMode mode2 = !uv2Enabled ? RingUVMode.Rect
                         : (meshType == MeshType.Ring) ? ringUVMode2
                         : (meshType == MeshType.Plane) ? planeUVMode2 : RingUVMode.Rect;

        bool polar1 = (mode1 == RingUVMode.Polar);
        bool polar2 = uv2Enabled && (mode2 == RingUVMode.Polar);

        // 极坐标基础（角度,半径）：Plane 用"扭曲前的原始网格"算角度，
        // 这样贴图钉在原始布局上、随顶点一起被扭曲成螺旋（流光跟随扭曲形状）；
        // Ring 用顶点位置。
        Vector2[] polarBase = null;
        if (polar1 || polar2)
            polarBase = (meshType == MeshType.Plane) ? PlanePolarUV() : PolarUV(mesh);

        Vector2[] base1 = polar1 ? polarBase : GetProjectedUV(mesh, mode1, rectUV);
        Vector2[] base2 = uv2Enabled ? (polar2 ? polarBase : GetProjectedUV(mesh, mode2, rectUV)) : null;

        // 极坐标接缝 + 中心奇点修复：复制顶点、同步扩展 base1/base2（会增加 mesh 顶点数）
        if (polarBase != null)
            FixPolarMesh(mesh, polarBase, ref base1, polar1, ref base2, polar2);

        // 变换后写入两个通道（UV2 先，UV1 后）
        if (uv2Enabled && base2 != null)
            mesh.SetUVs(1, new List<Vector2>(TransformUV(base2, uv2Rotate, uv2UTiling, uv2VTiling, flipU2, flipV2)));
        mesh.uv = TransformUV(base1, uvRotate, uTiling, vTiling, flipU, flipV);
    }

    // Plane 极坐标 UV：用扭曲前的规则网格坐标算角度/半径（与顶点绘制顺序一致），
    // 因此扭曲只旋转几何、不改角度基准 → 贴图随几何一起旋成螺旋。
    Vector2[] PlanePolarUV()
    {
        int rows = planeRows, cols = planeCols;
        float maxR = 0.5f * Mathf.Sqrt(planeW * planeW + planeH * planeH);
        maxR = Mathf.Max(maxR, 0.0001f);
        var uv = new Vector2[(rows + 1) * (cols + 1)];
        int idx = 0;
        for (int r = 0; r <= rows; r++)
        for (int c = 0; c <= cols; c++)
        {
            float x0 = ((float)c / cols - 0.5f) * planeW;
            float z0 = ((float)r / rows - 0.5f) * planeH;
            float ang = Mathf.Atan2(z0, x0) / (2f * Mathf.PI) + 0.5f;
            float rad = Mathf.Sqrt(x0 * x0 + z0 * z0) / maxR;
            uv[idx++] = new Vector2(ang, rad);
        }
        return uv;
    }

    // 对 Plane/Ring 网格做极坐标接缝 + 中心奇点修复（原理同 Custom 的 FixPolarSeam）：
    // 复制跨接缝/触及中心的三角形顶点使其独享 UV，并同步扩展两个通道的基础 UV 数组。
    void FixPolarMesh(Mesh mesh, Vector2[] polar, ref Vector2[] base1, bool polar1, ref Vector2[] base2, bool polar2)
    {
        var verts = new List<Vector3>(mesh.vertices);
        int[] tris = mesh.triangles;
        var pol = new List<Vector2>(polar);
        var b1  = new List<Vector2>(base1);
        var b2  = (base2 != null) ? new List<Vector2>(base2) : null;
        const float eps = 0.1f;

        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            float ua = pol[a].x, ub = pol[b].x, uc = pol[c].x;
            float va = pol[a].y, vb = pol[b].y, vc = pol[c].y;
            float span = Mathf.Max(ua, Mathf.Max(ub, uc)) - Mathf.Min(ua, Mathf.Min(ub, uc));
            bool center = va < eps || vb < eps || vc < eps;
            if (span <= 0.5f && !center) continue;

            float refU = (va >= vb && va >= vc) ? ua : (vb >= vc ? ub : uc);
            tris[i]     = DupPolarVert(verts, pol, b1, polar1, b2, polar2, a, refU, eps);
            tris[i + 1] = DupPolarVert(verts, pol, b1, polar1, b2, polar2, b, refU, eps);
            tris[i + 2] = DupPolarVert(verts, pol, b1, polar1, b2, polar2, c, refU, eps);
        }

        mesh.vertices = verts.ToArray();   // 先扩展顶点，再改三角形索引
        mesh.triangles = tris;
        base1 = b1.ToArray();
        if (b2 != null) base2 = b2.ToArray();
    }

    static int DupPolarVert(List<Vector3> verts, List<Vector2> pol, List<Vector2> b1, bool polar1,
                            List<Vector2> b2, bool polar2, int idx, float refU, float eps)
    {
        int ni = verts.Count;
        verts.Add(verts[idx]);
        pol.Add(pol[idx]);
        b1.Add(polar1 ? new Vector2(ResolvePolarU(pol[idx].x, pol[idx].y, refU, eps), b1[idx].y) : b1[idx]);
        if (b2 != null)
            b2.Add(polar2 ? new Vector2(ResolvePolarU(pol[idx].x, pol[idx].y, refU, eps), b2[idx].y) : b2[idx]);
        return ni;
    }

    // 按 UV 模式返回基础 UV：矩形直接用 Build* 产出的；圆形仅 Ring；极坐标 Ring / Plane
    Vector2[] GetProjectedUV(Mesh mesh, RingUVMode mode, Vector2[] rectUV)
    {
        if (mode == RingUVMode.Circular)
        {
            if (meshType == MeshType.Ring) return RingCircularUV(mesh);
        }
        else if (mode == RingUVMode.Polar)
        {
            if (meshType == MeshType.Ring || meshType == MeshType.Plane) return PolarUV(mesh);
        }
        return rectUV;
    }

    // Plane 只支持 矩形 / 极坐标 的 UV 模式下拉
    static readonly string[] PlaneUVModeLabels = { "矩形", "极坐标" };
    static RingUVMode PlaneUVModePopup(string label, RingUVMode cur)
    {
        int sel = (cur == RingUVMode.Polar) ? 1 : 0;
        sel = EditorGUILayout.Popup(label, sel, PlaneUVModeLabels);
        return (sel == 1) ? RingUVMode.Polar : RingUVMode.Rect;
    }

    // 极坐标 UV：U = 绕中心角度(0-1)，V = 归一化半径(0-1)。适合旋涡 / 冲击波 / 传送门等径向流动。
    // 注：角度在 ±180° 处有一条接缝（U 从 1 跳回 0），这是极坐标映射固有现象。
    Vector2[] PolarUV(Mesh mesh)
    {
        var v = mesh.vertices;
        float maxR = 0.0001f;
        for (int i = 0; i < v.Length; i++)
        {
            float d = Mathf.Sqrt(v[i].x * v[i].x + v[i].z * v[i].z);
            if (d > maxR) maxR = d;
        }
        var uv = new Vector2[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            float ang = Mathf.Atan2(v[i].z, v[i].x) / (2f * Mathf.PI) + 0.5f;   // 0..1
            float r   = Mathf.Sqrt(v[i].x * v[i].x + v[i].z * v[i].z) / maxR;   // 0..1
            uv[i] = new Vector2(ang, r);
        }
        return uv;
    }

    // Ring 圆形 UV：把顶点 XZ 位置直接映射到 0-1 UV 框（圆盘贴图不变形）
    // maxR 取实际顶点最大距离，兼容半径曲线放大效果
    Vector2[] RingCircularUV(Mesh mesh)
    {
        var v = mesh.vertices;
        float maxR = 0.0001f;
        for (int i = 0; i < v.Length; i++)
        {
            float d = Mathf.Sqrt(v[i].x * v[i].x + v[i].z * v[i].z);
            if (d > maxR) maxR = d;
        }
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
        uvTileMax = 10f;
        uvPreviewSpeed = Vector2.zero; uv2PreviewSpeed = Vector2.zero; _uvScrollAccum = Vector2.zero;
        uvPreviewTile = Vector2.one; uv2PreviewTile = Vector2.one; uvTilePreview = true;
        if (checkerMat != null)
        {
            checkerMat.SetVector("_Scroll", Vector4.zero);
            checkerMat.SetVector("_PreviewTile", new Vector4(1f, 1f, 0f, 0f));
        }
        flipNormals  = false;
        pivotOffset  = Vector3.zero;

        planeRows = 10; planeCols = 10; planeW = 1f; planeH = 1f;
        planeTwist = 0f; planeTwistCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        planeUVMode = RingUVMode.Rect; planeUVMode2 = RingUVMode.Circular;
        ringRows = 2; ringCols = 8; ringTopR = 0.5f; ringBotR = 1.5f;
        ringHeight = 0.5f; ringArc = 360f; ringTwist = 0f; ringIsCross = false;
        ringTwistCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        ringRadiusCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        ringHeightCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        ringTiltX = 0f; ringTiltZ = 0f;
        ringUVMode = RingUVMode.Rect; ringUVMode2 = RingUVMode.Circular;
        ringCrossW = 1f; ringCrossWMax = 5f;
        crossW = 1f; crossH = 1f; crossCount = 2;
        crossExtraW = 1f; crossExtraWMax = 5f;
        sphereR = 1f; sphereRows = 13; sphereCols = 24; sphereStyle = SphereStyle.Right;
        helixRows = 20; helixCols = 5; helixTopR = 1f; helixBotR = 0.1f;
        helixLoops = 1; helixH = 1f; helixCount = 1; helixIsCross = false;
        helixW = 0.5f; helixCurve = AnimationCurve.Linear(0, 1, 1, 1);
        helixYProfileCurve = AnimationCurve.Linear(0, 1, 1, 1);
        helixCrossW = 1f; helixCrossWMax = 5f;
        helixHeadTwist = 0f; helixTailTwist = 0f;
        splineRows = 2; splineCols = 16; splineW = 0.5f; splineWMax = 5f;
        splineCrossW = 1f; splineCrossWMax = 5f;
        splineShape = SplineShape.Strip; splineTubeSides = 8;
        splineTwist = 0f; splineWidthCurve = AnimationCurve.Linear(0, 1, 1, 1);
        lastSplinePresetPath = string.Empty;
        splinePaths = new List<SplinePath> { new SplinePath() };
        activeSplineIndex = 0;
        splinePositionSpace = SplinePositionSpace.Local;
        selectedSplinePoint = -1;
        selectedPoints.Clear();
        splineSoftSelect = false; splineSoftRadius = 1.5f;
        splineSoftCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        isBoxSelecting = false;
        hidePreview = false;
        pointTool = PointTool.Move; pointSpace = HandleSpace.World;
        _ptRot = Quaternion.identity; _ptScale = Vector3.one; _ptStateHash = 0;

        customPoints = new List<Vector3>
        {
            new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f),
            new Vector3( 1f, 0f,  1f), new Vector3(-1f, 0f, 1f),
        };
        customSelectedPoint = -1; customSelectedPoints.Clear();
        customSoftSelect = false; customSoftRadius = 1.5f;
        customSoftCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        customIsBoxSelecting = false;
        customFillDensity = 4; customUVFlowDir = CustomUVFlowDir.BoundingBox;
        customFlipUV = false; customFoldout = true;
        customHeightEnabled = false; customHeightPoints.Clear(); customHeightRadii.Clear();
        customSelectedHeightPoint = -1; customHeightFoldout = true;
        customHeightFalloff = AnimationCurve.EaseInOut(0, 1, 1, 0);
        customHeightMax = 5f; customHeightRadiusMax = 10f;
        customBoundaryHeight = true;
        customIrregularEdge = false; customEdgeAmplitude = 0.15f;
        customEdgeAmplitudeMax = 1f; customEdgeSegments = 8;
        customEdgeFrequency = 3f; customEdgeSeed = 0;

        noiseEnabled = false; noiseTex = null; noiseStrength = 0.1f;
        noiseTiling = Vector2.one; noiseOffset = Vector2.zero;
        noiseAxis = NoiseAxis.Normal; noiseAxisCustom = new Vector3(0f, 1f, 0f);
        noiseMaskUVEnabled = false; noiseMaskUVFalloffU = 0.5f; noiseMaskUVFalloffV = 0.5f;
        noiseMaskHeightEnabled = false; noiseMaskHeightCurve = AnimationCurve.Linear(0, 0, 1, 1);
        noiseFoldout = false;
        _noisePixels = null; _noiseTexRef = null;

        trailDirector = null; trailEdgeTop = null; trailEdgeBot = null; trailDirTried = false;
        trailStartFrame = 0; trailEndFrame = 30; trailFps = 60f;
        trailMode = TrailMode.SinglePoint; trailAxis = TrailAxis.Forward; trailWidth = 1f;
        trailAnchor = TrailAnchor.StartPoint;
        trailExportWorldSpace = false;
        trailResample = false; trailSegments = 30; trailWidthSeg = 1;
        trailSmooth = false; trailSmoothStrength = 0.5f; trailSmoothMax = 1f; trailSingleData = true; trailCross = false; trailSubsteps = 10;
        trailCrossW = 1f; trailCrossWMax = 5f;
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
                float hw = w * 0.5f * trailCrossW;                 // trailCrossW = 十字面宽度倍数
                ct.Add(mid + perp * hw);
                cb.Add(mid - perp * hw);
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

    // 控制点之间生成可重复的不规则边界。保留原控制点作为锚点，Y 沿原边插值。
    List<Vector3> GetCustomBoundary()
    {
        int hash = 17;
        unchecked
        {
            hash = hash * 31 + customIrregularEdge.GetHashCode();
            hash = hash * 31 + customEdgeAmplitude.GetHashCode();
            hash = hash * 31 + customEdgeSegments;
            hash = hash * 31 + customEdgeFrequency.GetHashCode();
            hash = hash * 31 + customEdgeSeed;
            foreach (var point in customPoints) hash = hash * 31 + point.GetHashCode();
        }
        if (_customBoundaryCache != null && hash == _customBoundaryCacheHash)
            return _customBoundaryCache;

        var original = new List<Vector3>(customPoints);
        if (!customIrregularEdge || customEdgeAmplitude <= 1e-6f || customEdgeSegments < 2)
            return CacheCustomBoundary(original, hash);

        float amplitude = customEdgeAmplitude;
        for (int attempt = 0; attempt < 7; attempt++)
        {
            var boundary = new List<Vector3>(customPoints.Count * customEdgeSegments);
            for (int i = 0; i < customPoints.Count; i++)
            {
                Vector3 a = customPoints[i];
                Vector3 b = customPoints[(i + 1) % customPoints.Count];
                boundary.Add(a);
                Vector2 edge = new Vector2(b.x - a.x, b.z - a.z);
                float length = edge.magnitude;
                if (length < 1e-5f) continue;
                Vector2 normal = new Vector2(-edge.y, edge.x) / length;
                float edgeAmplitude = Mathf.Min(amplitude, length * 0.25f);
                for (int segment = 1; segment < customEdgeSegments; segment++)
                {
                    float t = (float)segment / customEdgeSegments;
                    float noise = Mathf.PerlinNoise(
                        (customEdgeSeed % 10000) * 0.017f + i * 13.37f,
                        t * customEdgeFrequency + i * 2.71f) * 2f - 1f;
                    float offset = noise * edgeAmplitude * Mathf.Sin(Mathf.PI * t);
                    Vector3 point = Vector3.Lerp(a, b, t);
                    point.x += normal.x * offset;
                    point.z += normal.y * offset;
                    boundary.Add(point);
                }
            }
            if (!BoundarySelfIntersects(boundary)) return CacheCustomBoundary(boundary, hash);
            amplitude *= 0.5f;
        }
        return CacheCustomBoundary(original, hash);
    }

    List<Vector3> CacheCustomBoundary(List<Vector3> boundary, int hash)
    {
        _customBoundaryCache = boundary;
        _customBoundaryCacheHash = hash;
        return boundary;
    }

    static bool BoundarySelfIntersects(List<Vector3> boundary)
    {
        int count = boundary.Count;
        for (int i = 0; i < count; i++)
        for (int j = i + 2; j < count; j++)
        {
            if (i == 0 && j == count - 1) continue; // 相邻边共用端点
            if (SegmentsCrossXZ(boundary[i], boundary[(i + 1) % count],
                                boundary[j], boundary[(j + 1) % count]))
                return true;
        }
        return false;
    }

    static bool SegmentsCrossXZ(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        float abC = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
        float abD = (b.x - a.x) * (d.z - a.z) - (b.z - a.z) * (d.x - a.x);
        float cdA = (d.x - c.x) * (a.z - c.z) - (d.z - c.z) * (a.x - c.x);
        float cdB = (d.x - c.x) * (b.z - c.z) - (d.z - c.z) * (b.x - c.x);
        return ((abC > 1e-6f && abD < -1e-6f) || (abC < -1e-6f && abD > 1e-6f))
            && ((cdA > 1e-6f && cdB < -1e-6f) || (cdA < -1e-6f && cdB > 1e-6f));
    }

    // ── Custom 多边形模型 ────────────────────────────────────
    // 闭合多边形边界 → Ear Clipping 三角剖分 → 按流动方向映射 UV
    Mesh BuildCustom()
    {
        if (customPoints.Count < 3) return new Mesh { name = "Custom" };
        var boundary = GetCustomBoundary();

        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        var t = previewGO != null ? previewGO.transform : null;

        // ── 1. 多边形包围盒 ──
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in boundary)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }
        float rangeX = Mathf.Max(maxX - minX, 0.0001f);
        float rangeZ = Mathf.Max(maxZ - minZ, 0.0001f);

        // ── 2. 网格分辨率 ──
        int gridX, gridZ;
        if (rangeX >= rangeZ)
        {
            gridX = customFillDensity;
            gridZ = Mathf.Max(1, Mathf.RoundToInt(customFillDensity * (rangeZ / rangeX)));
        }
        else
        {
            gridZ = customFillDensity;
            gridX = Mathf.Max(1, Mathf.RoundToInt(customFillDensity * (rangeX / rangeZ)));
        }

        float cellW = rangeX / gridX;
        float cellH = rangeZ / gridZ;

        // ── 3. 多边形转 2D（XZ）并归一化为 CCW ──
        //     Sutherland-Hodgman 裁剪要求裁剪域为 CCW；这里让被裁剪的多边形也统一为 CCW，
        //     使裁剪结果保持 CCW，方便后续耳切三角化判断朝向。
        var poly2D = new List<Vector2>(boundary.Count);
        foreach (var p in boundary) poly2D.Add(new Vector2(p.x, p.z));
        if (PolygonArea2D(poly2D) < 0f) poly2D.Reverse();

        // ── 4. 逐网格单元：用单元矩形裁剪多边形，再对裁剪结果三角化 ──
        //     单元是凸矩形（凸裁剪域），SH 裁剪对任意（含凹）多边形都正确，
        //     裁出的顶点会精确落在多边形真实边界上，彻底消除边缘缝隙。
        var cellRect = new List<Vector2>(4);
        for (int r = 0; r < gridZ; r++)
        for (int c = 0; c < gridX; c++)
        {
            float x0 = minX + c * cellW;
            float x1 = x0 + cellW;
            float z0 = minZ + r * cellH;
            float z1 = z0 + cellH;

            // CCW 单元矩形作为凸裁剪域
            cellRect.Clear();
            cellRect.Add(new Vector2(x0, z0));
            cellRect.Add(new Vector2(x1, z0));
            cellRect.Add(new Vector2(x1, z1));
            cellRect.Add(new Vector2(x0, z1));

            // 多边形 ∩ 单元 = 该单元被面覆盖的实际区域
            var piece = SutherlandHodgmanClip(poly2D, cellRect);
            if (piece.Count >= 3)
                TriangulatePieceXZ(piece, verts, tris);
        }

        // ── 5. 焊接单元交界重合顶点（消除法线断裂，Noise/高度形变才不会开裂）──
        WeldVertsXZ(verts, tris);

        // ── 6. 边界控制点高度插值（拖动边界点 Y → 整片跟着起伏，不再恒为平面）──
        ApplyCustomBoundaryHeight(verts, boundary);

        // ── 7. 高度控制点形变（可选，在边界高度之上继续雕刻）──
        ApplyCustomHeight(verts);

        // ── 8. UV 流动映射（仅用 XZ 投影，不受高度影响）──
        ComputeCustomUV(verts, uvs, t);

        // ── 9. 极坐标接缝修复：跨 ±180° 的三角形会把整张贴图挤进一条缝，
        //      把低 U 侧顶点复制一份并 +1，使三角形 U 连续，接缝收窄为一条线。
        if (customUVFlowDir == CustomUVFlowDir.Polar)
            FixPolarSeam(verts, uvs, tris);

        return BuildMesh("Custom", verts, uvs, tris);
    }

    // 修复极坐标 UV：极坐标有两处天生问题——(1) ±180° 环绕缝，U 从 1 跳回 0；
    // (2) 中心奇点，半径≈0 的点被迫取某个 U，却连着所有角度的三角形。
    // 对"跨环绕缝"或"触及中心"的三角形，复制其顶点使其独享 UV，并把三个角的 U 解到一致：
    // 中心角贴合参考角、其余角解环绕，从而不再横跨整张贴图。
    static void FixPolarSeam(List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        const float eps = 0.1f;    // 归一化半径(V) 小于此值视为中心点（越大中心越干净）
        int triCount = tris.Count;
        for (int i = 0; i < triCount; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            float ua = uvs[a].x, ub = uvs[b].x, uc = uvs[c].x;
            float va = uvs[a].y, vb = uvs[b].y, vc = uvs[c].y;

            float span = Mathf.Max(ua, Mathf.Max(ub, uc)) - Mathf.Min(ua, Mathf.Min(ub, uc));
            bool touchesCenter = (va < eps || vb < eps || vc < eps);
            if (span <= 0.5f && !touchesCenter) continue;

            // 复制三顶点，让该三角形独享 UV（位置不变）
            int na = verts.Count; verts.Add(verts[a]); uvs.Add(uvs[a]);
            int nb = verts.Count; verts.Add(verts[b]); uvs.Add(uvs[b]);
            int nc = verts.Count; verts.Add(verts[c]); uvs.Add(uvs[c]);

            // 参考角 = 半径最大的角（角度最可靠）
            float refU = (va >= vb && va >= vc) ? ua : (vb >= vc ? ub : uc);

            uvs[na] = new Vector2(ResolvePolarU(ua, va, refU, eps), va);
            uvs[nb] = new Vector2(ResolvePolarU(ub, vb, refU, eps), vb);
            uvs[nc] = new Vector2(ResolvePolarU(uc, vc, refU, eps), vc);

            tris[i] = na; tris[i + 1] = nb; tris[i + 2] = nc;
        }
    }

    // 解算某个角的 U：中心点(半径极小)直接贴合参考角；否则解环绕到离 refU 最近(相差<0.5)
    static float ResolvePolarU(float u, float v, float refU, float eps)
    {
        if (v < eps) return refU;
        while (u - refU >  0.5f) u -= 1f;
        while (refU - u >  0.5f) u += 1f;
        return u;
    }

    // 按 XZ 位置焊接重合顶点：裁剪三角化会在相邻单元交界产生重复点，
    // 若不焊接，RecalculateNormals 会在接缝两侧算出不同法线，
    // 叠加沿法线的 Noise 或高度形变时接缝就会裂开。
    static void WeldVertsXZ(List<Vector3> verts, List<int> tris)
    {
        var map      = new Dictionary<(int, int), int>(verts.Count);
        var remap    = new int[verts.Count];
        var newVerts = new List<Vector3>(verts.Count);
        const float q = 10000f;   // 量化精度 1e-4
        for (int i = 0; i < verts.Count; i++)
        {
            var key = (Mathf.RoundToInt(verts[i].x * q), Mathf.RoundToInt(verts[i].z * q));
            if (!map.TryGetValue(key, out int idx))
            {
                idx = newVerts.Count;
                map[key] = idx;
                newVerts.Add(verts[i]);
            }
            remap[i] = idx;
        }
        for (int i = 0; i < tris.Count; i++) tris[i] = remap[tris[i]];
        verts.Clear();
        verts.AddRange(newVerts);
    }

    // 对一个简单多边形（XZ 平面，Vector2 = (x,z)）做耳切三角化，结果追加到 verts/tris（y=0）
    static void TriangulatePieceXZ(List<Vector2> poly, List<Vector3> verts, List<int> tris)
    {
        // 去掉相邻重复点与首尾重合
        var pts = new List<Vector2>(poly.Count);
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            if (pts.Count == 0 || (a - pts[pts.Count - 1]).sqrMagnitude > 1e-10f)
                pts.Add(a);
        }
        if (pts.Count >= 2 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-10f)
            pts.RemoveAt(pts.Count - 1);
        int n = pts.Count;
        if (n < 3) return;

        // 统一为 CCW，保证耳切凸角判断正确
        if (PolygonArea2D(pts) < 0f) pts.Reverse();

        int baseIdx = verts.Count;
        for (int i = 0; i < n; i++)
            verts.Add(new Vector3(pts[i].x, 0f, pts[i].y));

        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);

        int guard = 0, maxGuard = n * n + 16;
        while (idx.Count > 3 && guard++ < maxGuard)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int i0 = idx[(i - 1 + idx.Count) % idx.Count];
                int i1 = idx[i];
                int i2 = idx[(i + 1) % idx.Count];
                Vector2 a = pts[i0], b = pts[i1], cc = pts[i2];

                // 凸角（CCW 下叉积 > 0）
                float cross = (b.x - a.x) * (cc.y - a.y) - (b.y - a.y) * (cc.x - a.x);
                if (cross <= 0f) continue;

                // 三角形内不含其它顶点才是“耳”
                bool ear = true;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ik = idx[k];
                    if (ik == i0 || ik == i1 || ik == i2) continue;
                    if (PointInTri2D(pts[ik], a, b, cc)) { ear = false; break; }
                }
                if (!ear) continue;

                tris.Add(baseIdx + i0); tris.Add(baseIdx + i1); tris.Add(baseIdx + i2);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break; // 退化多边形，避免死循环
        }
        if (idx.Count == 3)
        {
            tris.Add(baseIdx + idx[0]); tris.Add(baseIdx + idx[1]); tris.Add(baseIdx + idx[2]);
        }
    }

    // 2D 点是否在三角形内（含边）
    static bool PointInTri2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    // 高度控制点形变：把平面上的顶点按就近的高度控制点沿 Y 抬升/下压
    void ApplyCustomHeight(List<Vector3> verts)
    {
        if (!customHeightEnabled || customHeightPoints.Count == 0) return;
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            float dy = 0f;
            for (int k = 0; k < customHeightPoints.Count; k++)
            {
                Vector3 hp = customHeightPoints[k];
                float r = (k < customHeightRadii.Count) ? customHeightRadii[k] : 1f;
                if (r <= 1e-4f) continue;
                float dx = v.x - hp.x, dz = v.z - hp.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= r) continue;
                dy += hp.y * Mathf.Clamp01(customHeightFalloff.Evaluate(d / r));
            }
            v.y += dy;
            verts[i] = v;
        }
    }

    // ── 边界控制点高度（把边界的 Y 插值进整片）────────────────────
    // 三角剖分在 XZ 平面进行，产出的顶点 y 恒为 0；若不做这一步，边界控制点上下拖动
    // 只会改动线框、生成的面片永远是一张平面。这里用均值坐标（Mean Value Coordinates）
    // 把各边界点的 Y 平滑插值到面内每个顶点：
    //   · 顶点落在某个边界点上 → 直接取该点高度
    //   · 顶点落在某条边界边上 → 沿边线性插值（裁剪产生的边缘点都走这一支，故边缘严丝合缝）
    //   · 顶点在面内         → 按 MVC 权重混合所有边界点高度（凹多边形同样成立）
    bool CustomBoundaryHasHeight()
    {
        if (!customBoundaryHeight || customPoints.Count < 3) return false;
        for (int i = 0; i < customPoints.Count; i++)
            if (Mathf.Abs(customPoints[i].y) > 1e-6f) return true;
        return false;   // 边界全平 → 跳过计算，与旧行为完全一致
    }

    void ApplyCustomBoundaryHeight(List<Vector3> verts, List<Vector3> boundary)
    {
        if (!CustomBoundaryHasHeight()) return;
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            v.y += EvalCustomBoundaryY(v.x, v.z, boundary);
            verts[i] = v;
        }
    }

    // 单点求值：返回 (x,z) 处由最终边界 Y 插值出的高度
    float EvalCustomBoundaryY(float x, float z, List<Vector3> boundary)
    {
        int n = boundary.Count;
        if (n < 3) return 0f;
        if (_mvcR == null || _mvcR.Length < n)
        {
            _mvcDx  = new float[n];
            _mvcDz  = new float[n];
            _mvcR   = new float[n];
            _mvcTan = new float[n];
        }

        for (int i = 0; i < n; i++)
        {
            _mvcDx[i] = boundary[i].x - x;
            _mvcDz[i] = boundary[i].z - z;
            _mvcR[i]  = Mathf.Sqrt(_mvcDx[i] * _mvcDx[i] + _mvcDz[i] * _mvcDz[i]);
            if (_mvcR[i] < 1e-6f) return boundary[i].y;      // 正好在某个边界点上
        }

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float cross = _mvcDx[i] * _mvcDz[j] - _mvcDz[i] * _mvcDx[j];
            float dot   = _mvcDx[i] * _mvcDx[j] + _mvcDz[i] * _mvcDz[j];
            float rr    = _mvcR[i] * _mvcR[j];

            // 落在边 i→j 上（共线且夹在两端之间）：MVC 在边上退化，改沿边线性插值
            if (Mathf.Abs(cross) <= 1e-6f * Mathf.Max(rr, 1e-6f) && dot < 0f)
                return Mathf.Lerp(boundary[i].y, boundary[j].y, _mvcR[i] / (_mvcR[i] + _mvcR[j]));

            // tan(α/2) = (1 - cosα) / sinα，其中 cosα = dot/rr、sinα = cross/rr
            _mvcTan[i] = (Mathf.Abs(cross) > 1e-12f) ? (rr - dot) / cross : 0f;
        }

        float wSum = 0f, acc = 0f;
        for (int i = 0; i < n; i++)
        {
            int im = (i - 1 + n) % n;
            float w = (_mvcTan[im] + _mvcTan[i]) / _mvcR[i];
            wSum += w;
            acc  += w * boundary[i].y;
        }
        return (Mathf.Abs(wSum) > 1e-9f) ? acc / wSum : 0f;
    }

    // Gizmo 用：边界高度关闭 / 全平时返回 0，避免白算一遍 MVC
    float EvalCustomBoundaryYSafe(float x, float z)
        => CustomBoundaryHasHeight() ? EvalCustomBoundaryY(x, z, GetCustomBoundary()) : 0f;

    // Sutherland-Hodgman 多边形裁切：用 clipPoly（CCW）裁切 subjectPoly
    // 返回裁切后的多边形顶点列表（CCW，可能是 3~8 边）
    static List<Vector2> SutherlandHodgmanClip(List<Vector2> subjectPoly, List<Vector2> clipPoly)
    {
        var output = new List<Vector2>(subjectPoly);

        for (int i = 0; i < clipPoly.Count; i++)
        {
            if (output.Count == 0) break;

            var clipA = clipPoly[i];
            var clipB = clipPoly[(i + 1) % clipPoly.Count];

            var input = new List<Vector2>(output);
            output.Clear();

            for (int j = 0; j < input.Count; j++)
            {
                var cur = input[j];
                var prev = input[(j - 1 + input.Count) % input.Count];

                bool curInside = IsLeftOf(clipA, clipB, cur);
                bool prevInside = IsLeftOf(clipA, clipB, prev);

                if (curInside)
                {
                    if (!prevInside)
                    {
                        // 从外到内：添加交点
                        Vector2 intersect = LineIntersect(prev, cur, clipA, clipB);
                        output.Add(intersect);
                    }
                    output.Add(cur);
                }
                else if (prevInside)
                {
                    // 从内到外：添加交点
                    Vector2 intersect = LineIntersect(prev, cur, clipA, clipB);
                    output.Add(intersect);
                }
            }
        }

        return output;
    }

    // 点 p 是否在线段 a->b 的左侧（CCW 多边形内部）
    static bool IsLeftOf(Vector2 a, Vector2 b, Vector2 p)
    {
        return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) >= 0f;
    }

    // 线段 p1p2 与线段 p3p4 的交点
    static Vector2 LineIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d = (p2.x - p1.x) * (p4.y - p3.y) - (p2.y - p1.y) * (p4.x - p3.x);
        if (Mathf.Abs(d) < 1e-10f) return p1;
        float t = ((p3.x - p1.x) * (p4.y - p3.y) - (p3.y - p1.y) * (p4.x - p3.x)) / d;
        return new Vector2(p1.x + t * (p2.x - p1.x), p1.y + t * (p2.y - p1.y));
    }

    // 多边形有符号面积（2D），>0=CCW
    static float PolygonArea2D(List<Vector2> poly)
    {
        float area = 0f;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        return area * 0.5f;
    }

    // 点是否在多边形内部（射线法，XZ 平面）
    static bool PointInPolygonXY(Vector3 p, List<Vector3> poly)
    {
        int n = poly.Count;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector3 pi = poly[i];
            Vector3 pj = poly[j];
            if (((pi.z > p.z) != (pj.z > p.z)) &&
                (p.x < (pj.x - pi.x) * (p.z - pi.z) / (pj.z - pi.z + 1e-10f) + pi.x))
                inside = !inside;
        }
        return inside;
    }

    // 多边形有符号面积（XZ 平面），>0=CCW
    static float PolygonSignedArea(List<Vector3> pts)
    {
        float area = 0f;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % n];
            area += (a.x * b.z - b.x * a.z);
        }
        return area * 0.5f;
    }

    static bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector2 p2 = new Vector2(p.x, p.z);
        Vector2 a2 = new Vector2(a.x, a.z);
        Vector2 b2 = new Vector2(b.x, b.z);
        Vector2 c2 = new Vector2(c.x, c.z);

        float d1 = Sign(p2, a2, b2);
        float d2 = Sign(p2, b2, c2);
        float d3 = Sign(p2, c2, a2);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    // 按流动方向计算 UV
    void ComputeCustomUV(List<Vector3> verts, List<Vector2> uvs, Transform t)
    {
        if (verts.Count == 0) return;

        // 极坐标：以重心为中心，U = 角度(0-1)，V = 归一化半径(0-1)
        if (customUVFlowDir == CustomUVFlowDir.Polar)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < verts.Count; i++) center += verts[i];
            center /= verts.Count;

            float maxR = 0.0001f;
            for (int i = 0; i < verts.Count; i++)
            {
                float dx = verts[i].x - center.x, dz = verts[i].z - center.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > maxR) maxR = d;
            }
            for (int i = 0; i < verts.Count; i++)
            {
                float dx = verts[i].x - center.x, dz = verts[i].z - center.z;
                float u = Mathf.Atan2(dz, dx) / (2f * Mathf.PI) + 0.5f;
                float v = Mathf.Sqrt(dx * dx + dz * dz) / maxR;
                if (customFlipUV) { u = 1f - u; v = 1f - v; }
                uvs.Add(new Vector2(u, v));
            }
            return;
        }

        // 计算流动方向的投影轴
        Vector3 flowAxis = Vector3.right;
        Vector3 perpAxis = Vector3.forward;

        switch (customUVFlowDir)
        {
            case CustomUVFlowDir.WorldX:
                flowAxis = Vector3.right;  perpAxis = Vector3.forward; break;
            case CustomUVFlowDir.WorldZ:
                flowAxis = Vector3.forward; perpAxis = Vector3.right;  break;
            case CustomUVFlowDir.LocalX:
                flowAxis = t != null ? t.right     : Vector3.right;
                perpAxis = t != null ? t.forward   : Vector3.forward;  break;
            case CustomUVFlowDir.LocalZ:
                flowAxis = t != null ? t.forward   : Vector3.forward;
                perpAxis = t != null ? t.right     : Vector3.right;    break;
            case CustomUVFlowDir.BoundingBox:
            {
                // 自动取包围盒最长轴为流动方向
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < verts.Count; i++)
                {
                    if (verts[i].x < minX) minX = verts[i].x;
                    if (verts[i].x > maxX) maxX = verts[i].x;
                    if (verts[i].z < minZ) minZ = verts[i].z;
                    if (verts[i].z > maxZ) maxZ = verts[i].z;
                }
                float rangeX = maxX - minX;
                float rangeZ = maxZ - minZ;
                if (rangeX >= rangeZ)
                {
                    flowAxis = Vector3.right;  perpAxis = Vector3.forward;
                }
                else
                {
                    flowAxis = Vector3.forward; perpAxis = Vector3.right;
                }
                break;
            }
        }

        // 计算投影范围
        float minFlow = float.MaxValue, maxFlow = float.MinValue;
        float minPerp = float.MaxValue, maxPerp = float.MinValue;
        for (int i = 0; i < verts.Count; i++)
        {
            float f = Vector3.Dot(verts[i], flowAxis);
            float p = Vector3.Dot(verts[i], perpAxis);
            if (f < minFlow) minFlow = f;
            if (f > maxFlow) maxFlow = f;
            if (p < minPerp) minPerp = p;
            if (p > maxPerp) maxPerp = p;
        }

        float rangeFlow = Mathf.Max(maxFlow - minFlow, 0.0001f);
        float rangePerp = Mathf.Max(maxPerp - minPerp, 0.0001f);

        for (int i = 0; i < verts.Count; i++)
        {
            float f = Vector3.Dot(verts[i], flowAxis);
            float p = Vector3.Dot(verts[i], perpAxis);
            float u = (f - minFlow) / rangeFlow;
            float v = (p - minPerp) / rangePerp;
            if (customFlipUV) { u = 1f - u; v = 1f - v; }
            uvs.Add(new Vector2(u, v));
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

        // 预计算端点值（十字片复用）
        float topRadiusW = ringTopR * ringRadiusCurve.Evaluate(0f);
        float botRadiusW = ringBotR * ringRadiusCurve.Evaluate(1f);
        float topY = ringHeight * ringHeightCurve.Evaluate(0f);
        // botY 始终为 0（(1-t)*ringHeight*curve(1) = 0*... = 0）

        // Pass 0: 普通环面
        for (int r = 0; r <= ringRows; r++)
        {
            float t      = (float)r / ringRows;
            float radius = Mathf.Lerp(ringTopR, ringBotR, t) * ringRadiusCurve.Evaluate(t);
            float y      = (1f - t) * ringHeight * ringHeightCurve.Evaluate(t);
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

                    // 该列的顶点和底点（含扭曲+半径曲线+高度曲线）：端点用预计算值
                    float topAngle = ct * ringArc * Mathf.Deg2Rad + ringTwist * ringTwistCurve.Evaluate(0f) * Mathf.Deg2Rad;
                    float botAngle = ct * ringArc * Mathf.Deg2Rad + ringTwist * ringTwistCurve.Evaluate(1f) * Mathf.Deg2Rad;
                    Vector3 topPt = new Vector3(Mathf.Cos(topAngle) * topRadiusW, topY,       Mathf.Sin(topAngle) * topRadiusW);
                    Vector3 botPt = new Vector3(Mathf.Cos(botAngle) * botRadiusW, 0,          Mathf.Sin(botAngle) * botRadiusW);

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

                    float offset = (t * 2f - 1f) * halfLen * ringCrossW;   // ringCrossW = 十字面宽度倍数
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

        // 整体倾斜（龙卷风效果）：绕 X/Z 轴旋转所有顶点
        if (Mathf.Abs(ringTiltX) > 0.001f || Mathf.Abs(ringTiltZ) > 0.001f)
        {
            Quaternion tiltRot = Quaternion.Euler(ringTiltX, 0f, ringTiltZ);
            for (int i = 0; i < verts.Count; i++)
                verts[i] = tiltRot * verts[i];
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
            // 第 2 片起套用「十字面宽度倍数」，只影响新增的那些片
            float wMul  = (i == 0) ? 1f : crossExtraW;
            var   right = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * crossW * wMul * 0.5f;
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

    float HelixYProfile(float t)
        => Mathf.Max(0.01f, helixYProfileCurve != null ? helixYProfileCurve.Evaluate(t) : 1f);

    static Vector3 ScaleHelixXZ(Vector3 offset, float profile)
        => new Vector3(offset.x * profile, offset.y, offset.z * profile);

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
                float profile = HelixYProfile(t);
                float radius = Mathf.Lerp(helixBotR, helixTopR, t) * profile;
                float y      = t * helixH;
                float width  = helixW * helixCurve.Evaluate(t) * 0.5f;

                var center = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

                // [FIX] tangent 计算 — 前向差分 + 端点后向差分，避免 t=1 时 NaN
                Vector3 tangent;
                if (t < 1f)
                {
                    float dt    = 0.01f;
                    float tNext = Mathf.Min(1f, t + dt);
                    float aNext = tNext * helixLoops * 2 * Mathf.PI + h * angleOffset;
                    float rNext = Mathf.Lerp(helixBotR, helixTopR, tNext) * HelixYProfile(tNext);
                    var   pNext = new Vector3(Mathf.Cos(aNext) * rNext, tNext * helixH, Mathf.Sin(aNext) * rNext);
                    tangent = (pNext - center).normalized;
                }
                else
                {
                    // 端点用后向差分
                    float dt    = 0.01f;
                    float tPrev = t - dt;
                    float aPrev = tPrev * helixLoops * 2 * Mathf.PI + h * angleOffset;
                    float rPrev = Mathf.Lerp(helixBotR, helixTopR, tPrev) * HelixYProfile(tPrev);
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
                    verts.Add(center + ScaleHelixXZ(bitang * (ct * 2 - 1), profile));
                    uvs.Add(new Vector2(ct, t));
                }

                // 十字片
                if (helixIsCross)
                {
                    // helixCrossW = 十字面宽度倍数（1 = 与主面等宽）
                    var crossBit = Vector3.Cross(tangent, bitang.normalized).normalized * width * helixCrossW;
                    for (int c = 0; c <= helixCols; c++)
                    {
                        float ct = (float)c / helixCols;
                        verts.Add(center + ScaleHelixXZ(crossBit * (ct * 2 - 1), profile));
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
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        if (splinePaths != null)
            foreach (var path in splinePaths)
                if (path != null && path.points != null && path.points.Count >= 2)
                    AppendSplinePath(path, verts, uvs, tris);
        return BuildMesh(splineShape == SplineShape.Tube ? "SplineTube" : "Spline", verts, uvs, tris);
    }

    // 每条路径分别采样并追加到同一份顶点/三角形数组；UV 沿各自路径从 0 到 1。
    void AppendSplinePath(SplinePath path, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        var points = path.points;
        SyncSplineFlips(path);
        var flips = path.flips;
        // Catmull-Rom 沿样条采样
        var samples = new List<Vector3>();
        for (int s = 0; s <= splineCols; s++)
        {
            float st = (float)s / splineCols * (points.Count - 1);
            samples.Add(CatmullRom(points, st));
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
            int baseIdx = verts.Count;
            for (int i = 0; i < samples.Count; i++)
            {
                float t = (float)i / Mathf.Max(samples.Count - 1, 1);
                float radius = splineW * splineWidthCurve.Evaluate(t) * 0.5f;

                Vector3 tangent = tangents[i];
                Vector3 right   = rights[i];

                // twist + 每点翻转（与 strip 模式一致）
                float totalTwist = splineTwist * t;
                {
                    float st = t * (points.Count - 1);
                    int fi = Mathf.FloorToInt(st);
                    float ff = st - fi;
                    fi = Mathf.Clamp(fi, 0, flips.Count - 1);
                    int fi2 = Mathf.Min(fi + 1, flips.Count - 1);
                    totalTwist += Mathf.Lerp(flips[fi], flips[fi2], ff);
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
                int bl = baseIdx + i * sidesV + c;
                int br = bl + 1;
                int tl = bl + sidesV;
                int tr = tl + 1;
                tris.Add(bl); tris.Add(tl); tris.Add(br);
                tris.Add(br); tris.Add(tl); tris.Add(tr);
            }

            return;
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
                float flipAngle = 0f;
                {
                    float st = t * (points.Count - 1);
                    int fi = Mathf.FloorToInt(st);
                    float ff = st - fi;
                    fi = Mathf.Clamp(fi, 0, flips.Count - 1);
                    int fi2 = Mathf.Min(fi + 1, flips.Count - 1);
                    flipAngle = Mathf.Lerp(flips[fi], flips[fi2], ff);
                }
                totalTwist += flipAngle;

                if (Mathf.Abs(totalTwist) > 0.01f)
                {
                    Quaternion twistRot = Quaternion.AngleAxis(totalTwist, tangents[i]);
                    right = twistRot * right;
                }

                // 十字片第二 pass（宽度可独立倍数缩放）
                Vector3 dir = right;
                float   wp  = w;
                if (p == 1)
                {
                    dir = Vector3.Cross(right, tangents[i]).normalized;
                    wp  = w * splineCrossW;
                }

                for (int r = 0; r <= splineRows; r++)
                {
                    float rt = (float)r / splineRows;
                    verts.Add(samples[i] + dir * wp * (rt * 2f - 1f));
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

    static void SyncSplineFlips(SplinePath path)
    {
        if (path.points == null) path.points = new List<Vector3>();
        if (path.flips == null) path.flips = new List<float>();
        while (path.flips.Count < path.points.Count)
            path.flips.Add(0f);
        while (path.flips.Count > path.points.Count)
            path.flips.RemoveAt(path.flips.Count - 1);
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

    // ── Noise 顶点偏移 ────────────────────────────────────────
    // 用噪声贴图采样结果沿指定方向偏移顶点位置
    void ApplyNoise(Mesh mesh)
    {
        if (noiseTex == null) return;

        // 缓存噪声像素（贴图未变时复用）。不要求贴图开 Read/Write：
        // 不可读时用 Blit 到临时 RenderTexture 再 ReadPixels 拿到像素。
        if (_noisePixels == null || _noiseTexRef != noiseTex || _noiseTexW != noiseTex.width || _noiseTexH != noiseTex.height)
        {
            _noiseTexW    = noiseTex.width;
            _noiseTexH    = noiseTex.height;
            _noiseTexRef  = noiseTex;
            _noisePixels  = ReadTexturePixels(noiseTex);
        }
        if (_noisePixels == null || _noisePixels.Length == 0) return;

        var verts  = mesh.vertices;
        var uvs    = mesh.uv;

        // Noise 强度遮罩：与顶点色完全解耦，独立计算（不读 mesh.colors）
        float[] maskArr = (noiseMaskUVEnabled || noiseMaskHeightEnabled) ? ComputeNoiseMask(mesh) : null;

        // 需要法线来支持法线方向偏移
        Vector3[] normals = null;
        if (noiseAxis == NoiseAxis.Normal)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        for (int i = 0; i < verts.Length; i++)
        {
            // 用 UV 采样噪声（含 Tiling / Offset，Wrap 模式）
            float u = uvs[i].x * noiseTiling.x + noiseOffset.x;
            float v = uvs[i].y * noiseTiling.y + noiseOffset.y;
            float n = SampleNoiseWrap(u, v);
            n = (n - 0.5f) * 2f;  // -1~1

            // 遮罩（独立计算，默认 1）
            float m = (maskArr != null && i < maskArr.Length) ? maskArr[i] : 1f;

            // 偏移方向
            Vector3 dir;
            switch (noiseAxis)
            {
                case NoiseAxis.X:      dir = Vector3.right;   break;
                case NoiseAxis.Y:      dir = Vector3.up;      break;
                case NoiseAxis.Z:      dir = Vector3.forward; break;
                case NoiseAxis.Custom: dir = noiseAxisCustom.normalized; break;
                default:               dir = normals != null ? normals[i] : Vector3.up; break;  // Normal
            }

            verts[i] += dir * n * noiseStrength * m;
        }

        mesh.vertices = verts;
    }

    // 读取贴图像素：优先直接读；贴图未开 Read/Write 时用 Blit→临时 RT→ReadPixels，
    // 因此项目中任何贴图都能用作噪声图，无需改导入设置。
    static Color[] ReadTexturePixels(Texture2D tex)
    {
        if (tex.isReadable)
        {
            try { return tex.GetPixels(); } catch { /* 落到 Blit 路径 */ }
        }

        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var prevActive = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;

        var tmp = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
        tmp.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        tmp.Apply();
        Color[] px = tmp.GetPixels();

        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(tmp);
        return px;
    }

    // 独立计算 Noise 强度遮罩（与顶点色解耦）：使用 Noise 专属的 UV 渐变 + 高度渐变参数，
    // 不依赖 edgeTransp / heightGradient / mesh.colors。
    float[] ComputeNoiseMask(Mesh mesh)
    {
        int n = mesh.vertexCount;
        var mask = new float[n];
        for (int i = 0; i < n; i++) mask[i] = 1f;

        // UV 渐变：按 UV 到最近边缘的距离衰减
        if (noiseMaskUVEnabled)
        {
            var uvs = mesh.uv;
            for (int i = 0; i < n && i < uvs.Length; i++)
            {
                float u = Mathf.Repeat(uvs[i].x, 1f);
                float v = Mathf.Repeat(uvs[i].y, 1f);
                float fadeU = 1f, fadeV = 1f;
                if (noiseMaskUVFalloffU > 0.01f) fadeU = Mathf.Clamp01(Mathf.Min(u, 1f - u) / noiseMaskUVFalloffU);
                if (noiseMaskUVFalloffV > 0.01f) fadeV = Mathf.Clamp01(Mathf.Min(v, 1f - v) / noiseMaskUVFalloffV);
                mask[i] *= fadeU * fadeV;
            }
        }

        // 高度渐变：归一化 Y 经曲线映射为遮罩值（曲线值即遮罩 0~1，真正生效）
        if (noiseMaskHeightEnabled)
        {
            var verts = mesh.vertices;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i].y < minY) minY = verts[i].y;
                if (verts[i].y > maxY) maxY = verts[i].y;
            }
            float rangeY = maxY - minY;
            if (rangeY < 0.0001f) rangeY = 1f;
            for (int i = 0; i < n && i < verts.Length; i++)
            {
                float t = (verts[i].y - minY) / rangeY;
                mask[i] *= Mathf.Clamp01(noiseMaskHeightCurve.Evaluate(t));
            }
        }

        return mask;
    }

    // 环绕采样噪声贴图（Wrap + 双线性）。传入的 u,v 已含 Tiling / Offset（纹理空间），
    // 这里再乘贴图宽高映射到像素空间，保证整张噪声图铺满网格而非只采样左上角。
    float SampleNoiseWrap(float u, float v)
    {
        if (_noiseTexW <= 0 || _noiseTexH <= 0 || _noisePixels == null) return 0.5f;

        // 纹理空间 → 像素浮点坐标（-0.5 对齐像素中心）
        float fx = u * _noiseTexW - 0.5f;
        float fy = v * _noiseTexH - 0.5f;
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0;
        float ty = fy - y0;

        float c00 = NoiseLum(x0,     y0);
        float c10 = NoiseLum(x0 + 1, y0);
        float c01 = NoiseLum(x0,     y0 + 1);
        float c11 = NoiseLum(x0 + 1, y0 + 1);

        float top = Mathf.Lerp(c00, c10, tx);
        float bot = Mathf.Lerp(c01, c11, tx);
        return Mathf.Lerp(top, bot, ty);
    }

    // 取像素亮度（整数像素坐标，环绕）
    float NoiseLum(int x, int y)
    {
        x = ((x % _noiseTexW) + _noiseTexW) % _noiseTexW;
        y = ((y % _noiseTexH) + _noiseTexH) % _noiseTexH;
        int idx = y * _noiseTexW + x;
        if (idx < 0 || idx >= _noisePixels.Length) return 0.5f;
        Color c = _noisePixels[idx];
        return (c.r + c.g + c.b) / 3f;
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
        _Scroll (""Scroll"", Vector) = (0,0,0,0)
        _PreviewTile (""Preview Tile"", Vector) = (1,1,0,0)
        _Cull (""Cull"", Float) = 0
    }
    SubShader {
        Tags { ""Queue""=""Transparent"" ""RenderType""=""Transparent"" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull [_Cull]
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            sampler2D _MainTex;
            fixed4 _BackColor1;
            fixed4 _BackColor2;
            float _UsePreviewTex;
            float _UVChannel;
            float4 _Scroll;
            float4 _PreviewTile;
            struct appdata { float4 vertex : POSITION; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = ((_UVChannel > 0.5) ? v.uv1 : v.uv0) * _PreviewTile.xy + _Scroll.xy; return o; }
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
        if (v.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
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

    // ── Custom 多边形 Scene 交互 ─────────────────────────────
    void DrawCustomHandles()
    {
        if (previewGO == null) return;
        var t = previewGO.transform;

        // 高度控制点优先绘制与交互
        if (customHeightEnabled) DrawCustomHeightHandles(t);

        // 绘制控制点
        for (int i = 0; i < customPoints.Count; i++)
        {
            Vector3 wp = t.TransformPoint(customPoints[i]);
            float   sz = HandleUtility.GetHandleSize(wp) * 0.12f;

            Handles.color = customSelectedPoints.Contains(i) ? Color.yellow : Color.green;
            if (Handles.Button(wp, Quaternion.identity, sz, sz * 1.2f, Handles.SphereHandleCap))
            {
                customSelectedPoints.Clear();
                customSelectedPoints.Add(i);
                customSelectedPoint = i;
                customSelectedHeightPoint = -1;
                Repaint();
            }
        }

        // 实际生成边界与 Mesh 一致；不规则模式下淡线显示原控制点边。
        var boundary = GetCustomBoundary();
        if (boundary.Count >= 2)
        {
            Handles.color = new Color(0.2f, 0.8f, 1f, 0.85f);
            for (int i = 0; i < boundary.Count; i++)
                Handles.DrawLine(t.TransformPoint(boundary[i]),
                                 t.TransformPoint(boundary[(i + 1) % boundary.Count]));
        }
        if (customIrregularEdge && customPoints.Count >= 2)
        {
            Handles.color = new Color(0.5f, 0.8f, 1f, 0.3f);
            for (int i = 0; i < customPoints.Count; i++)
                Handles.DrawDottedLine(t.TransformPoint(customPoints[i]),
                    t.TransformPoint(customPoints[(i + 1) % customPoints.Count]), 4f);
        }

        // 多点整体变换 Handle（移动/旋转/缩放 × 世界/本地）+ 软选（选中高度点时让位给高度 Handle）
        if (customSelectedPoints.Count > 0 && !(customHeightEnabled && customSelectedHeightPoint >= 0))
            DoPointsTransformHandle(t, customPoints, customSelectedPoints,
                                    customSoftSelect, customSoftRadius, customSoftCurve);

        // Shift+左键 在最近的边上插入新点
        if (Event.current.shift && Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            Plane groundPlane = new Plane(t.up, t.position);

            if (groundPlane.Raycast(ray, out float dist))
            {
                Vector3 hitWorld = ray.GetPoint(dist);
                Vector3 hitLocal = t.InverseTransformPoint(hitWorld);

                int bestEdge = -1;
                float bestDist = 0.3f;
                float bestT = 0f;
                for (int i = 0; i < customPoints.Count; i++)
                {
                    Vector3 a = customPoints[i];
                    Vector3 b = customPoints[(i + 1) % customPoints.Count];
                    // 只在 XZ 平面比距离：边界点若已被抬升，3D 距离会把这条边推远、导致插不进点
                    Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z);
                    Vector2 h2 = new Vector2(hitLocal.x, hitLocal.z);
                    Vector2 ab = b2 - a2;
                    float tt = (ab.sqrMagnitude > 1e-8f) ? Mathf.Clamp01(Vector2.Dot(h2 - a2, ab) / ab.sqrMagnitude) : 0f;
                    float d  = Vector2.Distance(h2, a2 + ab * tt);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestEdge = i;
                        bestT    = tt;
                    }
                }

                if (bestEdge >= 0)
                {
                    // 新点的 Y 沿该边插值，而不是被压回 y=0 平面（否则每插一点就在面上戳一个坑）
                    Vector3 ea = customPoints[bestEdge];
                    Vector3 eb = customPoints[(bestEdge + 1) % customPoints.Count];
                    hitLocal.y = Mathf.Lerp(ea.y, eb.y, bestT);
                    customPoints.Insert(bestEdge + 1, hitLocal);
                    customSelectedPoint = bestEdge + 1;
                    customSelectedPoints.Clear();
                    customSelectedPoints.Add(customSelectedPoint);
                    Event.current.Use();
                    GUIUtility.hotControl = 0;
                    dirty = true;
                    Repaint();
                }
            }
        }

        // 矩形框选（空白处拖拽）
        int boxCtrlId = GUIUtility.GetControlID(FocusType.Passive);
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !Event.current.shift)
        {
            bool clickedPoint = false;
            for (int i = 0; i < customPoints.Count; i++)
            {
                Vector3 wp = t.TransformPoint(customPoints[i]);
                Vector2 gp = HandleUtility.WorldToGUIPoint(wp);
                if (Vector2.Distance(Event.current.mousePosition, gp) < 12f) { clickedPoint = true; break; }
            }
            if (!clickedPoint)
            {
                customSelectedPoints.Clear();
                customIsBoxSelecting = true;
                customBoxSelectStart = Event.current.mousePosition;
                GUIUtility.hotControl = boxCtrlId;
                Event.current.Use();
            }
        }

        if (customIsBoxSelecting && GUIUtility.hotControl == boxCtrlId)
        {
            Vector2 cur = Event.current.mousePosition;
            Rect boxRect = Rect.MinMaxRect(
                Mathf.Min(customBoxSelectStart.x, cur.x),
                Mathf.Min(customBoxSelectStart.y, cur.y),
                Mathf.Max(customBoxSelectStart.x, cur.x),
                Mathf.Max(customBoxSelectStart.y, cur.y));

            Handles.color = new Color(1f, 1f, 0f, 0.3f);
            Handles.DrawSolidRectangleWithOutline(boxRect, new Color(1, 1, 0, 0.1f), new Color(1, 1, 0, 0.8f));

            if (Event.current.type == EventType.MouseUp)
            {
                for (int i = 0; i < customPoints.Count; i++)
                {
                    Vector2 gp = HandleUtility.WorldToGUIPoint(t.TransformPoint(customPoints[i]));
                    if (boxRect.Contains(gp))
                        customSelectedPoints.Add(i);
                }
                customSelectedPoint = customSelectedPoints.Count > 0
                                    ? customSelectedPoints[customSelectedPoints.Count - 1] : -1;
                customIsBoxSelecting = false;
                GUIUtility.hotControl = 0;
                Event.current.Use();
                Repaint();
            }
        }
    }

    static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / ab.sqrMagnitude;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    // ── 控制点整体变换 Handle（Spline / Custom 共用）───────────────
    // 移动 / 旋转 / 缩放三种工具，绕选集质心操作；坐标空间可选世界或本地（对齐预览模型朝向）。
    // 软选未选中点按距离衰减跟随：移动按权重缩放位移、旋转按权重 Slerp 角度、缩放按权重靠向 1。
    bool DoPointsTransformHandle(Transform t, List<Vector3> pts, List<int> sel,
                                 bool soft, float softRadius, AnimationCurve softCurve)
    {
        if (t == null || pts == null || sel == null || sel.Count == 0) return false;

        Vector3 centerLocal = Vector3.zero;
        int valid = 0;
        foreach (int i in sel)
        {
            if (i < 0 || i >= pts.Count) continue;
            centerLocal += pts[i];
            valid++;
        }
        if (valid == 0) return false;
        centerLocal /= valid;

        Vector3    centerWorld = t.TransformPoint(centerLocal);
        Quaternion baseRot     = (pointSpace == HandleSpace.Local) ? t.rotation : Quaternion.identity;

        // 选集 / 工具 / 坐标系变化时复位 Handle 累积姿态，避免带着上次的角度继续转
        int hash = pts.Count * 31 + sel.Count;
        foreach (int i in sel) hash = hash * 31 + i;
        hash = hash * 13 + (int)pointSpace * 3 + (int)pointTool;
        if (hash != _ptStateHash) { _ptStateHash = hash; _ptRot = baseRot; _ptScale = Vector3.one; }

        Vector3    moveLocal = Vector3.zero;      // kind 0
        Quaternion rotLocal  = Quaternion.identity; // kind 1
        Vector3    scaleFac  = Vector3.one;       // kind 2
        int kind = -1;

        if (pointTool == PointTool.Rotate)
        {
            EditorGUI.BeginChangeCheck();
            Quaternion nr = Handles.DoRotationHandle(_ptRot, centerWorld);
            if (EditorGUI.EndChangeCheck())
            {
                Quaternion dq = nr * Quaternion.Inverse(_ptRot);       // 世界空间增量
                _ptRot   = nr;
                rotLocal = Quaternion.Inverse(t.rotation) * dq * t.rotation;   // → 局部空间
                kind = 1;
            }
        }
        else if (pointTool == PointTool.Scale)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 ns = Handles.DoScaleHandle(_ptScale, centerWorld, baseRot,
                                               HandleUtility.GetHandleSize(centerWorld));
            if (EditorGUI.EndChangeCheck())
            {
                scaleFac = new Vector3(SafeRatio(ns.x, _ptScale.x),
                                       SafeRatio(ns.y, _ptScale.y),
                                       SafeRatio(ns.z, _ptScale.z));
                _ptScale = ns;
                kind = 2;
            }
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            Vector3 nw = Handles.DoPositionHandle(centerWorld, baseRot);
            if (EditorGUI.EndChangeCheck())
            {
                moveLocal = t.InverseTransformPoint(nw) - centerLocal;
                kind = 0;
            }
        }
        // 世界坐标模式：一次拖拽结束后把 Gizmo 复位（旋转回世界轴向、缩放回 1），
        // 这样下一次拖拽仍从世界轴/1.0 起步；本地模式保留跟随物体朝向的姿态。
        if (Event.current.type == EventType.MouseUp)
        {
            if (pointSpace == HandleSpace.World) _ptRot = Quaternion.identity;
            _ptScale = Vector3.one;
        }
        if (kind < 0) return false;

        Quaternion frameLocal = Quaternion.Inverse(t.rotation) * baseRot;   // Handle 坐标系 → 局部空间
        float radius = Mathf.Max(1e-4f, softRadius);

        for (int j = 0; j < pts.Count; j++)
        {
            float w = 1f;
            if (!sel.Contains(j))
            {
                if (!soft) continue;
                float dMin = float.MaxValue;
                foreach (int idx in sel)
                {
                    if (idx < 0 || idx >= pts.Count) continue;
                    dMin = Mathf.Min(dMin, Vector3.Distance(pts[j], pts[idx]));
                }
                if (dMin >= radius) continue;
                w = Mathf.Clamp01(softCurve != null ? softCurve.Evaluate(dMin / radius) : 0f);
                if (w <= 1e-4f) continue;
            }

            Vector3 p = pts[j];
            if (kind == 0)
            {
                p += moveLocal * w;
            }
            else if (kind == 1)
            {
                Quaternion q = (w >= 0.999f) ? rotLocal : Quaternion.Slerp(Quaternion.identity, rotLocal, w);
                p = centerLocal + q * (p - centerLocal);
            }
            else
            {
                Vector3 f   = (w >= 0.999f) ? scaleFac : Vector3.Lerp(Vector3.one, scaleFac, w);
                Vector3 loc = Quaternion.Inverse(frameLocal) * (p - centerLocal);
                loc = new Vector3(loc.x * f.x, loc.y * f.y, loc.z * f.z);
                p = centerLocal + frameLocal * loc;
            }
            pts[j] = p;
        }

        dirty = true;
        Repaint();
        return true;
    }

    static float SafeRatio(float a, float b) => (Mathf.Abs(b) > 1e-5f) ? a / b : 1f;

    // ── Custom 高度控制点 Scene 交互 ──
    void DrawCustomHeightHandles(Transform t)
    {
        for (int i = 0; i < customHeightPoints.Count; i++)
        {
            Vector3 hp = customHeightPoints[i];
            float baseY = EvalCustomBoundaryYSafe(hp.x, hp.z);
            Vector3 wp = t.TransformPoint(CustomHeightPointActualLocal(i));
            float sz = HandleUtility.GetHandleSize(wp) * 0.1f;

            // 高度点的位置始终相对基准曲面，边界 Y 上下调整时会一起跟随。
            float r = (i < customHeightRadii.Count) ? customHeightRadii[i] : 1f;
            Handles.color = new Color(1f, 0.4f, 0.9f, 0.35f);
            Vector3 baseW = t.TransformPoint(new Vector3(customHeightPoints[i].x, baseY, customHeightPoints[i].z));
            Handles.DrawWireDisc(baseW, t.up, r * t.lossyScale.x);
            // 从基面到高度点的竖线，直观显示抬升量
            Handles.color = new Color(1f, 0.4f, 0.9f, 0.9f);
            Handles.DrawDottedLine(baseW, wp, 3f);

            Handles.color = (i == customSelectedHeightPoint) ? Color.yellow : new Color(1f, 0.3f, 0.9f);
            if (Handles.Button(wp, Quaternion.identity, sz, sz * 1.3f, Handles.CubeHandleCap))
            {
                customSelectedHeightPoint = i;
                customSelectedPoints.Clear();
                Repaint();
            }
        }

        // 选中点：完整三轴移动 Handle（XZ 改位置，Y 改高度）
        if (customSelectedHeightPoint >= 0 && customSelectedHeightPoint < customHeightPoints.Count)
        {
            Vector3 wp = t.TransformPoint(CustomHeightPointActualLocal(customSelectedHeightPoint));
            EditorGUI.BeginChangeCheck();
            Vector3 nwp = Handles.DoPositionHandle(wp, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 local = t.InverseTransformPoint(nwp);
                SetCustomHeightPointFromActualLocal(customSelectedHeightPoint, local);
                Repaint();
            }
        }
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
        PushPreviewTile(target > 0.5f);   // 通道切换后预览平铺跟着换成该通道的值
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
