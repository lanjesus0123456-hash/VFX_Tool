using System.Collections.Generic;
using UnityEngine;

namespace VFXTools.SplineMesh
{
    /// <summary>
    /// 运行时组件：沿 Catmull-Rom 样条线程序化生成面片 Mesh。
    /// 挂载到任意 GameObject 后，配合 Editor 脚本在 Scene 视图中交互编辑控制点。
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [ExecuteInEditMode]
    public class VFXSplineMeshGenerator : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // 可序列化参数（Inspector 暴露）
        // ─────────────────────────────────────────────

        [Header("控制点")]
        [Tooltip("样条线控制点列表（至少需要 2 个点）")]
        public List<Vector3> controlPoints = new List<Vector3>
        {
            new Vector3(-2f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f),
            new Vector3( 0.5f, -1f, 0f),
            new Vector3( 2f, 0f, 0f),
        };

        [Header("Mesh 参数")]
        [Tooltip("面片宽度（世界单位）")]
        public float width = 0.5f;

        [Tooltip("沿样条线的细分段数（越高越平滑，性能开销越大）")]
        [Range(4, 256)]
        public int segments = 32;

        [Header("UV 参数")]
        [Tooltip("U 轴（流动方向）Tiling 倍数，1 = 不重复，2 = 重复两次")]
        public float uTiling = 1f;

        // ─────────────────────────────────────────────
        // 内部状态
        // ─────────────────────────────────────────────

        private int   _cachedSegments;
        private float _cachedWidth;
        private float _cachedUTiling;
        private List<Vector3> _cachedPoints = new List<Vector3>();

        // 复用的数组，避免 GenerateMesh 每次 new
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private Vector2[] _uvs;
        private int[]     _triangles;

        private MeshFilter   _meshFilter;
        private Mesh         _mesh;

        // ─────────────────────────────────────────────
        // Unity 生命周期
        // ─────────────────────────────────────────────

        private void OnEnable()
        {
            _meshFilter = GetComponent<MeshFilter>();

            if (_mesh == null)
            {
                _mesh = new Mesh();
                _mesh.name = "VFXSplineMesh";
                // [FIX] 防止编辑器将运行时 Mesh 序列化进场景
                _mesh.hideFlags = HideFlags.HideAndDontSave;
            }
            _meshFilter.sharedMesh = _mesh;

            GenerateMesh();
        }

        private void OnDisable()
        {
            _cachedSegments = -1;
        }

        private void OnDestroy()
        {
            // [FIX] 显式销毁 Mesh 防止泄漏
            if (_mesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_mesh);
                else
                    DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        private void Update()
        {
            if (IsDirty())
            {
                GenerateMesh();
                CacheCurrentState();
            }
        }

        // ─────────────────────────────────────────────
        // 脏检测
        // ─────────────────────────────────────────────

        private bool IsDirty()
        {
            if (_cachedSegments != segments) return true;
            if (!Mathf.Approximately(_cachedWidth,   width))   return true;
            if (!Mathf.Approximately(_cachedUTiling, uTiling)) return true;
            if (_cachedPoints.Count != controlPoints.Count)    return true;

            for (int i = 0; i < controlPoints.Count; i++)
            {
                if (_cachedPoints[i] != controlPoints[i]) return true;
            }
            return false;
        }

        private void CacheCurrentState()
        {
            _cachedSegments = segments;
            _cachedWidth    = width;
            _cachedUTiling  = uTiling;

            _cachedPoints.Clear();
            foreach (var p in controlPoints)
                _cachedPoints.Add(p);
        }

        // ─────────────────────────────────────────────
        // 核心：Mesh 生成
        // ─────────────────────────────────────────────

        public void GenerateMesh()
        {
            if (controlPoints == null || controlPoints.Count < 2)
                return;

            if (_mesh == null || _meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
                _mesh = new Mesh { name = "VFXSplineMesh", hideFlags = HideFlags.HideAndDontSave };
                _meshFilter.sharedMesh = _mesh;
            }

            // ── 1. 采样样条线上的点、切线 ──
            int sampleCount = segments + 1;
            Vector3[] splinePoints   = new Vector3[sampleCount];
            Vector3[] splineTangents = new Vector3[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / segments;
                splinePoints[i]   = EvaluateCatmullRom(t);
                splineTangents[i] = EvaluateCatmullRomTangent(t).normalized;

                // [FIX] 安全检查：零切线 fallback
                if (splineTangents[i].sqrMagnitude < 1e-6f)
                    splineTangents[i] = (i > 0) ? splineTangents[i - 1] : Vector3.forward;
            }

            // ── 2. 旋转最小化框架 ──
            Vector3[] frameNormals;
            Vector3[] frameBinormals;
            BuildRotationMinimizingFrames(splineTangents, out frameNormals, out frameBinormals);

            // ── 3. 计算弧长数组 ──
            float[] arcLengths = ComputeArcLengths(splinePoints);
            float totalLength  = arcLengths[sampleCount - 1];
            if (totalLength < 1e-6f) totalLength = 1e-6f;

            // ── 4. 准备顶点数组 ──
            int vertCount = sampleCount * 2;
            int triCount  = segments * 6;

            if (_vertices  == null || _vertices.Length  != vertCount) _vertices  = new Vector3[vertCount];
            if (_normals   == null || _normals.Length   != vertCount) _normals   = new Vector3[vertCount];
            if (_uvs       == null || _uvs.Length       != vertCount) _uvs       = new Vector2[vertCount];
            if (_triangles == null || _triangles.Length != triCount)  _triangles = new int[triCount];

            // ── 5. 填充顶点、法线、UV ──
            float halfWidth = width * 0.5f;

            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 center   = splinePoints[i];
                Vector3 binormal = frameBinormals[i];
                Vector3 normal   = frameNormals[i];

                float u = (arcLengths[i] / totalLength) * uTiling;

                int leftIdx  = i * 2;
                int rightIdx = i * 2 + 1;

                _vertices[leftIdx]  = center - binormal * halfWidth;
                _normals [leftIdx]  = normal;
                _uvs     [leftIdx]  = new Vector2(u, 0f);

                _vertices[rightIdx] = center + binormal * halfWidth;
                _normals [rightIdx] = normal;
                _uvs     [rightIdx] = new Vector2(u, 1f);
            }

            // ── 6. 填充三角形索引 ──
            for (int i = 0; i < segments; i++)
            {
                int bl = i * 2;
                int br = i * 2 + 1;
                int tl = i * 2 + 2;
                int tr = i * 2 + 3;

                int triBase = i * 6;
                _triangles[triBase + 0] = bl;
                _triangles[triBase + 1] = tl;
                _triangles[triBase + 2] = br;
                _triangles[triBase + 3] = br;
                _triangles[triBase + 4] = tl;
                _triangles[triBase + 5] = tr;
            }

            // ── 7. 写入 Mesh ──
            _mesh.Clear();
            _mesh.vertices  = _vertices;
            _mesh.normals   = _normals;
            _mesh.uv        = _uvs;
            _mesh.triangles = _triangles;
            _mesh.RecalculateBounds();

            CacheCurrentState();
        }

        // ─────────────────────────────────────────────
        // Catmull-Rom 样条线数学
        // ─────────────────────────────────────────────

        private void GetCatmullRomSegment(float t, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float localT)
        {
            int n = controlPoints.Count;
            float scaledT = t * (n - 1);
            int   segIdx  = Mathf.Min((int)scaledT, n - 2);
            localT = scaledT - segIdx;

            p1 = controlPoints[segIdx];
            p2 = controlPoints[segIdx + 1];
            p0 = segIdx > 0     ? controlPoints[segIdx - 1] : p1 * 2f - p2;
            p3 = segIdx < n - 2 ? controlPoints[segIdx + 2] : p2 * 2f - p1;
        }

        public Vector3 EvaluateCatmullRom(float t)
        {
            GetCatmullRomSegment(t, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float lt);
            return CatmullRomPosition(p0, p1, p2, p3, lt);
        }

        public Vector3 EvaluateCatmullRomTangent(float t)
        {
            GetCatmullRomSegment(t, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float lt);
            return CatmullRomTangent(p0, p1, p2, p3, lt);
        }

        private static Vector3 CatmullRomPosition(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                ( 2f * p1) +
                (-p0 + p2) * t +
                ( 2f*p0 - 5f*p1 + 4f*p2 - p3) * t2 +
                (-p0 + 3f*p1 - 3f*p2 + p3) * t3
            );
        }

        private static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            return 0.5f * (
                (-p0 + p2) +
                2f * ( 2f*p0 - 5f*p1 + 4f*p2 - p3) * t +
                3f * (-p0 + 3f*p1 - 3f*p2 + p3) * t2
            );
        }

        // ─────────────────────────────────────────────
        // 旋转最小化框架（Rotation Minimizing Frames）
        // [FIX] 修正双反射算法：第二步反射轴应为 t_L - t_cur
        // ─────────────────────────────────────────────

        private static void BuildRotationMinimizingFrames(
            Vector3[] tangents,
            out Vector3[] normals,
            out Vector3[] binormals)
        {
            int n = tangents.Length;
            normals   = new Vector3[n];
            binormals = new Vector3[n];

            // 初始化第一个帧
            Vector3 t0 = tangents[0];
            Vector3 helper = (Mathf.Abs(Vector3.Dot(t0, Vector3.up)) < 0.99f)
                ? Vector3.up
                : Vector3.right;

            binormals[0] = Vector3.Cross(t0, helper).normalized;
            normals[0]   = Vector3.Cross(binormals[0], t0).normalized;

            // 平行传输：双反射方法（Wang et al.）
            for (int i = 1; i < n; i++)
            {
                Vector3 t_prev = tangents[i - 1];
                Vector3 t_cur  = tangents[i];
                Vector3 n_prev = normals[i - 1];

                // [FIX] 正确的双反射实现
                // 第一次反射：关于 v1 = t_cur - t_prev
                Vector3 v1 = t_cur - t_prev;
                float   c1 = Vector3.Dot(v1, v1);

                if (c1 < 1e-10f)
                {
                    // 切线几乎不变，直接继承
                    normals[i]   = n_prev;
                    binormals[i] = Vector3.Cross(n_prev, t_cur).normalized;
                    continue;
                }

                // 反射 n_prev 和 t_prev 关于 v1
                Vector3 n_L = n_prev - (2f / c1) * Vector3.Dot(v1, n_prev) * v1;
                Vector3 t_L = t_prev - (2f / c1) * Vector3.Dot(v1, t_prev) * v1;

                // 第二次反射：关于 v2 = t_cur - t_L（将 t_L 对齐到 t_cur）
                Vector3 v2 = t_cur - t_L;
                float   c2 = Vector3.Dot(v2, v2);

                Vector3 n_cur;
                if (c2 < 1e-10f)
                {
                    n_cur = n_L;
                }
                else
                {
                    n_cur = n_L - (2f / c2) * Vector3.Dot(v2, n_L) * v2;
                }

                normals[i]   = n_cur.normalized;
                binormals[i] = Vector3.Cross(normals[i], t_cur).normalized;
            }
        }

        // ─────────────────────────────────────────────
        // 弧长计算
        // ─────────────────────────────────────────────

        private static float[] ComputeArcLengths(Vector3[] points)
        {
            float[] lengths = new float[points.Length];
            lengths[0] = 0f;
            for (int i = 1; i < points.Length; i++)
            {
                lengths[i] = lengths[i - 1] + Vector3.Distance(points[i], points[i - 1]);
            }
            return lengths;
        }

        // ─────────────────────────────────────────────
        // Gizmo
        // ─────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (controlPoints == null || controlPoints.Count < 2) return;

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            int   previewSteps = Mathf.Max(segments * 4, 64);
            Vector3 prev = transform.TransformPoint(EvaluateCatmullRom(0f));

            for (int i = 1; i <= previewSteps; i++)
            {
                float   t    = (float)i / previewSteps;
                Vector3 curr = transform.TransformPoint(EvaluateCatmullRom(t));
                Gizmos.DrawLine(prev, curr);
                prev = curr;
            }
        }
    }
}
