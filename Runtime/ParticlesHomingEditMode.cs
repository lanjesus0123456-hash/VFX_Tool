using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
[ExecuteInEditMode]
public class ParticlesHomingEditMode : MonoBehaviour
{
    [Header("目标设置")]
    [Tooltip("粒子追踪的目标位置")]
    public Transform target;

    [Header("追踪行为")]
    [Tooltip("施加在粒子上的追踪力大小")]
    public float force = 10.0f;

    [Tooltip("根据粒子生命周期调整追踪力的曲线")]
    public AnimationCurve forceOverLifetime = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("目标到达行为")]
    [Tooltip("粒子到达目标位置后是否立即消失")]
    public bool killOnReachTarget = true;

    [Tooltip("粒子到达目标的判定距离")]
    [Min(0.01f)]
    public float reachDistance = 0.1f;

    [Header("状态监控")]
    [Tooltip("当前激活状态")]
    [SerializeField] private bool _isActive = false;

    [Tooltip("当前存活粒子数量")]
    [SerializeField] private int _particleCount = 0;

    // 组件引用
    private ParticleSystem _ps;
    private ParticleSystem.Particle[] _particles;

    // 编辑模式专用
    private double _lastEditorTime;
    private const float EDIT_MODE_UPDATE_INTERVAL = 0.1f;

    void Start()
    {
        Initialize();
    }

    void OnEnable()
    {
        Initialize();
        #if UNITY_EDITOR
        // [FIX] 使用 EditorApplication.update 驱动编辑模式更新
        if (!Application.isPlaying)
        {
            _lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
            UnityEditor.EditorApplication.update += EditorUpdate;
        }
        #endif
    }

    void OnDisable()
    {
        Deactivate();
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.update -= EditorUpdate;
        }
        #endif
    }

    void Initialize()
    {
        _ps = GetComponent<ParticleSystem>();

        if (_ps == null)
        {
            Debug.LogWarning("ParticlesHomingEditMode: 找不到关联的粒子系统组件", this);
            return;
        }

        if (_ps.isPlaying || _ps.isEmitting)
        {
            Activate();
        }
        else
        {
            Deactivate();
        }

        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Activate();
        }
        #endif
    }

    void Activate()
    {
        _isActive = true;

        #if UNITY_EDITOR
        if (!Application.isPlaying && _ps != null && !_ps.isPlaying)
        {
            _ps.Play();
        }
        #endif
    }

    void Deactivate()
    {
        _isActive = false;
    }

    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (target != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(target.position, 0.2f);

            if (killOnReachTarget)
            {
                Gizmos.color = new Color(1, 0.5f, 0, 0.3f);
                Gizmos.DrawWireSphere(target.position, reachDistance);
            }
        }
    }

    /// <summary>
    /// [FIX] 编辑模式下通过 EditorApplication.update 驱动，
    /// 使用 EditorApplication.timeSinceStartup 计算真实 deltaTime，
    /// 避免 Time.deltaTime 在编辑模式下始终为 0 的问题。
    /// </summary>
    private void EditorUpdate()
    {
        if (Application.isPlaying) return;
        if (!_isActive || _ps == null || target == null) return;

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        float elapsed = (float)(now - _lastEditorTime);
        if (elapsed < EDIT_MODE_UPDATE_INTERVAL) return;
        _lastEditorTime = now;

        UpdateParticles(elapsed);

        // 强制 Scene 视图刷新
        UnityEditor.SceneView.RepaintAll();
    }
    #endif

    void LateUpdate()
    {
        // 运行模式下走 LateUpdate
        if (!Application.isPlaying) return;
        if (!_isActive) return;

        if (_ps == null)
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) return;
        }

        if (target == null) return;

        UpdateParticles(Time.deltaTime);
    }

    /// <summary>
    /// 粒子追踪核心逻辑（运行时和编辑模式共用）
    /// </summary>
    private void UpdateParticles(float deltaTime)
    {
        if (deltaTime <= 0f) return;

        int maxParticles = _ps.main.maxParticles;
        if (_particles == null || _particles.Length < maxParticles)
        {
            _particles = new ParticleSystem.Particle[maxParticles];
        }

        int particleCount = _ps.GetParticles(_particles);
        _particleCount = particleCount;

        if (particleCount == 0)
        {
            // [FIX] 编辑模式下也允许停用，避免空跑
            if (Application.isPlaying)
                Deactivate();
            return;
        }

        Vector3 targetPosition = target.position;
        bool isLocalSpace = _ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
        if (isLocalSpace)
        {
            targetPosition = transform.InverseTransformPoint(target.position);
        }

        int activeParticleCount = particleCount;
        for (int i = 0; i < activeParticleCount;)
        {
            if (killOnReachTarget)
            {
                float distanceToTarget = Vector3.Distance(_particles[i].position, targetPosition);
                if (distanceToTarget <= reachDistance)
                {
                    activeParticleCount--;
                    if (i < activeParticleCount)
                    {
                        _particles[i] = _particles[activeParticleCount];
                    }
                    continue;
                }
            }

            Vector3 targetDirection = (targetPosition - _particles[i].position).normalized;
            Vector3 seekForce = targetDirection * force * deltaTime;

            if (_particles[i].startLifetime > 0)
            {
                float lifetime = (_particles[i].startLifetime - _particles[i].remainingLifetime)
                               / _particles[i].startLifetime;
                seekForce *= forceOverLifetime.Evaluate(lifetime);
            }

            _particles[i].velocity += seekForce;
            i++;
        }

        _ps.SetParticles(_particles, activeParticleCount);
    }
}
