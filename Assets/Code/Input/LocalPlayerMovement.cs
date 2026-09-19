﻿using UnityEngine;
using GameNetwork;

/// <summary>
/// 本地玩家移动控制器
/// 负责客户端预测：立即响应输入，不等待服务器
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputController))]
public class LocalPlayerMovement : MonoBehaviour
{
    #region 配置参数

    [Header("移动参数")]
    [Tooltip("载具配置（配置后 walkSpeed/sprintSpeed 以 SO 为准，覆盖下方默认值）")]
    [SerializeField] private PlayerProfileSO profile;

    public float walkSpeed = 2f;
    public float sprintSpeed = 5f;

    [Header("瞄准")]
    [Tooltip("每秒最大转身角度（度）。俯视角射击要跟手，建议给很大：1080 = 1/3 秒转一圈")]
    public float turnSpeed = 1080f;

    [Tooltip("鼠标射线打在哪一层（只勾 Ground / 地面即可，能省一次全场景射线）")]
    public LayerMask groundLayer = ~0;

    [Tooltip("瞄准平面相对角色脚底的高度偏移。0 = 与角色同高")]
    public float aimPlaneOffset = 0f;

    [Tooltip("枪口挂点。留空则用角色中心前方 0.6m（将来做射击时填真实枪口）")]
    public Transform muzzle;

    [Header("网络同步")]
    [Tooltip("每秒发送多少次输入到服务器")]
    public int sendRate = 60; // 与服务器帧率一致

    [Header("调试可视化")]
    [Tooltip("画出瞄准射线（Scene视图常显，Game视图需开Gizmos）")]
    public bool drawAimRays = true;

    #endregion

    #region 私有字段

    private CharacterController _controller;
    private PlayerInputController _playerInput;
    private PlayerAnimationController _anim;
    private uint _localFrameId;  // 本地帧ID（客户端预测用）
    private float _sendTimer;

    // 客户端预测相关
    private Vector3 _predictedPosition;
    private Quaternion _predictedRotation;

    // 瞄准相关
    private float _aimAngleDeg;      // 目标朝向（Y 轴欧拉角，度）
    private Vector3 _lastPosition;   // 上一帧位置（算真实速度用）

    // 动画辅助：转向角速度、垂直速度、落地检测
    private float _lastYaw;
    private float _verticalSpeed;
    private bool _wasGrounded = true;

    #endregion

    #region 公开属性（瞄准）

    /// <summary>鼠标在地面上的投影点（世界坐标），即准星指向的位置</summary>
    public Vector3 AimPoint { get; private set; }

    /// <summary>是否已经算出有效瞄准点（相机没准备好时为 false）</summary>
    public bool HasAimPoint { get; private set; }

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        // 载具配置优先：SO 里的移速覆盖 Inspector 手填值（没配置则保持原值，兼容旧预制体）
        if (profile != null)
        {
            walkSpeed = profile.walkSpeed;
            sprintSpeed = profile.sprintSpeed;
        }

        _controller = GetComponent<CharacterController>();
        _playerInput = GetComponent<PlayerInputController>();
        _anim = PlayerAnimationController.Resolve(transform);
        _localFrameId = 0;
        _predictedPosition = transform.position;
        _predictedRotation = transform.rotation;
        _lastPosition = transform.position;
        _lastYaw = transform.eulerAngles.y;

        if (_anim == null)
        {
            Debug.LogWarning($"[LocalPlayerMovement] ⚠️ [{gameObject.name}] 找不到 Animator，角色不会有动画表现");
        }
    }

    #endregion

    #region 公开属性

    /// <summary>
    /// 玩家唯一ID
    /// </summary>
    public string PlayerId { get; private set; } = "";

    /// <summary>
    /// 玩家显示名称
    /// </summary>
    public string PlayerName { get; private set; } = "";

    #endregion

    #region 公开方法 - 初始化

    /// <summary>
    /// 🔑 初始化本地玩家（由 GameInitializer / PlayerManager 调用）
    /// </summary>
    /// <param name="playerId">玩家唯一ID</param>
    /// <param name="playerName">玩家显示名称</param>
    public void Initialize(string playerId, string playerName)
    {
        PlayerId = playerId;
        PlayerName = playerName;

        // 设置玩家标识
        gameObject.name = $"LocalPlayer_{playerId}";

        // 可以在这里设置玩家颜色、模型等个性化数据
        Debug.Log($"[LocalPlayerMovement] ✅ 初始化完成: {playerName} (ID: {playerId})");
    }

    /// <summary>
    /// 客户端预测移动（由 FrameSyncManager 调用）
    /// </summary>
    public void PredictMovement(float horizontal, float vertical)
    {
        // 简化的预测移动逻辑
        Vector3 moveDirection = new Vector3(horizontal, 0, vertical);
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            moveDirection.Normalize();
            _controller.Move(moveDirection * walkSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// 服务器回包校验位置（由 FrameSyncManager 调用）
    /// </summary>
    public void ServerReconciliation(Vector3 serverPosition)
    {
        // 简单的位置校正（可以优化为平滑插值）
        float distance = Vector3.Distance(transform.position, serverPosition);
        if (distance > 0.5f) // 偏差超过阈值才校正
        {
            transform.position = serverPosition;
            _predictedPosition = serverPosition;
        }
    }

    #endregion

    #region Unity 生命周期（继续）

    private void Update()
    {
        if (!NetworkManager.Instance.IsConnected) return;

        // 只在帧同步进行中（服务器广播游戏开始后）才收集/发送输入
        // 否则在大厅/房间界面就会开始发包，服务端也会收到无主输入
        var frameSync = FrameSyncManager.GetInstance();
        if (frameSync == null || !frameSync.IsSyncing) return;

        // 死亡锁定：死亡后不再移动（服务端也不再接受其位置更新）
        if (frameSync.IsLocalDead) return;

        // 1. 先算瞄准：鼠标 → 世界瞄准点 → 目标朝向（必须早于 GetCurrentInput，Rotation 要写进包里）
        UpdateAim();
        DrawAimDebug();

        // 2. 获取当前输入
        var inputReq = _playerInput.GetCurrentInput(_localFrameId);

        // 3. 立即本地移动（客户端预测）⭐ 关键！
        MoveLocally(inputReq);

        // 4. 用"真实位移"驱动动画（不是用输入意图，撞墙时速度自然归零）
        UpdateAnimation(inputReq);

        // 5. 定时发送输入到服务器
        _sendTimer += Time.deltaTime;
        if (_sendTimer >= 1f / sendRate)
        {
            SendInputToServer(inputReq);
            _sendTimer = 0f;
            _localFrameId++;
        }
    }

    #endregion

    #region 核心方法 - 本地移动（客户端预测）

    /// <summary>
    /// 立即在本地应用移动（不等服务器）
    /// 这是"客户端预测"的核心
    /// </summary>
    private void MoveLocally(PlayerInputReq input)
    {
        // 计算移动速度
        float currentSpeed = input.IsSprinting ? sprintSpeed : walkSpeed;

        // 根据摄像机方向计算移动方向
        Vector3 moveDirection = new Vector3(input.MoveX, 0, input.MoveY);
        
        // 如果有输入，相对于摄像机方向移动
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            // 将移动方向转换到世界坐标（考虑摄像机朝向）
            moveDirection = Camera.main.transform.TransformDirection(moveDirection);
            moveDirection.y = 0; // 保持水平移动
            moveDirection.Normalize();
        }

        // 应用重力（简单版本）
        Vector3 verticalVelocity = Vector3.down * 9.81f;
        if (_controller.isGrounded && input.Jump)
        {
            verticalVelocity = Vector3.up * 5f; // 跳跃力度
            _anim?.PlayJump();                  // 新模型：jump 触发器
        }

        // 合并移动
        Vector3 finalMove = moveDirection * currentSpeed * Time.deltaTime + verticalVelocity * Time.deltaTime;

        // 移动角色
        _controller.Move(finalMove);

        // 记录垂直速度（falling 动画参数用）
        if (Time.deltaTime > 0.0001f) _verticalSpeed = finalMove.y / Time.deltaTime;

        // 转向：始终面向鼠标（俯视角射击的核心手感）
        ApplyRotation();

        // 保存预测位置（用于后续校正）
        _predictedPosition = transform.position;
        _predictedRotation = transform.rotation;
    }

    /// <summary>
    /// 以有限角速度转向目标朝向，帧率无关
    /// </summary>
    private void ApplyRotation()
    {
        if (!HasAimPoint) return;

        float next = Mathf.MoveTowardsAngle(
            transform.eulerAngles.y,
            _aimAngleDeg,
            turnSpeed * Time.deltaTime
        );
        transform.rotation = Quaternion.Euler(0f, next, 0f);
    }

    #endregion

    #region 核心方法 - 鼠标瞄准

    /// <summary>
    /// 屏幕鼠标位置 → 世界瞄准点 → 目标朝向（弧度写进协议包）
    ///
    /// 瞄准点用"摄像机射线打场景几何"获得（地面/障碍物/玩家都算）：
    /// 准星指近处地面 → 枪口到该点自然带俯角；指远处 → 接近平射。
    /// 若什么都没打到，退回"与角色同高的水平面"交点。
    ///
    /// 朝向规则（方案 A）：角色朝向只取水平投影（yaw），
    /// 俯仰只体现在弹道方向（GetAimDirection），角色模型不倾斜。
    /// </summary>
    private void UpdateAim()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(_playerInput.LookScreenPosition);

        // 优先：射线打场景几何（带俯仰的真实瞄准点）
        if (Physics.Raycast(ray, out RaycastHit sceneHit, 1000f, groundLayer, QueryTriggerInteraction.Ignore))
        {
            SetAimPoint(sceneHit.point);
            return;
        }

        // 兜底：与角色同高的水平面
        var plane = new Plane(Vector3.up,
            new Vector3(0f, transform.position.y + aimPlaneOffset, 0f));

        if (!plane.Raycast(ray, out float hitDistance)) return;

        SetAimPoint(ray.GetPoint(hitDistance));
    }

    /// <summary>设置瞄准点并计算角色朝向角</summary>
    private void SetAimPoint(Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - transform.position;

        // 鼠标几乎压在角色身上：保持上一帧朝向，避免抖动
        Vector3 flat = dir;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0004f) return;

        AimPoint = worldPoint;
        HasAimPoint = true;

        // atan2(x, z) 才是 Unity 里 Y 轴欧拉角（0° 朝 +Z）；朝向只用水平分量
        _aimAngleDeg = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;

        // 协议里 rotation 字段是弧度
        _playerInput.UpdateRotation(_aimAngleDeg * Mathf.Deg2Rad);
    }

    /// <summary>枪口世界坐标（没配枪口就用角色前方 0.6m、离地 1m 处）</summary>
    public Vector3 GetMuzzlePosition()
    {
        return muzzle != null
            ? muzzle.position
            : transform.position + transform.forward * 0.6f + Vector3.up * 1f;
    }

    /// <summary>
    /// 调试可视化：
    /// 黄线 = 玩家 → 准星（角色朝向线，纯水平）
    /// 青线 = 枪口 → 准星（实际弹道方向，带俯仰）
    /// </summary>
    private void DrawAimDebug()
    {
        if (!drawAimRays || !HasAimPoint) return;

        Debug.DrawLine(transform.position + Vector3.up * 1.5f, AimPoint, Color.yellow);
        Debug.DrawLine(GetMuzzlePosition(), AimPoint, Color.cyan);
    }

    /// <summary>
    /// 从枪口指向瞄准点的单位方向（会聚射线）
    /// 将来做射击时：Ray/shot 的 origin 用 GetMuzzlePosition()，direction 用这个。
    /// 想改成平行射线，直接返回 transform.forward 即可。
    /// </summary>
    public Vector3 GetAimDirection()
    {
        Vector3 from = GetMuzzlePosition();
        Vector3 to = HasAimPoint ? AimPoint : from + transform.forward * 20f;
        Vector3 dir = to - from;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
    }

    #endregion

    #region 核心方法 - 动画驱动

    /// <summary>
    /// 喂原始输入轴（-1..1）给混合树：走路/冲刺同一套值，表现保持一致。
    /// </summary>
    private void UpdateAnimation(PlayerInputReq input)
    {
        if (_anim == null) return;

        Vector3 delta = transform.position - _lastPosition;
        _lastPosition = transform.position;
        delta.y = 0f;                       // 只统计水平速度
        float dt = Time.deltaTime;
        float speed = dt > 0.0001f ? delta.magnitude / dt : 0f;

        // 原始输入是摄像机相对的，先转成世界方向再转进角色本地空间——
        // 混合树的 horizontal/vertical 必须是角色本地坐标，否则只有面向+Z时方向才正确
        Vector3 moveDir = new Vector3(input.MoveX, 0f, input.MoveY);
        if (moveDir.sqrMagnitude > 0.01f && Camera.main != null)
        {
            moveDir = Camera.main.transform.TransformDirection(moveDir);
            moveDir.y = 0f;
            moveDir.Normalize();
        }
        Vector3 localDir = transform.InverseTransformDirection(moveDir);

        // 原始输入轴（角色本地空间）直接进混合树参数（horizontal/vertical）
        _anim.SetMoveInput(localDir.x, localDir.z);
        _anim.SetMoveSpeed(speed);          // 旧控制器 Speed 参数仍用真实速度（新模型自动跳过）
        _anim.ApplyMoveAnimSpeed(input.IsSprinting); // 奔跑时切换动画播放倍率

        // 转向参数：本帧朝向角速度归一化（±720°/s 映射到 ±1），驱动 turnLeft/turnRight
        float yaw = transform.eulerAngles.y;
        float yawRate = dt > 0.0001f ? Mathf.DeltaAngle(_lastYaw, yaw) / dt : 0f;
        _lastYaw = yaw;


        // 跳跃三态：落地触发器、下坠布尔
        bool grounded = _controller.isGrounded;
        if (grounded && !_wasGrounded) _anim.PlayLand();
        if (!grounded) _anim.SetFalling(_verticalSpeed < -0.5f);
        else _anim.SetFalling(false);
        _wasGrounded = grounded;

        _anim.SetGrounded(grounded);
        _anim.SetFiring(input.Fire);
        _anim.SetAiming(input.IsAiming);
    }

    #endregion

    #region 网络通信 - 发送输入

    /// <summary>
    /// 发送当前输入状态到服务器
    /// 输入 + 自己的权威位置一起上报：
    /// 本地位置经过 CharacterController 物理碰撞，是本角色的真实位置，
    /// 服务端持有后广播给其他玩家做位置纠正
    /// </summary>
    private void SendInputToServer(PlayerInputReq input)
    {
        try
        {
            // 附带权威位置状态
            input.PosX = transform.position.x;
            input.PosY = transform.position.y;
            input.PosZ = transform.position.z;
            input.RotY = transform.eulerAngles.y;

            // 附带载具配置名（hold-last：服务端首次收到时按其初始化血量上限）
            input.Vehicle = profile != null ? profile.name : "";

            NetworkManager.Instance.Send(MsgID.C2SPlayerInput, input);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LocalPlayer] 发送输入失败: {ex.Message}");
        }
    }

    #endregion

    #region 服务器校正（预留接口）

    /// <summary>
    /// 接收服务器的位置校正信息
    /// 当客户端预测与服务器不一致时调用
    /// </summary>
    /// <param name="serverPosition">服务器权威位置</param>
    /// <param name="serverFrameId">对应的帧ID</param>
    public void OnServerCorrection(Vector3 serverPosition, uint serverFrameId)
    {
        // TODO: 实现插值校正算法
        // 
        // 简单方案：直接瞬移到服务器位置（会有抖动）
        // transform.position = serverPosition;
        //
        // 高级方案：平滑插值 + 重模拟中间帧
        // StartCoroutine(SmoothCorrection(serverPosition));
        
        // 暂时使用简单方案
        transform.position = serverPosition;
        _predictedPosition = serverPosition;
        
        Debug.Log($"[LocalPlayer] 位置校正: Frame {serverFrameId}");
    }

    #endregion
}
