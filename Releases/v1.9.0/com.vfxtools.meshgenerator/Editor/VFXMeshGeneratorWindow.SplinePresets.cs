using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class VFXMeshGeneratorWindow
{
    const string SplinePresetFormat = "VFXMeshGenerator.SplinePreset";
    const int SplinePresetVersion = 1;
    const long SplinePresetMaxBytes = 16 * 1024 * 1024;
    [SerializeField] string lastSplinePresetPath = string.Empty;

    [Serializable]
    sealed class SplinePresetData
    {
        public string format;
        public int schemaVersion;
        public string toolVersion;
        public List<SplinePresetPath> paths;
        public int activePath;
        public Vector3 previewPosition;
        public Quaternion previewRotation;
        public Vector3 previewScale;
        public bool previewUV2;
        public SplinePresetAsset noiseTexture;
        public SplinePresetAsset previewTexture;
        public SplinePresetAsset previewMaterial;
        public string projectName;
        public int splineRows;
        public int splineCols;
        public float splineW;
        public float splineWMax;
        public float splineCrossW;
        public float splineCrossWMax;
        public SplineShape splineShape;
        public int splineTubeSides;
        public float splineTwist;
        public SplinePresetCurve splineWidthCurve;
        public bool splineSoftSelect;
        public float splineSoftRadius;
        public SplinePresetCurve splineSoftCurve;
        public PointTool pointTool;
        public HandleSpace pointSpace;
        public SplinePositionSpace splinePositionSpace;
        public float uvRotate;
        public float uTiling;
        public float vTiling;
        public bool flipU;
        public bool flipV;
        public bool uv2Enabled;
        public float uv2Rotate;
        public float uv2UTiling;
        public float uv2VTiling;
        public bool flipU2;
        public bool flipV2;
        public float uvTileMax;
        public bool flipNormals;
        public Vector3 pivotOffset;
        public bool edgeTransp;
        public float transpAdj1;
        public float transpAdj2;
        public bool heightGradient;
        public Color heightTopColor;
        public Color heightBotColor;
        public SplinePresetCurve heightCurve;
        public bool noiseEnabled;
        public float noiseStrength;
        public Vector2 noiseTiling;
        public Vector2 noiseOffset;
        public NoiseAxis noiseAxis;
        public Vector3 noiseAxisCustom;
        public bool noiseMaskUVEnabled;
        public float noiseMaskUVFalloffU;
        public float noiseMaskUVFalloffV;
        public bool noiseMaskHeightEnabled;
        public SplinePresetCurve noiseMaskHeightCurve;
        public bool showChecker;
        public bool previewCullBack;
        public Vector2 uvPreviewSpeed;
        public Vector2 uv2PreviewSpeed;
        public Vector2 uvPreviewTile;
        public Vector2 uv2PreviewTile;
        public bool uvTilePreview;
        public bool useCustomMaterial;
        public bool showWireframe;
    }

    [Serializable]
    sealed class SplinePresetPath
    {
        // Always local coordinates; preview TRS preserves World Position editing.
        public List<Vector3> points;
        public List<float> flips;
    }

    [Serializable]
    sealed class SplinePresetAsset
    {
        public string guid;
        public string path;
        public long localId;
    }

    [Serializable]
    sealed class SplinePresetCurve
    {
        public WrapMode preWrapMode;
        public WrapMode postWrapMode;
        public List<SplinePresetKey> keys;

        public static SplinePresetCurve Capture(AnimationCurve curve)
        {
            if (curve == null) throw new InvalidOperationException("曲线为空，请先设置曲线。");
            var data = new SplinePresetCurve
            {
                preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode,
                keys = new List<SplinePresetKey>()
            };
            for (int i = 0; i < curve.length; i++)
            {
                Keyframe k = curve.keys[i];
                data.keys.Add(new SplinePresetKey
                {
                    time = k.time, value = k.value, inTangent = float.IsInfinity(k.inTangent) ? 0f : k.inTangent, outTangent = float.IsInfinity(k.outTangent) ? 0f : k.outTangent,
                    inStepped = float.IsInfinity(k.inTangent), outStepped = float.IsInfinity(k.outTangent),
                    inWeight = k.inWeight, outWeight = k.outWeight, weightedMode = k.weightedMode,
                    broken = AnimationUtility.GetKeyBroken(curve, i),
                    leftMode = AnimationUtility.GetKeyLeftTangentMode(curve, i),
                    rightMode = AnimationUtility.GetKeyRightTangentMode(curve, i)
                });
            }
            return data;
        }

        public AnimationCurve Restore()
        {
            var frames = new Keyframe[keys.Count];
            for (int i = 0; i < frames.Length; i++) frames[i] = keys[i].ToKeyframe();
            var curve = new AnimationCurve(frames) { preWrapMode = preWrapMode, postWrapMode = postWrapMode };
            for (int i = 0; i < frames.Length; i++)
            {
                AnimationUtility.SetKeyBroken(curve, i, keys[i].broken);
                AnimationUtility.SetKeyLeftTangentMode(curve, i, keys[i].leftMode);
                AnimationUtility.SetKeyRightTangentMode(curve, i, keys[i].rightMode);
            }
            // Setting editor modes may also recalculate neighboring slopes; restore all saved values last.
            for (int i = 0; i < frames.Length; i++)
            {
                Keyframe k = curve.keys[i];
                k.inTangent = frames[i].inTangent; k.outTangent = frames[i].outTangent;
                k.inWeight = frames[i].inWeight; k.outWeight = frames[i].outWeight;
                k.weightedMode = frames[i].weightedMode;
                curve.MoveKey(i, k);
            }
            return curve;
        }

        public void Validate(string name)
        {
            Require(keys != null && keys.Count <= 1024, name + "关键帧无效或过多。");
            Require(Enum.IsDefined(typeof(WrapMode), preWrapMode) && Enum.IsDefined(typeof(WrapMode), postWrapMode), name + "循环模式无效。");
            float previous = float.NegativeInfinity;
            foreach (var k in keys)
            {
                Require(k != null, name + "存在空关键帧。");
                CheckFinite(k.time, name); CheckFinite(k.value, name);
                Require(k.time > previous, name + "关键帧时间必须递增。");
                previous = k.time;
                // Unity uses infinity for stepped (Constant) keys, and JSON cannot represent it.
                CheckFinite(k.inTangent, name); CheckFinite(k.outTangent, name);
                CheckFinite(k.inWeight, name); CheckFinite(k.outWeight, name);
                Require(k.inWeight >= 0 && k.inWeight <= 1 && k.outWeight >= 0 && k.outWeight <= 1, name + "权重无效。");
                Require(Enum.IsDefined(typeof(WeightedMode), k.weightedMode), name + "权重模式无效。");
                Require(Enum.IsDefined(typeof(AnimationUtility.TangentMode), k.leftMode) && Enum.IsDefined(typeof(AnimationUtility.TangentMode), k.rightMode), name + "切线模式无效。");
            }
        }
    }

    [Serializable]
    sealed class SplinePresetKey
    {
        public float time, value, inTangent, outTangent, inWeight, outWeight;
        public bool inStepped, outStepped;
        public WeightedMode weightedMode;
        public bool broken;
        public AnimationUtility.TangentMode leftMode, rightMode;
        public Keyframe ToKeyframe()
        {
            return new Keyframe(time, value, inStepped ? float.PositiveInfinity : inTangent,
                outStepped ? float.PositiveInfinity : outTangent, inWeight, outWeight) { weightedMode = weightedMode };
        }
    }

    void DrawSplinePresetControls()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("保存样条数据", "保存全部 Spline 路径、控制点和形状设置，之后可读取并继续编辑。"))) SaveSplinePresetDialog();
        if (GUILayout.Button(new GUIContent("读取样条数据", "读取已保存的样条数据，替换当前 Spline 的路径和设置。"))) LoadSplinePresetDialog();
        EditorGUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(lastSplinePresetPath))
            EditorGUILayout.LabelField(new GUIContent("数据文件：" + Path.GetFileName(lastSplinePresetPath), lastSplinePresetPath), EditorStyles.miniLabel);
        EditorGUILayout.HelpBox("样条数据保存全部路径及当前设置，读取后可继续改点、调整曲线和新增路径。模型导出仍使用「保存模型」。", MessageType.None);
    }

    string SplinePresetDirectory()
    {
        string dir = string.IsNullOrEmpty(lastSplinePresetPath) ? Application.dataPath : Path.GetDirectoryName(lastSplinePresetPath);
        return Directory.Exists(dir) ? dir : Application.dataPath;
    }

    void SaveSplinePresetDialog()
    {
        string fileName = string.IsNullOrEmpty(lastSplinePresetPath) ? "SplinePreset" : Path.GetFileNameWithoutExtension(lastSplinePresetPath);
        string path = EditorUtility.SaveFilePanel("保存样条数据", SplinePresetDirectory(), fileName, "json");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            SaveSplinePresetFile(path);
            ShowNotification(new GUIContent("样条数据已保存"));
        }
        catch (Exception ex) { EditorUtility.DisplayDialog("保存样条数据失败", ex.Message, "确定"); }
    }

    void LoadSplinePresetDialog()
    {
        string path = EditorUtility.OpenFilePanel("读取样条数据", SplinePresetDirectory(), "json");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            string warnings = LoadSplinePresetFile(path);
            if (!string.IsNullOrEmpty(warnings)) EditorUtility.DisplayDialog("样条数据已读取", warnings, "确定");
            else ShowNotification(new GUIContent("样条数据已读取，可继续编辑"));
        }
        catch (Exception ex) { EditorUtility.DisplayDialog("读取样条数据失败", ex.Message + "\n当前编辑数据已保留。", "确定"); }
    }

    void SaveSplinePresetFile(string path)
    {
        var data = CaptureSplinePreset();
        ValidateSplinePreset(data);
        string json = JsonUtility.ToJson(data, true);
        Require(Encoding.UTF8.GetByteCount(json) <= SplinePresetMaxBytes, "样条数据超过 16 MB，无法保存。");
        string fullPath = Path.GetFullPath(path);
        string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            if (File.Exists(fullPath)) File.Replace(tempPath, fullPath, null);
            else File.Move(tempPath, fullPath);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        lastSplinePresetPath = fullPath;
        Repaint();
    }

    string LoadSplinePresetFile(string path)
    {
        Require(new FileInfo(path).Length <= SplinePresetMaxBytes, "样条文件超过 16 MB，请选择有效的样条数据。");
        var data = JsonUtility.FromJson<SplinePresetData>(File.ReadAllText(path, Encoding.UTF8));
        // Validate the complete document before touching current editing state.
        ValidateSplinePreset(data);
        var warnings = new List<string>();
        Texture2D noise = RestoreSplineAsset<Texture2D>(data.noiseTexture, "Noise 贴图", warnings);
        Texture2D texture = RestoreSplineAsset<Texture2D>(data.previewTexture, "预览贴图", warnings);
        Material material = RestoreSplineAsset<Material>(data.previewMaterial, "预览材质", warnings);
        ApplySplinePreset(data, noise, texture, material);
        lastSplinePresetPath = Path.GetFullPath(path);
        Repaint();
        return string.Join("\n", warnings.ToArray());
    }

    SplinePresetData CaptureSplinePreset()
    {
        EnsurePreview();
        var data = new SplinePresetData
        {
            format = SplinePresetFormat, schemaVersion = SplinePresetVersion, toolVersion = "1.9.0",
            paths = new List<SplinePresetPath>(), activePath = activeSplineIndex,
            previewPosition = previewGO.transform.position, previewRotation = previewGO.transform.rotation,
            previewScale = previewGO.transform.localScale, previewUV2 = PreviewIsChannel2(),
            noiseTexture = CaptureSplineAsset(noiseTex), previewTexture = CaptureSplineAsset(uvPreviewTex),
            previewMaterial = CaptureSplineAsset(customMaterial),
            projectName = projectName,
            splineRows = splineRows,
            splineCols = splineCols,
            splineW = splineW,
            splineWMax = splineWMax,
            splineCrossW = splineCrossW,
            splineCrossWMax = splineCrossWMax,
            splineShape = splineShape,
            splineTubeSides = splineTubeSides,
            splineTwist = splineTwist,
            splineWidthCurve = SplinePresetCurve.Capture(splineWidthCurve),
            splineSoftSelect = splineSoftSelect,
            splineSoftRadius = splineSoftRadius,
            splineSoftCurve = SplinePresetCurve.Capture(splineSoftCurve),
            pointTool = pointTool,
            pointSpace = pointSpace,
            splinePositionSpace = splinePositionSpace,
            uvRotate = uvRotate,
            uTiling = uTiling,
            vTiling = vTiling,
            flipU = flipU,
            flipV = flipV,
            uv2Enabled = uv2Enabled,
            uv2Rotate = uv2Rotate,
            uv2UTiling = uv2UTiling,
            uv2VTiling = uv2VTiling,
            flipU2 = flipU2,
            flipV2 = flipV2,
            uvTileMax = uvTileMax,
            flipNormals = flipNormals,
            pivotOffset = pivotOffset,
            edgeTransp = edgeTransp,
            transpAdj1 = transpAdj1,
            transpAdj2 = transpAdj2,
            heightGradient = heightGradient,
            heightTopColor = heightTopColor,
            heightBotColor = heightBotColor,
            heightCurve = SplinePresetCurve.Capture(heightCurve),
            noiseEnabled = noiseEnabled,
            noiseStrength = noiseStrength,
            noiseTiling = noiseTiling,
            noiseOffset = noiseOffset,
            noiseAxis = noiseAxis,
            noiseAxisCustom = noiseAxisCustom,
            noiseMaskUVEnabled = noiseMaskUVEnabled,
            noiseMaskUVFalloffU = noiseMaskUVFalloffU,
            noiseMaskUVFalloffV = noiseMaskUVFalloffV,
            noiseMaskHeightEnabled = noiseMaskHeightEnabled,
            noiseMaskHeightCurve = SplinePresetCurve.Capture(noiseMaskHeightCurve),
            showChecker = showChecker,
            previewCullBack = previewCullBack,
            uvPreviewSpeed = uvPreviewSpeed,
            uv2PreviewSpeed = uv2PreviewSpeed,
            uvPreviewTile = uvPreviewTile,
            uv2PreviewTile = uv2PreviewTile,
            uvTilePreview = uvTilePreview,
            useCustomMaterial = useCustomMaterial,
            showWireframe = showWireframe,
        };
        foreach (var path in splinePaths)
        {
            SyncSplineFlips(path);
            data.paths.Add(new SplinePresetPath { points = new List<Vector3>(path.points), flips = new List<float>(path.flips) });
        }
        return data;
    }

    void ApplySplinePreset(SplinePresetData data, Texture2D noise, Texture2D texture, Material material)
    {
        // Copy lists and curves so editing never mutates the loaded preset document.
        var paths = new List<SplinePath>();
        foreach (var path in data.paths)
            paths.Add(new SplinePath { points = new List<Vector3>(path.points), flips = new List<float>(path.flips) });
        splinePaths = paths; activeSplineIndex = data.activePath;
        projectName = data.projectName;
        splineRows = data.splineRows;
        splineCols = data.splineCols;
        splineW = data.splineW;
        splineWMax = data.splineWMax;
        splineCrossW = data.splineCrossW;
        splineCrossWMax = data.splineCrossWMax;
        splineShape = data.splineShape;
        splineTubeSides = data.splineTubeSides;
        splineTwist = data.splineTwist;
        splineWidthCurve = data.splineWidthCurve.Restore();
        splineSoftSelect = data.splineSoftSelect;
        splineSoftRadius = data.splineSoftRadius;
        splineSoftCurve = data.splineSoftCurve.Restore();
        pointTool = data.pointTool;
        pointSpace = data.pointSpace;
        splinePositionSpace = data.splinePositionSpace;
        uvRotate = data.uvRotate;
        uTiling = data.uTiling;
        vTiling = data.vTiling;
        flipU = data.flipU;
        flipV = data.flipV;
        uv2Enabled = data.uv2Enabled;
        uv2Rotate = data.uv2Rotate;
        uv2UTiling = data.uv2UTiling;
        uv2VTiling = data.uv2VTiling;
        flipU2 = data.flipU2;
        flipV2 = data.flipV2;
        uvTileMax = data.uvTileMax;
        flipNormals = data.flipNormals;
        pivotOffset = data.pivotOffset;
        edgeTransp = data.edgeTransp;
        transpAdj1 = data.transpAdj1;
        transpAdj2 = data.transpAdj2;
        heightGradient = data.heightGradient;
        heightTopColor = data.heightTopColor;
        heightBotColor = data.heightBotColor;
        heightCurve = data.heightCurve.Restore();
        noiseEnabled = data.noiseEnabled;
        noiseStrength = data.noiseStrength;
        noiseTiling = data.noiseTiling;
        noiseOffset = data.noiseOffset;
        noiseAxis = data.noiseAxis;
        noiseAxisCustom = data.noiseAxisCustom;
        noiseMaskUVEnabled = data.noiseMaskUVEnabled;
        noiseMaskUVFalloffU = data.noiseMaskUVFalloffU;
        noiseMaskUVFalloffV = data.noiseMaskUVFalloffV;
        noiseMaskHeightEnabled = data.noiseMaskHeightEnabled;
        noiseMaskHeightCurve = data.noiseMaskHeightCurve.Restore();
        showChecker = data.showChecker;
        previewCullBack = data.previewCullBack;
        uvPreviewSpeed = data.uvPreviewSpeed;
        uv2PreviewSpeed = data.uv2PreviewSpeed;
        uvPreviewTile = data.uvPreviewTile;
        uv2PreviewTile = data.uv2PreviewTile;
        uvTilePreview = data.uvTilePreview;
        useCustomMaterial = data.useCustomMaterial;
        showWireframe = data.showWireframe;
        noiseTex = noise; uvPreviewTex = texture; customMaterial = material;
        _noisePixels = null; _noiseTexRef = null;
        selectedSplinePoint = -1; selectedPoints.Clear(); isBoxSelecting = false;
        _ptRot = Quaternion.identity; _ptScale = Vector3.one; _ptStateHash = 0;
        _uvScrollAccum = Vector2.zero;
        meshType = MeshType.Spline; meshTypeFoldout = true; hidePreview = false;
        EnsurePreview();
        previewGO.transform.position = data.previewPosition;
        previewGO.transform.rotation = data.previewRotation;
        previewGO.transform.localScale = data.previewScale;
        if (checkerMat != null)
        {
            checkerMat.SetTexture("_MainTex", uvPreviewTex != null ? uvPreviewTex : checkerTex);
            checkerMat.SetFloat("_UsePreviewTex", uvPreviewTex != null ? 1f : 0f);
            checkerMat.SetFloat("_Cull", previewCullBack ? 2f : 0f);
            checkerMat.SetFloat("_UVChannel", data.previewUV2 && uv2Enabled ? 1f : 0f);
            checkerMat.SetVector("_Scroll", Vector4.zero);
            PushPreviewTile(data.previewUV2 && uv2Enabled);
        }
        Mesh previousMesh = previewMF != null ? previewMF.sharedMesh : null;
        RebuildMesh(); dirty = false;
        if (previousMesh != null && previousMesh != previewMF.sharedMesh) DestroyImmediate(previousMesh);
    }

    static SplinePresetAsset CaptureSplineAsset(UnityEngine.Object asset)
    {
        if (asset == null) return null;
        string path = AssetDatabase.GetAssetPath(asset);
        Require(!string.IsNullOrEmpty(path), "贴图或材质「" + asset.name + "」尚未保存为工程资源，请先保存该资源再保存样条数据。");
        string guid; long id;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out id);
        return new SplinePresetAsset { guid = guid, path = path, localId = id };
    }

    static T RestoreSplineAsset<T>(SplinePresetAsset reference, string label, List<string> warnings) where T : UnityEngine.Object
    {
        if (reference == null || (string.IsNullOrEmpty(reference.guid) && string.IsNullOrEmpty(reference.path))) return null;
        string path = string.IsNullOrEmpty(reference.guid) ? null : AssetDatabase.GUIDToAssetPath(reference.guid);
        if (string.IsNullOrEmpty(path)) path = reference.path;
        if (!string.IsNullOrEmpty(path))
        {
            if (reference.localId != 0)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    string guid; long id;
                    if (asset is T && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out id) && id == reference.localId) return (T)asset;
                }
            }
            else
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
        }
        warnings.Add(label + "未找到：" + reference.path + "。请重新指定该资源；样条和其它设置已恢复。");
        return null;
    }

    static void ValidateSplinePreset(SplinePresetData data)
    {
        Require(data != null && data.format == SplinePresetFormat, "这不是 VFX Mesh Generator 的样条数据文件。");
        Require(data.schemaVersion == SplinePresetVersion, "样条数据版本不兼容（" + data.schemaVersion + "），当前支持版本 1。");
        Require(data.paths != null && data.paths.Count >= 1 && data.paths.Count <= 256, "样条路径数量无效（应为 1～256）。");
        Require(data.activePath >= 0 && data.activePath < data.paths.Count, "当前路径索引无效。");
        int totalPoints = 0;
        foreach (var path in data.paths)
        {
            Require(path != null && path.points != null && path.points.Count >= 2 && path.points.Count <= 4096, "每条样条需要 2～4096 个控制点。");
            totalPoints += path.points.Count;
            Require(path.flips != null && path.flips.Count == path.points.Count, "控制点与翻转数据数量不一致。");
            foreach (var point in path.points) CheckFinite(point, "控制点");
            foreach (float flip in path.flips) CheckFinite(flip, "翻转角");
        }
        Require(totalPoints <= 16384, "控制点总数超过 16384。");
        Require(data.splineRows >= 1 && data.splineRows <= 20 && data.splineCols >= 4 && data.splineCols <= 256 && data.splineTubeSides >= 3 && data.splineTubeSides <= 32, "样条细分数量超出支持范围。");
        Require(Enum.IsDefined(typeof(SplineShape), data.splineShape) && Enum.IsDefined(typeof(PointTool), data.pointTool) && Enum.IsDefined(typeof(HandleSpace), data.pointSpace) && Enum.IsDefined(typeof(SplinePositionSpace), data.splinePositionSpace) && Enum.IsDefined(typeof(NoiseAxis), data.noiseAxis), "样条设置包含无效的模式。");
        long vertices = (long)data.paths.Count * (data.splineCols + 1) * (data.splineShape == SplineShape.Tube ? data.splineTubeSides + 1 : (data.splineRows + 1) * (data.splineShape == SplineShape.Cross ? 2 : 1));
        Require(vertices <= 1000000, "样条细分过高，生成顶点将超过 100 万。");
        CheckFinite(data.splineW, "splineW");
        CheckFinite(data.splineWMax, "splineWMax");
        CheckFinite(data.splineCrossW, "splineCrossW");
        CheckFinite(data.splineCrossWMax, "splineCrossWMax");
        CheckFinite(data.splineTwist, "splineTwist");
        Require(data.splineWidthCurve != null, "缺少曲线：splineWidthCurve。"); data.splineWidthCurve.Validate("splineWidthCurve");
        CheckFinite(data.splineSoftRadius, "splineSoftRadius");
        Require(data.splineSoftCurve != null, "缺少曲线：splineSoftCurve。"); data.splineSoftCurve.Validate("splineSoftCurve");
        CheckFinite(data.uvRotate, "uvRotate");
        CheckFinite(data.uTiling, "uTiling");
        CheckFinite(data.vTiling, "vTiling");
        CheckFinite(data.uv2Rotate, "uv2Rotate");
        CheckFinite(data.uv2UTiling, "uv2UTiling");
        CheckFinite(data.uv2VTiling, "uv2VTiling");
        CheckFinite(data.uvTileMax, "uvTileMax");
        CheckFinite(data.pivotOffset, "pivotOffset");
        CheckFinite(data.transpAdj1, "transpAdj1");
        CheckFinite(data.transpAdj2, "transpAdj2");
        CheckFinite(data.heightTopColor, "heightTopColor");
        CheckFinite(data.heightBotColor, "heightBotColor");
        Require(data.heightCurve != null, "缺少曲线：heightCurve。"); data.heightCurve.Validate("heightCurve");
        CheckFinite(data.noiseStrength, "noiseStrength");
        CheckFinite(data.noiseTiling, "noiseTiling");
        CheckFinite(data.noiseOffset, "noiseOffset");
        CheckFinite(data.noiseAxisCustom, "noiseAxisCustom");
        CheckFinite(data.noiseMaskUVFalloffU, "noiseMaskUVFalloffU");
        CheckFinite(data.noiseMaskUVFalloffV, "noiseMaskUVFalloffV");
        Require(data.noiseMaskHeightCurve != null, "缺少曲线：noiseMaskHeightCurve。"); data.noiseMaskHeightCurve.Validate("noiseMaskHeightCurve");
        CheckFinite(data.uvPreviewSpeed, "uvPreviewSpeed");
        CheckFinite(data.uv2PreviewSpeed, "uv2PreviewSpeed");
        CheckFinite(data.uvPreviewTile, "uvPreviewTile");
        CheckFinite(data.uv2PreviewTile, "uv2PreviewTile");
        Require(data.splineW >= 0.01f && data.splineWMax >= data.splineW && data.splineCrossW >= 0 && data.splineCrossWMax >= data.splineCrossW && data.splineSoftRadius > 0 && data.uvTileMax > 0, "宽度、上限或软选半径无效。");
        CheckFinite(data.previewPosition, "预览位置"); CheckFinite(data.previewScale, "预览缩放");
        CheckFinite(new Vector4(data.previewRotation.x, data.previewRotation.y, data.previewRotation.z, data.previewRotation.w), "预览旋转");
        float rotationLength = data.previewRotation.x * data.previewRotation.x + data.previewRotation.y * data.previewRotation.y + data.previewRotation.z * data.previewRotation.z + data.previewRotation.w * data.previewRotation.w;
        Require(Mathf.Abs(rotationLength - 1f) < 0.001f, "预览旋转数据无效。");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    static void CheckFinite(float value, string name) { Require(!float.IsNaN(value) && !float.IsInfinity(value), name + "包含无效数字。"); }
    static void CheckFinite(Vector2 value, string name) { CheckFinite(value.x, name); CheckFinite(value.y, name); }
    static void CheckFinite(Vector3 value, string name) { CheckFinite(value.x, name); CheckFinite(value.y, name); CheckFinite(value.z, name); }
    static void CheckFinite(Vector4 value, string name) { CheckFinite(value.x, name); CheckFinite(value.y, name); CheckFinite(value.z, name); CheckFinite(value.w, name); }
    static void CheckFinite(Color value, string name) { CheckFinite(value.r, name); CheckFinite(value.g, name); CheckFinite(value.b, name); CheckFinite(value.a, name); }
}