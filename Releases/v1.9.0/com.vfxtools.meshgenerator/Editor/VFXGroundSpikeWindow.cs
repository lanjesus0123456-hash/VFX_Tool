// VFX Ground Spike v1.8.0 — modular geometry/layout/growth and animated FBX.
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Fbx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public enum VFXSpikeShape { Cone, Crystal, Blade, Mixed }
public enum VFXSpikeLayoutMode { Line, Ring, Arc }
public enum VFXSpikeOrder { Sequential, CenterOut, Random }

[Serializable]
public sealed class VFXSpikeShapeModule
{
    public VFXSpikeShape shape = VFXSpikeShape.Mixed;
    public float width = 0.35f;
    public float depth = 0.3f;
    public float height = 1.5f;
    public float heightVariation = 0.25f;
    public int radialSides = 6;
    public float tiltVariation = 8f;
}

[Serializable]
public sealed class VFXSpikeLayoutModule
{
    public VFXSpikeLayoutMode mode = VFXSpikeLayoutMode.Line;
    public int count = 8;
    public float spacing = 0.75f;
    public float radius = 2f;
    public float arcAngle = 120f;
    public float jitter = 0.15f;
    public int seed = 1;
}

[Serializable]
public sealed class VFXSpikeGrowthModule
{
    public float duration = 0.6f;
    public float stagger = 0.08f;
    public VFXSpikeOrder order = VFXSpikeOrder.Sequential;
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
}

public sealed class VFXGroundSpikePiece
{
    public Mesh mesh;
    public Vector3 position;
    public float startTime;
}

public static class VFXGroundSpikeGenerator
{
    static float NextRange(System.Random random, float min, float max)
        => min + (float)random.NextDouble() * (max - min);

    static Vector3 RingPoint(int i, int sides, float width, float depth, float height,
                             float scale, VFXSpikeShape shape)
    {
        float angle = (float)i / sides * Mathf.PI * 2f;
        if (shape == VFXSpikeShape.Blade) angle += Mathf.PI * 0.25f;
        float x = shape == VFXSpikeShape.Blade ? Mathf.Sign(Mathf.Cos(angle)) * width * 0.5f
                                               : Mathf.Cos(angle) * width * 0.5f;
        float z = shape == VFXSpikeShape.Blade ? Mathf.Sign(Mathf.Sin(angle)) * depth * 0.5f
                                               : Mathf.Sin(angle) * depth * 0.5f;
        return new Vector3(x * scale, height, z * scale);
    }

    static Mesh BuildSpikeMesh(VFXSpikeShape shape, float width, float depth, float height, int requestedSides)
    {
        int sides = shape == VFXSpikeShape.Blade ? 4 : Mathf.Clamp(requestedSides, 3, 16);
        bool shoulder = shape != VFXSpikeShape.Cone;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var tris = new List<int>();
        for (int i = 0; i <= sides; i++)
        {
            vertices.Add(RingPoint(i, sides, width, depth, 0f, 1f, shape));
            uv.Add(new Vector2((float)i / sides, 0f));
        }
        int ring = sides + 1;
        if (shoulder)
        {
            float scale = shape == VFXSpikeShape.Blade ? 0.48f : 0.72f;
            float y = height * (shape == VFXSpikeShape.Blade ? 0.62f : 0.5f);
            for (int i = 0; i <= sides; i++)
            {
                vertices.Add(RingPoint(i, sides, width, depth, y, scale, shape));
                uv.Add(new Vector2((float)i / sides, y / height));
            }
        }
        int apex = vertices.Count;
        vertices.Add(new Vector3(shape == VFXSpikeShape.Blade ? width * 0.15f : 0f, height, 0f));
        uv.Add(new Vector2(0.5f, 1f));
        for (int i = 0; i < sides; i++)
        {
            if (shoulder)
            {
                tris.Add(i); tris.Add(ring + i); tris.Add(i + 1);
                tris.Add(i + 1); tris.Add(ring + i); tris.Add(ring + i + 1);
                tris.Add(ring + i); tris.Add(apex); tris.Add(ring + i + 1);
            }
            else { tris.Add(i); tris.Add(apex); tris.Add(i + 1); }
        }
        // 独立底盖顶点，避免底盖法线把侧壁抹平。
        int cap = vertices.Count;
        vertices.Add(Vector3.zero); uv.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < sides; i++)
        {
            Vector3 point = RingPoint(i, sides, width, depth, 0f, 1f, shape);
            vertices.Add(point);
            uv.Add(new Vector2(point.x / width + 0.5f, point.z / depth + 0.5f));
        }
        for (int i = 0; i < sides; i++)
        {
            tris.Add(cap + 1 + i);
            tris.Add(cap + 1 + ((i + 1) % sides));
            tris.Add(cap);
        }
        var mesh = new Mesh { name = "GroundSpike_" + shape };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static int[] BuildOrder(int count, VFXSpikeOrder mode, int seed)
    {
        var indices = new List<int>(count);
        for (int i = 0; i < count; i++) indices.Add(i);
        if (mode == VFXSpikeOrder.CenterOut)
        {
            float center = (count - 1) * 0.5f;
            indices.Sort((a, b) =>
            {
                int result = Mathf.Abs(a - center).CompareTo(Mathf.Abs(b - center));
                return result != 0 ? result : a.CompareTo(b);
            });
        }
        else if (mode == VFXSpikeOrder.Random)
        {
            var rng = new System.Random(seed ^ 0x53A71);
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int temp = indices[i]; indices[i] = indices[j]; indices[j] = temp;
            }
        }
        var rank = new int[count];
        for (int i = 0; i < count; i++) rank[indices[i]] = i;
        return rank;
    }

    public static List<VFXGroundSpikePiece> Generate(VFXSpikeShapeModule shape,
                                                      VFXSpikeLayoutModule layout,
                                                      VFXSpikeGrowthModule growth)
    {
        if (shape == null || layout == null || growth == null) throw new ArgumentNullException("modules");
        int count = Mathf.Clamp(layout.count, 1, 128);
        float width = Mathf.Max(0.01f, shape.width);
        float depth = Mathf.Max(0.01f, shape.depth);
        float height = Mathf.Max(0.01f, shape.height);
        var rng = new System.Random(layout.seed);
        int[] order = BuildOrder(count, growth.order, layout.seed);
        var pieces = new List<VFXGroundSpikePiece>(count);
        for (int i = 0; i < count; i++)
        {
            float angle = 0f;
            Vector3 position;
            if (layout.mode == VFXSpikeLayoutMode.Line)
                position = new Vector3((i - (count - 1) * 0.5f) * Mathf.Max(0.01f, layout.spacing), 0f, 0f);
            else
            {
                float normalized = layout.mode == VFXSpikeLayoutMode.Ring ? (float)i / count
                                   : count == 1 ? 0.5f : (float)i / (count - 1);
                angle = layout.mode == VFXSpikeLayoutMode.Ring ? normalized * 360f
                        : (normalized - 0.5f) * Mathf.Clamp(layout.arcAngle, 0f, 360f);
                float radians = angle * Mathf.Deg2Rad;
                position = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * Mathf.Max(0.01f, layout.radius);
            }
            position += new Vector3(NextRange(rng, -layout.jitter, layout.jitter), 0f,
                                    NextRange(rng, -layout.jitter, layout.jitter));
            VFXSpikeShape chosen = shape.shape == VFXSpikeShape.Mixed
                ? (VFXSpikeShape)rng.Next(0, 3) : shape.shape;
            float variedHeight = height * Mathf.Max(0.1f,
                1f + NextRange(rng, -shape.heightVariation, shape.heightVariation));
            var mesh = BuildSpikeMesh(chosen, width, depth, variedHeight, shape.radialSides);
            float tiltX = NextRange(rng, -shape.tiltVariation, shape.tiltVariation);
            float tiltZ = NextRange(rng, -shape.tiltVariation, shape.tiltVariation);
            Quaternion orientation = Quaternion.Euler(tiltX, angle, tiltZ);
            var vertices = mesh.vertices;
            for (int v = 0; v < vertices.Length; v++) vertices[v] = orientation * vertices[v];
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.name = "GroundSpike_" + (i + 1).ToString("D2") + "_" + chosen;
            pieces.Add(new VFXGroundSpikePiece
            {
                mesh = mesh,
                position = position,
                startTime = order[i] * Mathf.Max(0f, growth.stagger)
            });
        }
        return pieces;
    }

    public static float TotalDuration(IList<VFXGroundSpikePiece> pieces, VFXSpikeGrowthModule growth)
    {
        float max = 0f;
        foreach (var piece in pieces) max = Mathf.Max(max, piece.startTime);
        return max + Mathf.Max(0.05f, growth.duration);
    }

    public static float GrowthScale(VFXGroundSpikePiece piece, VFXSpikeGrowthModule growth, float time)
    {
        float normalized = Mathf.Clamp01((time - piece.startTime) / Mathf.Max(0.05f, growth.duration));
        if (time < piece.startTime) return 0.001f;
        return Mathf.Max(0.001f, growth.curve != null ? growth.curve.Evaluate(normalized) : normalized);
    }

    static void WriteMesh(FbxScene scene, FbxNode node, Mesh mesh)
    {
        var fbxMesh = FbxMesh.Create(scene, mesh.name + "_mesh");
        Vector3[] vertices = mesh.vertices, normals = mesh.normals;
        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;
        fbxMesh.InitControlPoints(vertices.Length);
        for (int i = 0; i < vertices.Length; i++)
            fbxMesh.SetControlPointAt(new FbxVector4(-vertices[i].x, vertices[i].y, vertices[i].z, 0), i);
        fbxMesh.CreateLayer();
        var layer = fbxMesh.GetLayer(0);
        var normalElement = FbxLayerElementNormal.Create(fbxMesh, "Normals");
        normalElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
        normalElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
        for (int i = 0; i < normals.Length; i++)
            normalElement.GetDirectArray().Add(new FbxVector4(-normals[i].x, normals[i].y, normals[i].z, 0));
        layer.SetNormals(normalElement);
        var uvElement = FbxLayerElementUV.Create(fbxMesh, "UVSet0");
        uvElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
        uvElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
        for (int i = 0; i < uv.Length; i++) uvElement.GetDirectArray().Add(new FbxVector2(uv[i].x, uv[i].y));
        layer.SetUVs(uvElement);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            fbxMesh.BeginPolygon();
            fbxMesh.AddPolygon(triangles[i]);
            fbxMesh.AddPolygon(triangles[i + 2]);
            fbxMesh.AddPolygon(triangles[i + 1]);
            fbxMesh.EndPolygon();
        }
        node.SetNodeAttribute(fbxMesh);
    }

    public static void ExportFbx(IList<VFXGroundSpikePiece> pieces, VFXSpikeGrowthModule growth,
                                 string path, bool animated)
    {
        if (pieces == null || pieces.Count == 0) throw new ArgumentException("没有可导出的地刺");
        using (var manager = FbxManager.Create())
        {
            var scene = FbxScene.Create(manager, "GroundSpike");
            scene.GetGlobalSettings().SetSystemUnit(FbxSystemUnit.m);
            FbxAnimStack stack = null;
            FbxAnimLayer animLayer = null;
            if (animated)
            {
                stack = FbxAnimStack.Create(scene, "GroundSpike_Growth");
                animLayer = FbxAnimLayer.Create(scene, "BaseLayer");
                stack.AddMember(animLayer);
                stack.SetLocalTimeSpan(new FbxTimeSpan(FbxTime.FromSecondDouble(0),
                    FbxTime.FromSecondDouble(TotalDuration(pieces, growth))));
                scene.SetCurrentAnimationStack(stack);
            }
            foreach (var piece in pieces)
            {
                var node = FbxNode.Create(scene, piece.mesh.name);
                node.LclTranslation.Set(new FbxDouble3(-piece.position.x, piece.position.y, piece.position.z));
                WriteMesh(scene, node, piece.mesh);
                scene.GetRootNode().AddChild(node);
                if (!animated) continue;
                node.LclScaling.Set(new FbxDouble3(1d, 0.001d, 1d));
                var curve = node.LclScaling.GetCurve(animLayer, "Y", true);
                if (curve == null) throw new InvalidOperationException("无法创建 FBX 生长缩放曲线");
                curve.KeyModifyBegin();
                if (piece.startTime > 0.0001f)
                {
                    var zero = FbxTime.FromSecondDouble(0);
                    int zeroKey = curve.KeyAdd(zero);
                    curve.KeySet(zeroKey, zero, 0.001f, FbxAnimCurveDef.EInterpolationType.eInterpolationLinear);
                }
                for (int sample = 0; sample <= 16; sample++)
                {
                    float time = piece.startTime + Mathf.Max(0.05f, growth.duration) * sample / 16f;
                    var fbxTime = FbxTime.FromSecondDouble(time);
                    int key = curve.KeyAdd(fbxTime);
                    curve.KeySet(key, fbxTime, GrowthScale(piece, growth, time),
                                 FbxAnimCurveDef.EInterpolationType.eInterpolationLinear);
                }
                curve.KeyModifyEnd();
            }
            var exporter = FbxExporter.Create(manager, "Exporter");
            int format = manager.GetIOPluginRegistry().FindWriterIDByDescription("FBX binary (*.fbx)");
            if (!exporter.Initialize(path, format, manager.GetIOSettings()))
                throw new IOException("FBX 导出器初始化失败：" + path);
            if (!exporter.Export(scene)) throw new IOException("FBX 写入失败：" + path);
            exporter.Destroy();
        }
    }
}

public sealed class VFXGroundSpikeWindow : EditorWindow
{
    VFXSpikeShapeModule shape = new VFXSpikeShapeModule();
    VFXSpikeLayoutModule layout = new VFXSpikeLayoutModule();
    VFXSpikeGrowthModule growth = new VFXSpikeGrowthModule();
    List<VFXGroundSpikePiece> pieces;
    GameObject previewRoot;
    Material previewMaterial;
    float previewTime;
    bool shapeFold = true, layoutFold = true, growthFold = true;

    [MenuItem("Tools/VFX Ground Spike")]
    public static void Open() => GetWindow<VFXGroundSpikeWindow>("VFX Ground Spike");
    void OnDisable() => ClearPreview();

    void OnGUI()
    {
        shapeFold = EditorGUILayout.Foldout(shapeFold, "形状模块", true);
        if (shapeFold)
        {
            shape.shape = (VFXSpikeShape)EditorGUILayout.EnumPopup("形状", shape.shape);
            shape.width = Mathf.Max(0.01f, EditorGUILayout.FloatField("宽度", shape.width));
            shape.depth = Mathf.Max(0.01f, EditorGUILayout.FloatField("厚度", shape.depth));
            shape.height = Mathf.Max(0.01f, EditorGUILayout.FloatField("高度", shape.height));
            shape.heightVariation = EditorGUILayout.Slider("高度随机比例", shape.heightVariation, 0f, 0.8f);
            shape.radialSides = EditorGUILayout.IntSlider("锥/晶截面边数", shape.radialSides, 3, 16);
            shape.tiltVariation = EditorGUILayout.Slider("倾斜随机角度", shape.tiltVariation, 0f, 35f);
        }
        layoutFold = EditorGUILayout.Foldout(layoutFold, "排列模块", true);
        if (layoutFold)
        {
            layout.mode = (VFXSpikeLayoutMode)EditorGUILayout.EnumPopup("排列", layout.mode);
            layout.count = EditorGUILayout.IntSlider("数量", layout.count, 1, 64);
            if (layout.mode == VFXSpikeLayoutMode.Line)
                layout.spacing = Mathf.Max(0.01f, EditorGUILayout.FloatField("间距", layout.spacing));
            else
            {
                layout.radius = Mathf.Max(0.01f, EditorGUILayout.FloatField("半径", layout.radius));
                if (layout.mode == VFXSpikeLayoutMode.Arc)
                    layout.arcAngle = EditorGUILayout.Slider("弧度", layout.arcAngle, 0f, 360f);
            }
            layout.jitter = EditorGUILayout.Slider("位置扰动", layout.jitter, 0f, 1f);
            layout.seed = EditorGUILayout.IntField("随机种子", layout.seed);
        }
        growthFold = EditorGUILayout.Foldout(growthFold, "生长时序模块", true);
        if (growthFold)
        {
            growth.duration = Mathf.Max(0.05f, EditorGUILayout.FloatField("单根生长时长(秒)", growth.duration));
            growth.stagger = Mathf.Max(0f, EditorGUILayout.FloatField("错峰间隔(秒)", growth.stagger));
            growth.order = (VFXSpikeOrder)EditorGUILayout.EnumPopup("生长顺序", growth.order);
            growth.curve = EditorGUILayout.CurveField("生长曲线", growth.curve);
        }
        EditorGUILayout.HelpBox("先生成，拖时间预览，再分别导出最终模型或生长动画 FBX。动画使用各地刺节点的 Y 缩放关键帧；导入时保留节点层级与变换动画。", MessageType.Info);
        if (GUILayout.Button("生成 / 刷新地刺")) GeneratePreview();
        if (pieces != null)
        {
            float total = VFXGroundSpikeGenerator.TotalDuration(pieces, growth);
            float time = EditorGUILayout.Slider("预览时间(秒)", previewTime, 0f, total);
            if (!Mathf.Approximately(time, previewTime)) { previewTime = time; UpdatePreview(); }
            EditorGUILayout.LabelField("已生成 " + pieces.Count + " 根；动画总长 " + total.ToString("F2") + " 秒");
        }
        GUI.enabled = pieces != null && pieces.Count > 0;
        if (GUILayout.Button("导出最终模型 FBX")) Export(false);
        if (GUILayout.Button("导出生长动画 FBX")) Export(true);
        GUI.enabled = true;
    }

    void GeneratePreview()
    {
        try
        {
            ClearPreview();
            pieces = VFXGroundSpikeGenerator.Generate(shape, layout, growth);
            previewRoot = new GameObject("_VFXGroundSpikePreview") { hideFlags = HideFlags.HideAndDontSave };
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            if (shader != null) previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            foreach (var piece in pieces)
            {
                var child = new GameObject(piece.mesh.name) { hideFlags = HideFlags.HideAndDontSave };
                child.transform.SetParent(previewRoot.transform, false);
                child.transform.localPosition = piece.position;
                child.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = previewMaterial;
            }
            previewTime = VFXGroundSpikeGenerator.TotalDuration(pieces, growth);
            UpdatePreview();
        }
        catch (Exception e) { ClearPreview(); Debug.LogException(e); EditorUtility.DisplayDialog("VFX Ground Spike", e.Message, "确定"); }
    }

    void UpdatePreview()
    {
        if (previewRoot == null || pieces == null) return;
        for (int i = 0; i < pieces.Count; i++)
            previewRoot.transform.GetChild(i).localScale = new Vector3(1f,
                VFXGroundSpikeGenerator.GrowthScale(pieces[i], growth, previewTime), 1f);
        SceneView.RepaintAll();
    }

    void Export(bool animated)
    {
        string title = animated ? "导出生长动画 FBX" : "导出最终模型 FBX";
        string name = animated ? "VFX_GroundSpike_Growth" : "VFX_GroundSpike_Final";
        string path = EditorUtility.SaveFilePanel(title, Application.dataPath, name, "fbx");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            VFXGroundSpikeGenerator.ExportFbx(pieces, growth, path, animated);
            if (path.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/')))
                AssetDatabase.Refresh();
            Debug.Log("[VFX Ground Spike] 已导出：" + path);
        }
        catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("VFX Ground Spike", e.Message, "确定"); }
    }

    void ClearPreview()
    {
        if (previewRoot != null) DestroyImmediate(previewRoot);
        previewRoot = null;
        if (pieces != null)
            foreach (var piece in pieces) if (piece.mesh != null) DestroyImmediate(piece.mesh);
        pieces = null;
        if (previewMaterial != null) DestroyImmediate(previewMaterial);
        previewMaterial = null;
    }
}