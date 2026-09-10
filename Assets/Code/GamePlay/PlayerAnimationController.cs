using UnityEngine;

/// <summary>
/// 角色动画统一门面（Facade）
///
/// 设计目的：本地玩家和远程玩家共用一套"动画语义"，谁都不要直接碰 Animator。
///   - LocalPlayerMovement  /  RemotePlayerMovement 只负责"算出真实数据"（速度、是否在开火…）
///   - 动画怎么播（混合树？Trigger？CrossFade？）全部由这里决定
/// 好处：以后换模型、换 Animator、加受击/死亡/换弹，只改这一个文件。
///
/// 三种参数的使用原则：
///   1. 连续量（移动速度）      → float，交给混合树，脚本里做阻尼平滑
///   2. 持续状态（开火/瞄准/地面/死亡）→ bool，由 Animator 状态机自己循环，脚本只管开关
///   3. 一次性事件（受击/换弹）   → Trigger 或直接 CrossFade 到指定状态
///
/// 另外：参数名全部可配置，且初始化时会检查 Animator 里是否真的存在该参数，
/// 不存在的直接跳过（不会每帧刷 Warning）。
/// </summary>
public class PlayerAnimationController : MonoBehaviour
{
    #region Inspector 配置

    [Header("Animator（留空则自动取自身 → 子物体）")]
    [SerializeField] private Animator animator;

    [Header("参数名（需与 Animator 面板一致，留空 = 不使用该参数）")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string isMovingParam = "IsMoving";
    [SerializeField] private string firingParam = "IsFiring";
    [SerializeField] private string aimingParam = "IsAiming";
    [SerializeField] private string groundedParam = "Grounded";
    [SerializeField] private string deadParam = "IsDead";
    [SerializeField] private string hitTriggerParam = "Hit";
    [SerializeField] private string reloadTriggerParam = "Reload";

    [Header("2D 混合树参数（droid_walker 等新模型，没有的自动跳过）")]
    [SerializeField] private string horizontalParam = "horizontal";
    [SerializeField] private string verticalParam = "vertical";
    [SerializeField] private string turnParam = "turn";
    [SerializeField] private string activeParam = "active";
    [SerializeField] private string fallingParam = "falling";
    [SerializeField] private string jumpTriggerParam = "jump";
    [SerializeField] private string landTriggerParam = "land";
    [SerializeField] private string fireTriggerParam = "fire";
    [SerializeField] private string damageTriggerParam = "damage";
    [SerializeField] private string damageDirXParam = "damageDirX";
    [SerializeField] private string damageDirYParam = "damageDirY";

    [Header("移动动画播放速度（需要 controller 里有 moveAnimSpeed 参数并绑到状态的 Speed Parameter）")]
    [Tooltip("走路（未冲刺）时的动画播放倍率")]
    [SerializeField] public float walkAnimSpeed = 1f;

    [Tooltip("奔跑（冲刺中）时的动画播放倍率")]
    [SerializeField] public float runAnimSpeed = 1f;

    [Tooltip("驱动播放倍率的参数名")]
    [SerializeField] private string moveSpeedParamName = "moveAnimSpeed";

    /// <summary>走路播放倍率（只读便捷访问）</summary>
    public float WalkAnimSpeed => walkAnimSpeed;

    /// <summary>奔跑播放倍率（只读便捷访问）</summary>
    public float RunAnimSpeed => runAnimSpeed;

    [Header("速度映射")]
    [Tooltip("把 m/s 除以 maxMoveSpeed 归一化到 0~1 再喂给混合树。\n混合树阈值是 0 / 0.5 / 1 时必须开启。")]
    [SerializeField] private bool normalizeSpeed = true;

    [Tooltip("归一化分母，建议填冲刺速度（sprintSpeed）。走 5 / 冲 8 → 0.625 / 1.0")]
    [SerializeField] private float maxMoveSpeed = 8f;

    [Tooltip("SetFloat 的阻尼时间（秒），滤掉速度抖动，避免脚步动画抽搐")]
    [SerializeField] private float speedDampTime = 0.08f;

    [Tooltip("速度死区（m/s）：低于此值直接归零，防止站桩时脚底打滑")]
    [SerializeField] private float speedDeadzone = 0.15f;

    [Tooltip("IsMoving 置为 true 的阈值（归一化后的参数值）")]
    [SerializeField] private float moveStartThreshold = 0.05f;

    [Header("一次性动画（填了状态名就走 CrossFade，不填就只打 Trigger）")]
    [Tooltip("子状态机里的状态要写全路径，例如 Locomotion.Death")]
    [SerializeField] private string deathStateName = "";
    [SerializeField] private string hitStateName = "";
    [SerializeField] private string reloadStateName = "";
    [SerializeField] private float crossFadeDuration = 0.1f;

    [Header("调试")]
    [SerializeField] private bool verbose = false;

    #endregion

    #region 运行时状态

    private Animator _anim;

    // 参数 Hash + 是否存在的标记（Animator 里没有的参数直接跳过，避免每帧刷 Warning）
    private int _speedHash, _isMovingHash, _firingHash, _aimingHash, _groundedHash, _deadHash, _hitHash, _reloadHash;
    private bool _hasSpeed, _hasIsMoving, _hasFiring, _hasAiming, _hasGrounded, _hasDead, _hasHit, _hasReload;

    // 新模型（2D 混合树）参数
    private int _horizontalHash, _verticalHash, _turnHash, _activeHash, _fallingHash;
    private int _jumpHash, _landHash, _fireHash, _damageHash, _damageDirXHash, _damageDirYHash;
    private bool _hasHorizontal, _hasVertical, _hasTurn, _hasActive, _hasFalling;
    private bool _hasJump, _hasLand, _hasFireTrigger, _hasDamageTrigger, _hasDamageDir;
    private bool _firingIsBool;

    // 播放速度旋钮（controller 的 moveAnimSpeed 参数）
    private int _moveSpeedHash;
    private bool _hasMoveSpeed;

    private float _currentSpeed;    // 当前喂给 Animator 的值（已归一化/已平滑）
    private float _speedVelocity;   // SmoothDamp 内部速度
    private bool _firing;
    private bool _aiming;
    private bool _grounded = true;
    private bool _dead;

    #endregion

    #region 公开只读属性

    /// <summary>是否已死亡（死亡期间移动/开火输入被屏蔽）</summary>
    public bool IsDead => _dead;

    /// <summary>当前实际写入 Animator 的速度参数值</summary>
    public float CurrentSpeedParam => _currentSpeed;

    /// <summary>当前是否在播开火状态</summary>
    public bool IsFiring => _firing;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        _anim = animator;

        if (_anim == null)
        {
            Debug.LogWarning($"[PlayerAnimationController] ⚠️ [{name}] 找不到 Animator，动画控制已停用", this);
            enabled = false;
            return;
        }

        CacheParameters();
        PushInitialState();
    }

    /// <summary>
    /// 预先把参数名转成 Hash，并记录哪些参数在 Animator 里真实存在
    /// </summary>
    private void CacheParameters()
    {
        _speedHash     = ResolveParam(speedParam,      ref _hasSpeed,     "float (混合树)");
        _isMovingHash  = ResolveParam(isMovingParam,   ref _hasIsMoving,  "bool");
        _firingHash    = ResolveParam(firingParam,     ref _hasFiring,    "bool/trigger");
        _aimingHash    = ResolveParam(aimingParam,     ref _hasAiming,    "bool");
        _groundedHash  = ResolveParam(groundedParam,   ref _hasGrounded,  "bool");
        _deadHash      = ResolveParam(deadParam,       ref _hasDead,      "bool");
        _hitHash       = ResolveParam(hitTriggerParam, ref _hasHit,       "trigger");
        _reloadHash    = ResolveParam(reloadTriggerParam, ref _hasReload, "trigger");

        // 旧 firingParam 若是 Trigger 类型，SetFiring 不再写它（避免误触发），开火走 PlayFire
        _firingIsBool = _hasFiring && GetParamType(firingParam) == AnimatorControllerParameterType.Bool;

        _horizontalHash = ResolveParam(horizontalParam, ref _hasHorizontal, "float (2D混合)");
        _verticalHash   = ResolveParam(verticalParam,   ref _hasVertical,   "float (2D混合)");
        _turnHash       = ResolveParam(turnParam,       ref _hasTurn,       "float");
        _activeHash     = ResolveParam(activeParam,     ref _hasActive,     "bool");
        _fallingHash    = ResolveParam(fallingParam,    ref _hasFalling,    "bool");
        _jumpHash       = ResolveParam(jumpTriggerParam, ref _hasJump,      "trigger");
        _landHash       = ResolveParam(landTriggerParam, ref _hasLand,      "trigger");
        _fireHash       = ResolveParam(fireTriggerParam, ref _hasFireTrigger, "trigger");
        _damageHash     = ResolveParam(damageTriggerParam, ref _hasDamageTrigger, "trigger");
        _damageDirXHash = ResolveParam(damageDirXParam, ref _hasDamageDir,  "float (受击方向)");
        _damageDirYHash = ResolveParam(damageDirYParam, ref _hasDamageDir,  "float (受击方向)");

        _moveSpeedHash  = ResolveParam(moveSpeedParamName, ref _hasMoveSpeed, "float (播放速度旋钮)");
    }

    /// <summary>查询参数类型（不存在返回 Bool 的无效值由调用方先查存在性）</summary>
    private AnimatorControllerParameterType GetParamType(string paramName)
    {
        foreach (var p in _anim.parameters)
        {
            if (p.name == paramName) return p.type;
        }
        return AnimatorControllerParameterType.Bool;
    }

    /// <summary>
    /// 把初始状态推给 Animator，避免 Animator 默认值和脚本里的默认值不一致
    ///（比如面板上 Grounded 默认 false，而脚本认为角色是站着的）
    /// </summary>
    private void PushInitialState()
    {
        if (_hasSpeed) _anim.SetFloat(_speedHash, _currentSpeed);
        if (_hasIsMoving) _anim.SetBool(_isMovingHash, false);
        if (_hasFiring && _firingIsBool) _anim.SetBool(_firingHash, false);
        if (_hasAiming) _anim.SetBool(_aimingHash, false);
        if (_hasGrounded) _anim.SetBool(_groundedHash, _grounded);
        if (_hasDead) _anim.SetBool(_deadHash, false);
        if (_hasActive) _anim.SetBool(_activeHash, true); // 新模型：进场即激活
        if (_hasFalling) _anim.SetBool(_fallingHash, false);
    }

    private int ResolveParam(string paramName, ref bool present, string expectType)
    {
        present = !string.IsNullOrEmpty(paramName) && HasParameter(paramName);
        if (string.IsNullOrEmpty(paramName)) return 0;

        if (!present)
        {
            Debug.Log($"[PlayerAnimationController] ℹ️ [{name}] Animator 里没有 {expectType} 参数 '{paramName}'，已跳过（不影响其它动画）");
            return 0;
        }
        return Animator.StringToHash(paramName);
    }

    private bool HasParameter(string paramName)
    {
        foreach (var p in _anim.parameters)
        {
            if (p.name == paramName) return true;
        }
        return false;
    }

    #endregion

    #region 公开方法 - 连续量（混合树）

    /// <summary>
    /// 设置移动速度（单位 m/s）。
    /// 每帧调用即可，内部自己做死区 + 阻尼平滑。
    /// 本地玩家传"位置差 / dt"，远程玩家传"前馈速度"，两者量纲一致，动画表现自然一致。
    /// </summary>
    public void SetMoveSpeed(float metersPerSecond)
    {
        if (_anim == null) return;

        // 死亡后锁死移动表现
        if (_dead) metersPerSecond = 0f;

        float target = Mathf.Max(0f, metersPerSecond);
        if (target < speedDeadzone) target = 0f;

        if (normalizeSpeed && maxMoveSpeed > 0.0001f)
        {
            target = target / maxMoveSpeed;
        }

        _currentSpeed = Mathf.SmoothDamp(_currentSpeed, target, ref _speedVelocity,
                                         Mathf.Max(0.0001f, speedDampTime));
        if (Mathf.Abs(_currentSpeed - target) < 0.001f)
        {
            _currentSpeed = target;
            _speedVelocity = 0f;
        }

        if (_hasSpeed) _anim.SetFloat(_speedHash, _currentSpeed);
        if (_hasIsMoving) _anim.SetBool(_isMovingHash, _currentSpeed > moveStartThreshold);

        if (verbose) Debug.Log($"[Anim] {name} speed={metersPerSecond:F2} m/s → param={_currentSpeed:F3}");
    }

    #endregion

    #region 公开方法 - 2D 混合树（droid_walker 等新模型）

    /// <summary>
    /// 设置角色本地空间速度（米/秒）：localX=侧向，localZ=前后。
    /// 驱动 horizontal/vertical 二维混合树；同时换算标量速度喂旧 Speed 参数（旧模型兼容）
    /// </summary>
    public void SetMoveInput(float inputX, float inputZ)
    {
        if (_anim == null) return;
        if (_dead) { inputX = 0f; inputZ = 0f; }

        // 直接喂原始输入轴（-1..1），走路/冲刺同一套值——混合树表现完全一致
        if (_hasHorizontal) _anim.SetFloat(_horizontalHash, Mathf.Clamp(inputX, -1f, 1f));
        if (_hasVertical) _anim.SetFloat(_verticalHash, Mathf.Clamp(inputZ, -1f, 1f));

        SetMoveSpeed(new Vector2(inputX, inputZ).magnitude);
    }

    /// <summary>
    /// 设置移动动画播放速度倍率（驱动 controller 的 moveAnimSpeed → 状态 Speed Parameter）
    /// 只缩放动画播放节奏，不影响混合方向
    /// </summary>
    public void SetMoveAnimSpeed(float multiplier)
    {
        if (_anim == null || !_hasMoveSpeed) return;
        _anim.SetFloat(_moveSpeedHash, Mathf.Clamp(multiplier, 0.1f, 5f));
    }

    /// <summary>按奔跑/走路状态应用对应的动画播放速度倍率</summary>
    public void ApplyMoveAnimSpeed(bool sprinting)
    {
        SetMoveAnimSpeed(sprinting ? runAnimSpeed : walkAnimSpeed);
    }

    /// <summary>转向参数（归一化 -1..1：负=左转，正=右转），驱动 turnLeft/turnRight 混合</summary>
    public void SetTurn(float normalizedTurn)
    {
        if (_anim == null || _dead) return;
        if (_hasTurn) _anim.SetFloat(_turnHash, Mathf.Clamp(normalizedTurn, -1f, 1f));
    }

    /// <summary>跳跃动画（Trigger）</summary>
    public void PlayJump()
    {
        if (_anim == null || _dead) return;
        if (_hasJump) _anim.SetTrigger(_jumpHash);
    }

    /// <summary>落地动画（Trigger）</summary>
    public void PlayLand()
    {
        if (_anim == null || _dead) return;
        if (_hasLand) _anim.SetTrigger(_landHash);
    }

    /// <summary>是否悬空下坠（falling bool）</summary>
    public void SetFalling(bool falling)
    {
        if (_anim == null) return;
        if (_hasFalling) _anim.SetBool(_fallingHash, falling);
    }

    /// <summary>开火动画（fire Trigger）——本地开火命中/远端收到开火事件时调用</summary>
    public void PlayFire()
    {
        if (_anim == null || _dead) return;
        if (_hasFireTrigger) _anim.SetTrigger(_fireHash);
    }

    /// <summary>
    /// 受击动画：localDir 为受击方向在角色本地空间的朝向分量（决定受击姿态偏摆）
    /// </summary>
    public void PlayDamage(Vector2 localDir)
    {
        if (_anim == null || _dead) return;

        if (_hasDamageDir)
        {
            _anim.SetFloat(_damageDirXHash, localDir.x);
            _anim.SetFloat(_damageDirYHash, localDir.y);
        }
        if (_hasDamageTrigger) _anim.SetTrigger(_damageHash);
    }

    #endregion

    #region 公开方法 - 持续状态（bool）

    /// <summary>是否处于开火状态（持续型）。
    /// 新模型的 fire 是 Trigger（点射事件），此方法自动跳过，开火走 PlayFire</summary>
    public void SetFiring(bool firing)
    {
        if (_anim == null) return;
        if (_dead) firing = false;
        if (_firingIsBool == false && _hasFiring == false) return;
        if (_firingIsBool == false) return; // fire 参数是 Trigger，不在此处写

        if (firing == _firing) return;

        _firing = firing;
        if (_hasFiring) _anim.SetBool(_firingHash, firing);
    }

    /// <summary>是否处于瞄准状态（举枪姿态）</summary>
    public void SetAiming(bool aiming)
    {
        if (_anim == null) return;
        if (_dead) aiming = false;
        if (aiming == _aiming) return;

        _aiming = aiming;
        if (_hasAiming) _anim.SetBool(_aimingHash, aiming);
    }

    /// <summary>是否站在地面上（跳跃/下落动画用）</summary>
    public void SetGrounded(bool grounded)
    {
        if (_anim == null) return;
        if (grounded == _grounded) return;

        _grounded = grounded;
        if (_hasGrounded) _anim.SetBool(_groundedHash, grounded);
    }

    #endregion

    #region 公开方法 - 一次性事件

    /// <summary>
    /// 播受击动画。
    /// 注意：血量和伤害判定还没做，这个方法现在只是预留接口，
    /// 等受击事件系统接上时直接调用即可。
    /// </summary>
    public void PlayHit()
    {
        if (_anim == null || _dead) return;

        if (string.IsNullOrEmpty(hitStateName))
        {
            if (_hasHit) _anim.SetTrigger(_hitHash);
        }
        else
        {
            CrossFade(hitStateName);
        }
    }

    /// <summary>播换弹动画</summary>
    public void PlayReload()
    {
        if (_anim == null || _dead) return;

        if (string.IsNullOrEmpty(reloadStateName))
        {
            if (_hasReload) _anim.SetTrigger(_reloadHash);
        }
        else
        {
            CrossFade(reloadStateName);
        }
    }

    /// <summary>进入死亡状态（会锁死移动与开火）</summary>
    public void PlayDeath()
    {
        SetDead(true);
    }

    /// <summary>复活（解除锁死）</summary>
    public void Revive()
    {
        SetDead(false);
    }

    /// <summary>直接设置生死状态</summary>
    public void SetDead(bool dead)
    {
        if (_anim == null) return;
        if (dead == _dead) return;

        _dead = dead;
        if (_hasDead) _anim.SetBool(_deadHash, dead);

        if (dead)
        {
            // 死亡瞬间把移动/开火状态清零，防止尸体还在"跑步 + 开枪"
            _firing = false;
            _aiming = false;
            _currentSpeed = 0f;
            _speedVelocity = 0f;

            if (_hasFiring) _anim.SetBool(_firingHash, false);
            if (_hasAiming) _anim.SetBool(_aimingHash, false);
            if (_hasSpeed) _anim.SetFloat(_speedHash, 0f);
            if (_hasIsMoving) _anim.SetBool(_isMovingHash, false);
            if (_hasHorizontal) _anim.SetFloat(_horizontalHash, 0f);
            if (_hasVertical) _anim.SetFloat(_verticalHash, 0f);
            if (_hasTurn) _anim.SetFloat(_turnHash, 0f);

            // 新模型：active=false 走 Inactive 状态（旧模型走 IsDead/deathState）
            if (_hasActive) _anim.SetBool(_activeHash, false);

            if (!string.IsNullOrEmpty(deathStateName)) CrossFade(deathStateName);
        }
        else
        {
            // 复活：重新激活
            if (_hasActive) _anim.SetBool(_activeHash, true);
        }
        // 复活不在这里 CrossFade：IsDead 置回 false 后，让 Animator 状态机自己按条件过渡回去
    }

    /// <summary>
    /// 直接 CrossFade 到指定状态（给业务层兜底用，比如将来做处决动作）
    /// </summary>
    public void CrossFadeTo(string stateName)
    {
        CrossFade(stateName);
    }

    private void CrossFade(string stateName)
    {
        if (_anim == null || string.IsNullOrEmpty(stateName)) return;

        if (!_anim.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[PlayerAnimationController] ⚠️ [{name}] Animator 第 0 层找不到状态 '{stateName}'。" +
                             "子状态机里的状态要写全路径（如 Locomotion.Death）", this);
            return;
        }

        _anim.CrossFade(stateName, crossFadeDuration, 0);
    }

    #endregion

    #region 静态工具 - 自动挂载

    /// <summary>
    /// 在玩家根节点上取（或创建）动画控制器。
    /// Animator 通常在模型的子物体上（PlayerVisual），这里自动找到它并挂上去，
    /// 省得每次做预制体都要手动拖引用。
    /// </summary>
    public static PlayerAnimationController Resolve(Transform root)
    {
        var existing = root.GetComponentInChildren<PlayerAnimationController>(true);
        if (existing != null) return existing;

        var anim = root.GetComponentInChildren<Animator>(true);
        if (anim == null) return null;

        return anim.gameObject.AddComponent<PlayerAnimationController>();
    }

    #endregion
}
