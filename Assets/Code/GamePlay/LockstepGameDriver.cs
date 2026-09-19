using System.Collections.Generic;
using GameLockstep;
using GameNetwork;
using UnityEngine;
using Diagnostics = System.Diagnostics;

/// <summary>
/// 帧同步会话驱动（表现层总编排，阶段7）
///
/// 职责：
/// - GameStart 带种子/座位表 → 构造 LogicWorld + LockstepClient（旧服务器无种子则不启动，走旧链路）
/// - 15Hz 固定步长推进：输入采样量化 → SubmitLocalCommand → Drain 发包 → Advance
/// - 渲染插值：Transform 在前后两个逻辑帧状态间按 accumulator 比例插值（本地/远端同一套）
/// - LogicEvent 消费：开火特效/受击动画/死亡/换弹表现（回滚重演的帧用单调帧号闸门去重）
/// - 本局内禁用旧状态同步组件（LocalPlayerMovement/LocalPlayerCombat/RemotePlayerMovement），
///   旧组件阶段9 删除
///
/// 网上只有指令；位置/血量/开火结果全部来自本地确定性模拟
/// </summary>
public class LockstepGameDriver : MonoBehaviour
{
    public static LockstepGameDriver Instance { get; private set; }

    private LockstepClient _client;
    private LogicWorld World => _client?.World;

    private string _localPlayerId = "";
    private int _localSeat = -1;
    private readonly Dictionary<int, string> _seatToPlayerId = new();

    /// <summary>座位 → 表现层视图（Transform/动画/名牌/特效入口 + 插值缓存）</summary>
    private class View
    {
        public Transform Transform;
        public PlayerAnimationController Anim;
        public NameplateUI Nameplate;
        public WeaponController Weapon;         // 本地玩家的武器（特效用）
        public RemotePlayerMovement Remote;     // 远端玩家（特效/死亡表现用）
        public bool IsLocal;

        public Vector3 PrevPos, CurPos;          // 相邻逻辑帧位置（插值用）
        public float PrevYawDeg, CurYawDeg;
        public bool PrevGrounded = true;
        public int PrevReloadTicks;
        public int LastHp = int.MinValue;
    }

    private readonly Dictionary<int, View> _views = new();

    private readonly Diagnostics.Stopwatch _clock = new Diagnostics.Stopwatch();
    private double _accumulatorMs;
    private long _nextWallTick;                 // 下一个要推进到的墙钟帧号（1起）
    private long _lastConsumedEventFrame;       // 事件去重闸门（回滚重演的旧帧不再消费）

    // 输入采样（渲染帧刷新，逻辑帧消费）
    private PlayerInputController _input;
    private float _aimAngleDeg;
    private Vector2 _moveAxes;                  // 摄像机相对输入轴
    private bool _fireHeld, _sprintHeld, _jumpHeld, _reloadSticky;
    private CameraFollow _cameraFollow;

    private float TickSeconds => LockstepConfig.TICK_MS / 1000f;

    #region 会话启动

    /// <summary>
    /// 开局入口（NetworkHandler.OnGameStart 调用）。
    /// 种子为 0 或座位表为空 = 旧服务器未实现新链路 → 不启动，旧链路继续工作
    /// </summary>
    public static void BeginSession(ulong seed, int tickRate, IReadOnlyList<KeyValuePair<string, int>> seats)
    {
        if (seed == 0 || seats == null || seats.Count == 0)
        {
            Debug.Log("[LockstepDriver] 旧服务器（无种子），沿用旧状态同步链路");
            return;
        }
        if (tickRate > 0 && tickRate != LockstepConfig.TICK_RATE)
        {
            Debug.LogError($"[LockstepDriver] ❌ 逻辑帧率不一致：服务器 {tickRate} vs 本地 {LockstepConfig.TICK_RATE}，拒进");
            return;
        }
        if (Instance != null)
        {
            Debug.LogWarning("[LockstepDriver] ⚠️ 会话已存在（重开局），先销毁旧会话");
            Destroy(Instance.gameObject);
        }

        var go = new GameObject("LockstepGameDriver");
        var driver = go.AddComponent<LockstepGameDriver>();
        driver.InitSession(seed, seats);
    }

    private void InitSession(ulong seed, IReadOnlyList<KeyValuePair<string, int>> seats)
    {
        Instance = this;
        _localPlayerId = NetworkManager.Instance != null ? NetworkManager.Instance.GetLocalPlayerId() : "";

        // 座位表：string 玩家ID ↔ int 座位号
        foreach (var kv in seats)
        {
            _seatToPlayerId[kv.Value] = kv.Key;
            if (kv.Key == _localPlayerId) _localSeat = kv.Value;
        }
        if (_localSeat < 0)
        {
            Debug.LogError($"[LockstepDriver] ❌ 座位表中找不到本地玩家 [{_localPlayerId}]，回退旧链路");
            Destroy(gameObject);
            return;
        }

        // 逻辑世界：种子 + 按座位取确定性出生点（全端一致）
        var world = new LogicWorld(seed);
        for (int seat = 0; seat < seats.Count; seat++)
        {
            LogicWorld.GetSpawnPoint(seat, out var x, out var z);
            world.AddPlayer(seat, x, z, 0, LockstepConfig.MAX_HP);
        }
        _client = new LockstepClient(_localSeat, world);

        BuildViews();
        LockstepNetEvents.FrameInputsReceived += OnFrameInputs;

        _clock.Start();
        Debug.Log($"[LockstepDriver] ✅ 会话启动：座位{_localSeat}/{seats.Count}，种子{seed}，逻辑帧率{LockstepConfig.TICK_RATE}Hz");
    }

    /// <summary>建立座位 → 场景玩家对象的视图映射，并禁用旧链路组件</summary>
    private void BuildViews()
    {
        var playerManager = PlayerManager.GetInstance();
        var local = playerManager?.GetLocalPlayer();
        if (local == null)
        {
            Debug.LogError("[LockstepDriver] ❌ 找不到本地玩家对象（GameInitializer 未生成？）");
            return;
        }

        // 本地玩家：禁用旧移动/战斗（武器保留引用供特效调用）
        _input = local.GetComponent<PlayerInputController>();
        local.enabled = false;
        var localCombat = local.GetComponent<LocalPlayerCombat>();
        if (localCombat != null) localCombat.enabled = false;
        var localWeapon = local.GetComponentInChildren<WeaponController>();
        if (localWeapon != null) localWeapon.enabled = false;

        foreach (var kv in _seatToPlayerId)
        {
            int seat = kv.Key;
            string playerId = kv.Value;
            View view;
            if (playerId == _localPlayerId)
            {
                view = new View
                {
                    Transform = local.transform,
                    Anim = PlayerAnimationController.Resolve(local.transform),
                    Nameplate = local.GetComponentInChildren<NameplateUI>(),
                    Weapon = localWeapon,
                    IsLocal = true,
                };
            }
            else
            {
                var remote = playerManager?.GetRemotePlayer(playerId);
                if (remote == null)
                {
                    Debug.LogWarning($"[LockstepDriver] ⚠️ 找不到远端玩家对象 [{playerId}]（未加入？）");
                    continue;
                }
                remote.enabled = false; // 旧插值/校正下线，位置由逻辑驱动
                view = new View
                {
                    Transform = remote.transform,
                    Anim = PlayerAnimationController.Resolve(remote.transform),
                    Nameplate = remote.GetComponentInChildren<NameplateUI>(),
                    Remote = remote,
                };
            }

            // 初始插值状态 = 逻辑世界出生点（首帧不从 (0,0,0) 插过来）
            var lp0 = World?.GetPlayer(seat);
            if (lp0 != null)
            {
                view.CurPos = new Vector3(lp0.X.ToFloat(), lp0.Y.ToFloat(), lp0.Z.ToFloat());
                view.PrevPos = view.CurPos;
                view.CurYawDeg = FPAngle.ToDegrees(lp0.Yaw);
                view.PrevYawDeg = view.CurYawDeg;
                view.Transform.SetPositionAndRotation(view.CurPos, Quaternion.Euler(0f, view.CurYawDeg, 0f));
            }
            _views[seat] = view;
        }
    }

    private void OnDestroy()
    {
        LockstepNetEvents.FrameInputsReceived -= OnFrameInputs;
        if (Instance == this) Instance = null;
    }

    private void OnFrameInputs(long frame, IReadOnlyDictionary<int, PlayerCommand> inputs)
    {
        _client?.OnAuthoritativeInputs(frame, inputs);
    }

    #endregion

    #region 帧循环

    private void Update()
    {
        if (_client == null) return;

        SampleInput();

        // 固定步长推进（渲染帧率与逻辑帧率解耦）
        _accumulatorMs += Time.deltaTime * 1000.0;
        int guard = 0;
        while (_accumulatorMs >= LockstepConfig.TICK_MS && guard++ < 8)
        {
            _accumulatorMs -= LockstepConfig.TICK_MS;
            LogicTick(++_nextWallTick);
        }

        RenderFrame((float)(_accumulatorMs / LockstepConfig.TICK_MS));
    }

    /// <summary>渲染帧输入采样：瞄准射线 + 摄像机相对移动轴 + 按键（换弹边沿做粘性捕获）</summary>
    private void SampleInput()
    {
        if (_input == null) return;

        // 换弹是边沿触发（WasPressedThisFrame 只在渲染帧有效），粘性捕获到下一个逻辑帧
        if (_input.ReloadPressed) _reloadSticky = true;

        var req = _input.GetCurrentInput(0);
        _moveAxes = new Vector2(req.MoveX, req.MoveY);
        _fireHeld = _input.FireHeld;
        _sprintHeld = req.IsSprinting;
        _jumpHeld = req.Jump;

        UpdateAim();
    }

    /// <summary>鼠标 → 世界瞄准点 → 目标朝向角（与旧 LocalPlayerMovement.UpdateAim 同规则）</summary>
    private void UpdateAim()
    {
        var localView = GetLocalView();
        if (localView == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(_input.LookScreenPosition);

        Vector3 aimPoint;
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
        {
            aimPoint = hit.point;
        }
        else
        {
            var plane = new Plane(Vector3.up, Vector3.up * localView.Transform.position.y);
            if (!plane.Raycast(ray, out float d)) return;
            aimPoint = ray.GetPoint(d);
        }

        Vector3 flat = aimPoint - localView.Transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0004f) return; // 鼠标压在角色身上：保持上一帧朝向

        _aimAngleDeg = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
    }

    /// <summary>一个逻辑墙钟帧：提交未来帧指令 → 发包 → 推进 → 消费事件 → 捕获插值状态</summary>
    private void LogicTick(long wall)
    {
        // 1. 量化本地指令，生效帧 = 当前帧 + 输入延迟
        var cmd = new PlayerCommand
        {
            MoveX = 0,
            MoveZ = 0,
            AimAngle = FPAngle.FromDegrees(_aimAngleDeg),
            Buttons = BuildButtons(),
        };
        // 摄像机相对轴 → 世界空间轴（逻辑层只认世界空间）
        Vector3 worldMove = new Vector3(_moveAxes.x, 0f, _moveAxes.y);
        if (worldMove.sqrMagnitude > 0.0001f && Camera.main != null)
        {
            worldMove = Camera.main.transform.TransformDirection(worldMove);
            worldMove.y = 0f;
            worldMove.Normalize();
        }
        cmd.MoveX = PlayerCommand.QuantizeAxis(worldMove.x);
        cmd.MoveZ = PlayerCommand.QuantizeAxis(worldMove.z);
        _reloadSticky = false;

        _client.SubmitLocalCommand(wall + LockstepConfig.INPUT_DELAY_FRAMES, cmd);

        // 2. 待发指令 → 服务器
        var pending = _client.DrainPendingSends();
        if (pending.Count > 0 && NetworkManager.Instance != null && NetworkManager.Instance.IsConnected)
        {
            NetworkManager.Instance.Send(MsgID.C2SLockstepCommand, LockstepProtoConvert.ToCommandReq(pending));
        }

        // 3. 捕获推进前状态（插值起点 = 上一次的逻辑终点）
        foreach (var kv in _views) CaptureView(kv.Key, kv.Value, prev: true);

        // 4. 推进
        _client.Advance(wall);

        // 5. 从逻辑世界读回新状态 + 血量/换弹/生死表现
        foreach (var kv in _views)
        {
            CaptureView(kv.Key, kv.Value, prev: false);
            UpdateVitalsView(kv.Key, kv.Value);
        }

        // 6. 消费本帧事件（单调帧号闸门：回滚重演的旧帧不再消费）
        ConsumeEvents();
    }

    private byte BuildButtons()
    {
        byte b = 0;
        if (_fireHeld) b |= PlayerCommand.BTN_FIRE;
        if (_sprintHeld) b |= PlayerCommand.BTN_SPRINT;
        if (_reloadSticky) b |= PlayerCommand.BTN_RELOAD;
        if (_jumpHeld) b |= PlayerCommand.BTN_JUMP;
        return b;
    }

    private void CaptureView(int seat, View view, bool prev)
    {
        if (prev)
        {
            view.PrevPos = view.CurPos;
            view.PrevYawDeg = view.CurYawDeg;
            return;
        }
        var lp = World?.GetPlayer(seat);
        if (lp == null) return;
        view.CurPos = new Vector3(lp.X.ToFloat(), lp.Y.ToFloat(), lp.Z.ToFloat());
        view.CurYawDeg = FPAngle.ToDegrees(lp.Yaw);
    }

    private View GetLocalView()
    {
        return _views.TryGetValue(_localSeat, out var v) ? v : null;
    }

    #endregion

    #region 渲染与表现

    /// <summary>渲染插值：逻辑帧之间按 alpha 摆 Transform + 喂动画</summary>
    private void RenderFrame(float alpha)
    {
        foreach (var kv in _views)
        {
            var view = kv.Value;
            if (view.Transform == null) continue;

            Vector3 pos = Vector3.Lerp(view.PrevPos, view.CurPos, Mathf.Clamp01(alpha));
            float yaw = Mathf.LerpAngle(view.PrevYawDeg, view.CurYawDeg, Mathf.Clamp01(alpha));
            view.Transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            UpdateAnimationView(view);
        }
    }

    /// <summary>逐逻辑帧表现：血量名牌、换弹动画、生死状态、跳跃/落地</summary>
    private void UpdateVitalsView(int seat, View view)
    {
        var lp = World?.GetPlayer(seat);
        if (lp == null || view.Anim == null) return;

        if (view.Nameplate != null && lp.Hp != view.LastHp)
        {
            view.Nameplate.SetHp(lp.Hp);
            view.LastHp = lp.Hp;
        }

        if (view.PrevReloadTicks == 0 && lp.ReloadTicksLeft > 0)
            view.Anim.PlayReload();
        view.PrevReloadTicks = lp.ReloadTicksLeft;

        bool grounded = lp.IsGrounded;
        if (view.PrevGrounded && !grounded && lp.VerticalVelocity.Raw > 0) view.Anim.PlayJump();
        if (!view.PrevGrounded && grounded) view.Anim.PlayLand();
        view.PrevGrounded = grounded;
        view.Anim.SetGrounded(grounded);
        view.Anim.SetFalling(!grounded && lp.VerticalVelocity.ToFloat() * LockstepConfig.TICK_RATE < -0.5f);

        if (lp.IsDead && !view.Anim.IsDead) view.Anim.SetDead(true);
    }

    /// <summary>逐渲染帧动画参数：本地空间移动轴/速度/转向</summary>
    private void UpdateAnimationView(View view)
    {
        if (view.Anim == null) return;

        Vector3 delta = view.CurPos - view.PrevPos;
        delta.y = 0f;
        float speed = delta.magnitude / TickSeconds;

        // 世界位移 → 角色本地空间喂混合树（与旧链路同规则）
        Vector3 localDir = view.Transform.InverseTransformDirection(delta.normalized);
        if (delta.sqrMagnitude > 0.000001f)
            view.Anim.SetMoveInput(localDir.x, localDir.z);
        else
            view.Anim.SetMoveInput(0f, 0f);
        view.Anim.SetMoveSpeed(speed);

        float sprintThreshold = (5f + 8f) * 0.5f; // 走/跑速度中值，超阈视为冲刺
        view.Anim.ApplyMoveAnimSpeed(speed > sprintThreshold);

        float yawRate = Mathf.DeltaAngle(view.PrevYawDeg, view.CurYawDeg) / TickSeconds;
        view.Anim.SetTurn(Mathf.Clamp(yawRate / 720f, -1f, 1f));

        if (view.IsLocal) view.Anim.SetFiring(_fireHeld);
    }

    /// <summary>消费确定性事件：Fired=射手特效，Hit=受击反应，Died=死亡表现</summary>
    private void ConsumeEvents()
    {
        var events = World?.TickEvents;
        if (events == null || events.Count == 0) return;

        foreach (var ev in events)
        {
            if (ev.Frame <= _lastConsumedEventFrame) continue; // 回滚重演去重
            _lastConsumedEventFrame = ev.Frame;

            switch (ev.Type)
            {
                case LogicEventType.Fired:
                    OnFiredEvent(ev);
                    break;
                case LogicEventType.Hit:
                    OnHitEvent(ev);
                    break;
                case LogicEventType.Died:
                    OnDiedEvent(ev);
                    break;
            }
        }
    }

    private void OnFiredEvent(LogicEvent ev)
    {
        if (!_views.TryGetValue(ev.ShooterId, out var view) || view.Transform == null) return;

        Vector3 origin = view.IsLocal && view.Weapon != null && view.Weapon.muzzle != null
            ? view.Weapon.muzzle.position
            : view.Transform.position + view.Transform.forward * 0.6f + Vector3.up * 1f;
        Vector3 end = new Vector3(ev.HitX.ToFloat(), 1f, ev.HitZ.ToFloat());
        Vector3 dir = end - origin;
        if (dir.sqrMagnitude < 0.0001f) dir = view.Transform.forward;
        dir.Normalize();
        // 逻辑世界没有表面法线，弹孔朝向用 -dir 兜底（面向射手）
        Vector3 normal = ev.TargetId != 0 ? -dir : Vector3.up;

        if (view.IsLocal)
        {
            if (view.Weapon != null)
                view.Weapon.PlayFireVisual(origin, dir, end, ev.TargetId != 0, normal);
            view.Anim?.PlayFire();
            _cameraFollow ??= FindObjectOfType<CameraFollow>();
            _cameraFollow?.NotifyFired(new WeaponController.ShotInfo
            {
                Origin = origin,
                Direction = dir,
                EndPoint = end,
                HitNormal = normal,
            });
        }
        else if (view.Remote != null)
        {
            // 复用旧链路的远端开火表现（弹道/枪口/弹孔/开火动画）
            view.Remote.OnFireEvent(RemoteFiredEventData.Create(
                _seatToPlayerId.TryGetValue(ev.ShooterId, out var pid) ? pid : "",
                origin, dir, end, ev.TargetId != 0, "", normal));
        }
    }

    private void OnHitEvent(LogicEvent ev)
    {
        if (!_views.TryGetValue(ev.TargetId, out var view) || view.Transform == null) return;

        Vector3 shooterPos = new Vector3(ev.OriginX.ToFloat(), 1f, ev.OriginZ.ToFloat());

        if (view.IsLocal)
        {
            _cameraFollow ??= FindObjectOfType<CameraFollow>();
            _cameraFollow?.NotifyDamaged(ev.Damage, shooterPos);
            Vector2 localDir = view.Transform.InverseTransformDirection(shooterPos - view.Transform.position);
            view.Anim?.PlayDamage(localDir.normalized);
        }
        else
        {
            view.Remote?.PlayDamage(shooterPos);
        }
    }

    private void OnDiedEvent(LogicEvent ev)
    {
        if (!_views.TryGetValue(ev.TargetId, out var view)) return;

        string killer = _seatToPlayerId.TryGetValue(ev.ShooterId, out var k) ? k : "?";
        string victim = _seatToPlayerId.TryGetValue(ev.TargetId, out var v) ? v : "?";
        Debug.Log($"[LockstepDriver] ☠️ {victim} 被 {killer} 击杀（帧{ev.Frame}）");

        if (view.IsLocal)
        {
            view.Anim?.SetDead(true);
        }
        else
        {
            view.Remote?.SetDead(true);
        }
    }

    #endregion
}
