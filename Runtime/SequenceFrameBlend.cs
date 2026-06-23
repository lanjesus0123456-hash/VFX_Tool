// ============================================================
//  SequenceFrameBlend — 序列帧升格平滑控制器
//
//  配合 VFX/SequenceFrameBlend Shader 使用。
//  挂载在拥有 MeshRenderer 的物体上，自动驱动帧间插值。
//
//  使用方式：
//  1. 在 MeshRenderer 上使用 VFX/SequenceFrameBlend 材质
//  2. 挂载此脚本
//  3. 设置 rows/cols 与 Shader 中一致
//  4. 设置 fps（原始序列帧帧率）
//  5. 运行后脚本自动计算 FrameA/FrameB/BlendFactor 并写入 Material
//
//  参数说明：
//  rows           : 序列帧行数（与 Shader 保持一致）
//  cols           : 序列帧列数（与 Shader 保持一致）
//  fps            : 原始播放帧率（如 24fps）
//  loop           : 是否循环播放
//  playOnAwake    : 是否启动时自动播放
// ============================================================
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class SequenceFrameBlend : MonoBehaviour
{
    [Header("Frame Layout")]
    [Tooltip("序列帧列数（与 Shader 中 Columns 一致）")]
    public int cols = 8;

    [Tooltip("序列帧行数（与 Shader 中 Rows 一致）")]
    public int rows = 8;

    [Header("Playback")]
    [Tooltip("原始播放帧率")]
    public float fps = 24f;

    [Tooltip("是否循环播放")]
    public bool loop = true;

    [Tooltip("是否启动时自动播放")]
    public bool playOnAwake = true;

    // ── 内部状态 ──────────────────────────────────────────────
    private Material  mat;
    private int       totalFrames;
    private float     timer;
    private bool      isPlaying;

    // Shader 属性 ID 缓存
    private static readonly int ID_FrameA      = Shader.PropertyToID("_FrameA");
    private static readonly int ID_FrameB      = Shader.PropertyToID("_FrameB");
    private static readonly int ID_BlendFactor  = Shader.PropertyToID("_BlendFactor");
    private static readonly int ID_Cols         = Shader.PropertyToID("_Cols");
    private static readonly int ID_Rows         = Shader.PropertyToID("_Rows");

    void Start()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr == null) return;

        // 使用 material（实例化副本），避免修改共享材质
        mat = mr.material;
        totalFrames = rows * cols;

        // 同步布局参数到 Shader
        mat.SetFloat(ID_Cols, cols);
        mat.SetFloat(ID_Rows, rows);

        timer = 0f;
        isPlaying = playOnAwake;
    }

    void Update()
    {
        if (!isPlaying || mat == null || totalFrames <= 0) return;

        timer += Time.deltaTime * fps;

        if (!loop && timer >= totalFrames)
        {
            timer = totalFrames - 1;
            isPlaying = false;
        }

        // 当前时间对应的帧（含小数部分用于插值）
        float frameFloat = loop ? (timer % totalFrames) : timer;

        int frameA = Mathf.FloorToInt(frameFloat);
        int frameB = frameA + 1;

        // 循环边界
        if (loop)
        {
            frameA = frameA % totalFrames;
            frameB = frameB % totalFrames;
        }
        else
        {
            frameA = Mathf.Clamp(frameA, 0, totalFrames - 1);
            frameB = Mathf.Clamp(frameB, 0, totalFrames - 1);
        }

        float blend = frameFloat - Mathf.FloorToInt(frameFloat);

        mat.SetFloat(ID_FrameA,     frameA);
        mat.SetFloat(ID_FrameB,     frameB);
        mat.SetFloat(ID_BlendFactor, blend);
    }

    // ── 公开方法 ──────────────────────────────────────────────

    /// <summary>开始播放</summary>
    public void Play()
    {
        timer = 0f;
        isPlaying = true;
    }

    /// <summary>暂停</summary>
    public void Pause() => isPlaying = false;

    /// <summary>继续</summary>
    public void Resume() => isPlaying = true;

    /// <summary>停止并重置到第 0 帧</summary>
    public void Stop()
    {
        isPlaying = false;
        timer = 0f;
        if (mat != null)
        {
            mat.SetFloat(ID_FrameA, 0);
            mat.SetFloat(ID_FrameB, 1);
            mat.SetFloat(ID_BlendFactor, 0);
        }
    }

    /// <summary>跳转到指定帧</summary>
    public void SetFrame(int frame)
    {
        timer = Mathf.Clamp(frame, 0, totalFrames - 1);
    }

    void OnDestroy()
    {
        // 清理实例化的材质副本
        if (mat != null)
            Destroy(mat);
    }
}
