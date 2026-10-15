// VFX Fracture v1.7.0 — connected surface shards with thickness and multi-node FBX export.
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Fbx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class VFXFracturePiece
{
    public Mesh mesh;
    public Vector3 pivot;
}

public static class VFXFractureGenerator
{
    struct EdgeUse
    {
        public int a, b, count;
        public EdgeUse(int from, int to) { a = from; b = to; count = 1; }
    }

    static ulong EdgeKey(int a, int b)
    {
        uint lo = (uint)Mathf.Min(a, b);
        uint hi = (uint)Mathf.Max(a, b);
        return ((ulong)lo << 32) | hi;
    }

    static void AddEdge(Dictionary<ulong, EdgeUse> edges, int a, int b)
    {
        ulong key = EdgeKey(a, b);
        EdgeUse use;
        if (edges.TryGetValue(key, out use)) { use.count++; edges[key] = use; }
        else edges.Add(key, new EdgeUse(a, b));
    }

    static Vector3[] ResolveNormals(Vector3[] vertices, int[] triangles, Vector3[] normals)
    {
        if (normals != null && normals.Length == vertices.Length) return normals;
        normals = new Vector3[vertices.Length];
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            normals[a] += face; normals[b] += face; normals[c] += face;
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].sqrMagnitude > 1e-8f ? normals[i].normalized : Vector3.up;
        return normals;
    }

    static int AddFrontVertex(int original, Dictionary<int, int> map, List<int> originals,
                              List<Vector3> vertices, List<Vector2> uv, List<Color> colors,
                              Vector3[] sourceVertices, Vector2[] sourceUV, Vector3 pivot, Color color)
    {
        int index;
        if (map.TryGetValue(original, out index)) return index;
        index = vertices.Count;
        map.Add(original, index);
        originals.Add(original);
        vertices.Add(sourceVertices[original] - pivot);
        uv.Add(sourceUV.Length == sourceVertices.Length ? sourceUV[original]
                                                      : new Vector2(sourceVertices[original].x, sourceVertices[original].z));
        colors.Add(color);
        return index;
    }

    static VFXFracturePiece BuildPiece(int id, List<int> faces, Vector3[] sourceVertices,
                                        Vector3[] sourceNormals, Vector2[] sourceUV,
                                        int[] triangles, float thickness, int total)
    {
        var unique = new HashSet<int>();
        foreach (int face in faces)
            for (int k = 0; k < 3; k++) unique.Add(triangles[face * 3 + k]);
        Vector3 pivot = Vector3.zero;
        foreach (int index in unique) pivot += sourceVertices[index];
        pivot /= Mathf.Max(1, unique.Count);

        Color color = Color.HSVToRGB((id + 0.37f) / Mathf.Max(1, total), 0.55f, 1f);
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var colors = new List<Color>();
        var tris = new List<int>();
        var originals = new List<int>();
        var map = new Dictionary<int, int>();
        var edges = new Dictionary<ulong, EdgeUse>();

        foreach (int face in faces)
        {
            int a = triangles[face * 3], b = triangles[face * 3 + 1], c = triangles[face * 3 + 2];
            tris.Add(AddFrontVertex(a, map, originals, vertices, uv, colors, sourceVertices, sourceUV, pivot, color));
            tris.Add(AddFrontVertex(b, map, originals, vertices, uv, colors, sourceVertices, sourceUV, pivot, color));
            tris.Add(AddFrontVertex(c, map, originals, vertices, uv, colors, sourceVertices, sourceUV, pivot, color));
            AddEdge(edges, a, b); AddEdge(edges, b, c); AddEdge(edges, c, a);
        }

        int frontVertices = vertices.Count;
        int frontIndices = tris.Count;
        for (int i = 0; i < frontVertices; i++)
        {
            int original = originals[i];
            vertices.Add(vertices[i] - sourceNormals[original] * thickness);
            uv.Add(uv[i]);
            colors.Add(color);
        }
        for (int i = 0; i < frontIndices; i += 3)
        {
            tris.Add(tris[i + 2] + frontVertices);
            tris.Add(tris[i + 1] + frontVertices);
            tris.Add(tris[i] + frontVertices);
        }

        foreach (EdgeUse edge in edges.Values)
        {
            if (edge.count != 1) continue;
            int start = vertices.Count;
            Vector3 topA = sourceVertices[edge.a] - pivot;
            Vector3 topB = sourceVertices[edge.b] - pivot;
            Vector3 botA = topA - sourceNormals[edge.a] * thickness;
            Vector3 botB = topB - sourceNormals[edge.b] * thickness;
            vertices.Add(topA); vertices.Add(botA); vertices.Add(topB); vertices.Add(botB);
            float length = Vector3.Distance(topA, topB);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(length, 0f)); uv.Add(new Vector2(length, 1f));
            for (int k = 0; k < 4; k++) colors.Add(color);
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start + 2); tris.Add(start + 1); tris.Add(start + 3);
        }

        var mesh = new Mesh { name = "Fracture_Piece_" + id.ToString("D2") };
        if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return new VFXFracturePiece { mesh = mesh, pivot = pivot };
    }

    public static List<VFXFracturePiece> Generate(Mesh source, int requestedPieces, float thickness, int seed)
    {
        if (source == null) throw new ArgumentNullException("source");
        Vector3[] vertices = source.vertices;
        int[] triangles = source.triangles;
        if (vertices.Length < 3 || triangles.Length < 3 || triangles.Length % 3 != 0)
            throw new ArgumentException("源模型需要至少一个有效三角面");
        int faceCount = triangles.Length / 3;
        int pieceCount = Mathf.Clamp(requestedPieces, 1, faceCount);
        thickness = Mathf.Max(0.001f, thickness);
        Vector3[] normals = ResolveNormals(vertices, triangles, source.normals);
        Vector2[] sourceUV = source.uv;

        var centroids = new Vector3[faceCount];
        var neighbors = new List<int>[faceCount];
        for (int i = 0; i < faceCount; i++)
        {
            centroids[i] = (vertices[triangles[i * 3]] + vertices[triangles[i * 3 + 1]]
                          + vertices[triangles[i * 3 + 2]]) / 3f;
            neighbors[i] = new List<int>();
        }
        var first = new Dictionary<ulong, int>();
        for (int face = 0; face < faceCount; face++)
            for (int k = 0; k < 3; k++)
            {
                int a = triangles[face * 3 + k], b = triangles[face * 3 + (k + 1) % 3];
                ulong key = EdgeKey(a, b);
                int other;
                if (first.TryGetValue(key, out other))
                {
                    neighbors[face].Add(other);
                    neighbors[other].Add(face);
                }
                else first.Add(key, face);
            }

        var seeds = new int[pieceCount];
        var picked = new bool[faceCount];
        seeds[0] = new System.Random(seed).Next(faceCount);
        picked[seeds[0]] = true;
        for (int shard = 1; shard < pieceCount; shard++)
        {
            float bestDistance = -1f;
            int best = -1;
            for (int face = 0; face < faceCount; face++)
            {
                if (picked[face]) continue;
                float nearest = float.MaxValue;
                for (int i = 0; i < shard; i++)
                    nearest = Mathf.Min(nearest, (centroids[face] - centroids[seeds[i]]).sqrMagnitude);
                if (nearest > bestDistance) { bestDistance = nearest; best = face; }
            }
            seeds[shard] = best;
            picked[best] = true;
        }

        var owner = new int[faceCount];
        for (int i = 0; i < owner.Length; i++) owner[i] = -1;
        var queue = new Queue<int>();
        for (int i = 0; i < pieceCount; i++) { owner[seeds[i]] = i; queue.Enqueue(seeds[i]); }
        while (queue.Count > 0)
        {
            int face = queue.Dequeue();
            foreach (int next in neighbors[face])
                if (owner[next] < 0) { owner[next] = owner[face]; queue.Enqueue(next); }
        }
        for (int face = 0; face < faceCount; face++)
        {
            if (owner[face] >= 0) continue;
            float best = float.MaxValue;
            for (int shard = 0; shard < pieceCount; shard++)
            {
                float d = (centroids[face] - centroids[seeds[shard]]).sqrMagnitude;
                if (d < best) { best = d; owner[face] = shard; }
            }
        }

        var groups = new List<int>[pieceCount];
        for (int i = 0; i < pieceCount; i++) groups[i] = new List<int>();
        for (int face = 0; face < faceCount; face++) groups[owner[face]].Add(face);
        var result = new List<VFXFracturePiece>(pieceCount);
        for (int i = 0; i < pieceCount; i++)
            result.Add(BuildPiece(i, groups[i], vertices, normals, sourceUV, triangles, thickness, pieceCount));
        return result;
    }

    public static void ExportFbx(IList<VFXFracturePiece> pieces, string path)
    {
        if (pieces == null || pieces.Count == 0) throw new ArgumentException("没有可导出的碎片");
        using (var manager = FbxManager.Create())
        {
            var scene = FbxScene.Create(manager, "Fracture");
            scene.GetGlobalSettings().SetSystemUnit(FbxSystemUnit.m);
            foreach (var piece in pieces)
            {
                Mesh mesh = piece.mesh;
                var node = FbxNode.Create(scene, mesh.name);
                node.LclTranslation.Set(new FbxDouble3(-piece.pivot.x, piece.pivot.y, piece.pivot.z));
                var fbxMesh = FbxMesh.Create(scene, mesh.name + "_mesh");
                Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                Vector2[] uv = mesh.uv;
                Color[] colors = mesh.colors;
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
                var colorElement = FbxLayerElementVertexColor.Create(fbxMesh, "Colors");
                colorElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                colorElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                for (int i = 0; i < colors.Length; i++)
                    colorElement.GetDirectArray().Add(new FbxColor(colors[i].r, colors[i].g, colors[i].b, colors[i].a));
                layer.SetVertexColors(colorElement);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    fbxMesh.BeginPolygon();
                    fbxMesh.AddPolygon(triangles[i]);
                    fbxMesh.AddPolygon(triangles[i + 2]);
                    fbxMesh.AddPolygon(triangles[i + 1]);
                    fbxMesh.EndPolygon();
                }
                node.SetNodeAttribute(fbxMesh);
                scene.GetRootNode().AddChild(node);
            }
            var exporter = FbxExporter.Create(manager, "Exporter");
            int format = manager.GetIOPluginRegistry().FindWriterIDByDescription("FBX binary (*.fbx)");
            if (!exporter.Initialize(path, format, manager.GetIOSettings()))
                throw new IOException("FBX 导出器初始化失败：" + path);
            exporter.Export(scene);
            exporter.Destroy();
        }
    }
}

public sealed class VFXFractureWindow : EditorWindow
{
    MeshFilter sourceFilter;
    Mesh sourceMesh;
    int pieceCount = 12;
    float thickness = 0.08f;
    int seed = 1;
    float previewSpread;
    List<VFXFracturePiece> pieces;
    GameObject previewRoot;
    Material previewMaterial;

    [MenuItem("Tools/VFX Fracture")]
    public static void Open() => GetWindow<VFXFractureWindow>("VFX Fracture");

    void OnDisable() => ClearPreview();

    Mesh InputMesh => sourceMesh != null ? sourceMesh
                   : sourceFilter != null ? sourceFilter.sharedMesh : null;

    void OnGUI()
    {
        EditorGUILayout.LabelField("破碎初版：连续表面分片 + 厚度", EditorStyles.boldLabel);
        sourceFilter = (MeshFilter)EditorGUILayout.ObjectField("场景 MeshFilter", sourceFilter, typeof(MeshFilter), true);
        sourceMesh = (Mesh)EditorGUILayout.ObjectField("或 Mesh 资源", sourceMesh, typeof(Mesh), false);
        pieceCount = EditorGUILayout.IntSlider("碎片数量", pieceCount, 2, 64);
        thickness = Mathf.Max(0.001f, EditorGUILayout.FloatField("碎片厚度", thickness));
        seed = EditorGUILayout.IntField("随机种子", seed);
        float spread = EditorGUILayout.Slider("预览散开", previewSpread, 0f, 2f);
        if (!Mathf.Approximately(spread, previewSpread)) { previewSpread = spread; UpdateSpread(); }
        EditorGUILayout.HelpBox("按相邻三角面分组，给每块补背面和侧壁。适合特效表面破碎初稿；厚壁、凹体的精确体积切割留待后续版本。导出为一个 FBX，内部每块是独立节点且轴点在碎片中心。", MessageType.Info);
        if (GUILayout.Button("生成碎片")) GeneratePreview();
        GUI.enabled = pieces != null && pieces.Count > 0;
        if (GUILayout.Button("导出 FBX")) Export();
        GUI.enabled = true;
        if (pieces != null) EditorGUILayout.LabelField("已生成 " + pieces.Count + " 块", EditorStyles.miniLabel);
    }

    void GeneratePreview()
    {
        Mesh input = InputMesh;
        if (input == null) { EditorUtility.DisplayDialog("VFX Fracture", "请指定场景 MeshFilter 或 Mesh 资源。", "确定"); return; }
        try
        {
            ClearPreview();
            pieces = VFXFractureGenerator.Generate(input, pieceCount, thickness, seed);
            previewRoot = new GameObject("_VFXFracturePreview") { hideFlags = HideFlags.HideAndDontSave };
            if (sourceFilter != null && sourceMesh == null)
            {
                previewRoot.transform.position = sourceFilter.transform.position;
                previewRoot.transform.rotation = sourceFilter.transform.rotation;
                previewRoot.transform.localScale = sourceFilter.transform.lossyScale;
            }
            Material sourceMaterial = sourceFilter != null ? sourceFilter.GetComponent<MeshRenderer>()?.sharedMaterial : null;
            if (sourceMaterial == null)
            {
                Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
                if (shader != null) previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            foreach (var piece in pieces)
            {
                var go = new GameObject(piece.mesh.name) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(previewRoot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = sourceMaterial != null ? sourceMaterial : previewMaterial;
            }
            UpdateSpread();
            SceneView.RepaintAll();
        }
        catch (Exception e)
        {
            ClearPreview();
            Debug.LogException(e);
            EditorUtility.DisplayDialog("VFX Fracture", e.Message, "确定");
        }
    }

    void UpdateSpread()
    {
        if (previewRoot == null || pieces == null) return;
        Vector3 center = Vector3.zero;
        foreach (var piece in pieces) center += piece.pivot;
        center /= pieces.Count;
        for (int i = 0; i < pieces.Count; i++)
        {
            Vector3 dir = pieces[i].pivot - center;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            previewRoot.transform.GetChild(i).localPosition = pieces[i].pivot + dir.normalized * previewSpread;
        }
        SceneView.RepaintAll();
    }

    void Export()
    {
        string path = EditorUtility.SaveFilePanel("导出破碎 FBX", Application.dataPath, "VFX_Fracture", "fbx");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            VFXFractureGenerator.ExportFbx(pieces, path);
            if (path.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/')))
                AssetDatabase.Refresh();
            Debug.Log("[VFX Fracture] 已导出：" + path);
        }
        catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("VFX Fracture", e.Message, "确定"); }
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