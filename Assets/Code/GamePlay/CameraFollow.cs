using UnityEngine;

/// <summary>
/// 俯视角摄像机跟随（挂 Main Camera）
/// 职责：
/// 1. 平滑跟随目标（默认自动找本地玩家，Player 未生成时定时重试）
/// 2. 首次找到目标时做「视线对齐」：屏幕中心射线穿过注视点，玩家出现在画面中心，
///    保留场景里摆好的观察角度，并锁定跟随高度
/// 3. viewGroundOffset 运行时可实时调整构图（y 越大玩家在画面里越靠下）
/// 4. 战斗反馈钩子：开火（已订阅本地玩家武器事件）/ 受击（伤害系统未接入，留空方法）
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("跟随")]
    [Tooltip("跟随目标（留空自动找本地玩家）")]
    [SerializeField] private Transform target;

    [Tooltip("跟随平滑时间（秒，越小跟得越紧）")]
    [SerializeField] private float followSmoothTime = 0.08f;

    [Header("画面构图微调（运行时可实时调整）")]
    [Tooltip("注视点地面偏移。x=沿摄像机右方，y=沿摄像机视线水平前方。y 越大玩家在画面里越靠下")]
    [SerializeField] private Vector2 viewGroundOffset = new Vector2(0f, 6f);

    [Header("战斗反馈钩子")]
    [Tooltip("是否启用开火反馈")]
    [SerializeField] private bool fireFeedbackEnabled = true;

    [Tooltip("目标查找重试间隔（秒）")]
    [SerializeField] private float findRetryInterval = 0.5f;

    private Vector3 _offset;              // 实际使用的跟随偏移（每帧按当前参数重算）
    private float _alignHeight = 10f;     // 对齐时锁定的高度差（运行时不变）
    private Vector3 _followVelocity;
    private WeaponController _localWeapon; // 本地玩家武器（开火反馈的事件源）
    private float _findTimer;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        TryFindTarget();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            // 目标未生成/已销毁（等待进游戏、死亡重开）：定时重试
            _findTimer -= Time.deltaTime;
            if (_findTimer <= 0f)
            {
                TryFindTarget();
                _findTimer = findRetryInterval;
            }
            return;
        }

        // 每帧按当前参数重算偏移——viewGroundOffset 运行时调整立即生效（SmoothDamp 平滑过渡）
        RecomputeOffset();
        Vector3 goal = ComputeLookPoint() + _offset;
        transform.position = Vector3.SmoothDamp(transform.position, goal, ref _followVelocity,
                                                Mathf.Max(0.001f, followSmoothTime));
    }

    /// <summary>
    /// 查找本地玩家，找到后锁定跟随高度、视线对齐（瞬移到位），随后订阅其武器开火事件
    /// </summary>
    private void TryFindTarget()
    {
        var local = FindObjectOfType<LocalPlayerMovement>();
        if (local == null) return;

        target = local.transform;

        // 锁定高度差（保留场景摆好的摄像机高度），此后运行时不变
        float height = transform.position.y - target.position.y;
        _alignHeight = height > 0.01f ? height : 10f;
        RecomputeOffset();

        // 瞬移到对齐位置，并清空平滑速度缓存（避免旧速度把摄像机带偏）
        transform.position = ComputeLookPoint() + _offset;
        _followVelocity = Vector3.zero;

        // 订阅本地玩家武器的开火事件（玩家销毁/重建时由 OnDestroy/重试逻辑接管）
        if (fireFeedbackEnabled && _localWeapon == null)
        {
            _localWeapon = local.GetComponentInChildren<WeaponController>();
            if (_localWeapon != null)
            {
                _localWeapon.OnFired += OnLocalWeaponFired;
            }
        }
    }

    /// <summary>注视点 = 玩家位置 + 构图偏移（沿当前观察方向的水平投影）</summary>
    private Vector3 ComputeLookPoint()
    {
        Vector3 f = transform.forward;
        Vector3 fwdFlat = new Vector3(f.x, 0f, f.z);
        fwdFlat = fwdFlat.sqrMagnitude > 0.0001f ? fwdFlat.normalized : Vector3.zero;
        Vector3 rightFlat = new Vector3(f.z, 0f, -f.x);

        return target.position
               + rightFlat * viewGroundOffset.x
               + fwdFlat * viewGroundOffset.y;
    }

    /// <summary>
    /// 按当前视角与参数重算跟随偏移：
    /// offset = -forward * t，使摄像机中心射线穿过注视点（t = 高度差 / |forward.y|）
    /// </summary>
    private void RecomputeOffset()
    {
        Vector3 f = transform.forward;
        Vector3 fwdFlat = new Vector3(f.x, 0f, f.z);
        bool hasFlat = fwdFlat.sqrMagnitude > 0.0001f;
        if (hasFlat) fwdFlat.Normalize();

        if (f.y > -0.05f || !hasFlat)
        {
            // 近似垂直向下（或朝上）：注视点直接放到摄像机正下方
            _offset = new Vector3(0f, _alignHeight, 0f);
        }
        else
        {
            float t = _alignHeight / -f.y;
            _offset = new Vector3(-fwdFlat.x * t, _alignHeight, -fwdFlat.z * t);
        }
    }

    private void OnDestroy()
    {
        if (_localWeapon != null)
        {
            _localWeapon.OnFired -= OnLocalWeaponFired;
        }
    }

    #region 战斗反馈钩子（后续屏幕震动在此填实现）

    /// <summary>本地玩家开火（当前为占位，接屏幕震动时填实现）</summary>
    private void OnLocalWeaponFired(WeaponController.ShotInfo shot)
    {
        if (!fireFeedbackEnabled) return;

        NotifyFired(shot);
    }

    /// <summary>开火反馈钩子</summary>
    public void NotifyFired(WeaponController.ShotInfo shot)
    {
        // TODO: 开火反馈（轻微后坐震动）
    }

    /// <summary>
    /// 受击反馈钩子（伤害系统未接入，暂无订阅方——伤害结算落地后由其调用）
    /// </summary>
    /// <param name="damage">本次受到的伤害</param>
    /// <param name="sourcePosition">伤害来源位置（震动方向用）</param>
    public void NotifyDamaged(int damage, Vector3 sourcePosition)
    {
        // TODO: 受击反馈（震动/红闪）
    }

    #endregion
}
