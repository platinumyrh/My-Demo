using System.Collections.Generic;
using GameLockstep;
using GameNetwork;
using UnityEngine;
using Diagnostics = System.Diagnostics;

/// <summary>
/// 帧同步会话驱动（表现层总编排，阶段7）
///
/// 职责：
/// - GameStart 带种子/座位表 → 构造 LogicWorld + LockstepClient
/// - 15Hz 固定步长推进：输入采样量化 → SubmitLocalCommand → Drain 发包 → Advance
/// - 渲染插值：Transform 在前后两个逻辑帧状态间按 accumulator 比例插值（本地/远端同一套）
/// - LogicEvent 消费：开火特效/受击动画/死亡/换弹表现（回滚重演的帧用单调帧号闸门去重）
/// - 表现层只读取 PlayerView / 动画 / 名牌 / 武器特效入口
///
/// 网上只有指令；位置/血量/开火结果全部来自本地确定性模拟
/// </summary>
public class LockstepGameDriver : MonoBehaviour
{
    public static LockstepGameDriver Instance { get; private set; }

    private LockstepClient _client;
    private LogicWorld World => _client?.World;

    // 单机模式：没有服务器，由本地封帧器回喂权威帧
    private OfflineLockstepRelay _offlineRelay;

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
        public PlayerView Player;                // 表现层视图（远端特效/受击/死亡入口）
        public bool IsLocal;

        public Vector3 PrevPos, CurPos;          // 相邻逻辑帧位置（插值用）
        public float PrevYawDeg, CurYawDeg;      // 瞄准角（逻辑朝向）：驱动头部
        public float TargetBodyYawDeg;           // 身体目标朝向：逻辑帧按位移算，45° 吸附
        public float VisualBodyYawDeg;           // 身体表现朝向：渲染帧按角速度追目标
        public float TurnBlend;                  // 转身片段混合权重（已平滑）
        public bool Dead;
        public bool PrevGrounded = true;
        public int PrevReloadTicks;
        public int LastHp = int.MinValue;
    }

    private readonly Dictionary<int, View> _views = new();//座位 → 表现层视图

    // GameStart 可能先于 Main 场景加载：先缓存，等 GameInitializer 建好 PlayerView 后再真正启动
    private static bool _hasPendingSession;
    private static ulong _pendingSeed;
    private static int _pendingTickRate;
    private static long _pendingStartMs;
    private static List<KeyValuePair<string, int>> _pendingSeats;

    private readonly Diagnostics.Stopwatch _clock = new Diagnostics.Stopwatch();
    private double _accumulatorMs;
    private long _nextWallTick;                 // 下一个要推进到的墙钟帧号（1起）
    private long _serverStartMs;                // 联机开赛时刻（服务器墙钟）；0 = 单机自由时钟
    private long _lastConsumedEventFrame;       // 事件去重闸门（回滚重演的旧帧不再消费）

    // 输入采样（渲染帧刷新，逻辑帧消费）
    private PlayerInputController _input;
    private float _aimAngleDeg;
    private Vector2 _moveAxes;                  // 摄像机相对输入轴
    private bool _fireHeld, _sprintHeld, _jumpHeld, _reloadSticky;
    private CameraFollow _cameraFollow;
    private bool _snapCameraPending;            // 复活瞬移：等插值写完玩家位置后再摆摄像机
    private ScoreboardUI _scoreboard;

    private float TickSeconds => LockstepConfig.TICK_MS / 1000f;

    [Header("身体朝向（纯表现，不改逻辑和瞄准）")]
    [Tooltip("站立时身体转向角速度（度/秒），45° 约需 45/该值 秒")]
    [SerializeField] private float bodyTurnRateIdle = 270f;

    [Tooltip("移动时身体转向角速度（度/秒）：跑动中不该慢慢转身")]
    [SerializeField] private float bodyTurnRateMoving = 360f;

    [Tooltip("转身片段最多占多少权重（0~1）。混合树的轴端点 ±0.5 才是 100% 转身片段，" +
             "而且片段自带旋转会和根节点叠加，别给太满；调到 0 就是完全不用转身片段")]
    [SerializeField, Range(0f, 1f)] private float turnClipMaxWeight = 0.25f;

    [Tooltip("转身权重平滑时间（秒）")]
    [SerializeField] private float turnBlendSmoothTime = 0.06f;

    /// <summary>角速度到这个值时 turn 混合参数顶满（对应权重上限）</summary>
    private const float TURN_FULL_DEG_PER_SEC = 360f;

    #region 会话启动

    /// <summary>
    /// 开局入口（NetworkHandler.OnGameStart 调用）。
    /// 种子为 0 或座位表为空 = 服务器未提供 lockstep 初始化数据 → 不启动。
    /// serverStartMs = 服务器约定的开赛时刻（全端同一时间轴；0 = 单机，不等）
    /// </summary>
    public static void BeginSession(ulong seed, int tickRate, IReadOnlyList<KeyValuePair<string, int>> seats, long serverStartMs = 0)
    {
        if (seed == 0 || seats == null || seats.Count == 0)
        {
            Debug.LogWarning("[LockstepDriver] 服务器未提供 lockstep 种子/座位表，无法启动帧同步");
            return;
        }
        if (tickRate > 0 && tickRate != LockstepConfig.TICK_RATE)
        {
            Debug.LogError($"[LockstepDriver] ❌ 逻辑帧率不一致：服务器 {tickRate} vs 本地 {LockstepConfig.TICK_RATE}，拒进");
            return;
        }

        _pendingSeed = seed;
        _pendingTickRate = tickRate;
        _pendingStartMs = serverStartMs;
        _pendingSeats = new List<KeyValuePair<string, int>>(seats);
        _hasPendingSession = true;
        TryStartPendingSession();
    }

    /// <summary>Main 场景生成 PlayerView 后调用；GameStart 先到时会在这里补启动。</summary>
    public static void TryStartPendingSession()
    {
        if (!_hasPendingSession || Instance != null) return;
        if (PlayerManager.GetInstance()?.GetLocalPlayer() == null) return;

        _hasPendingSession = false;
        var go = new GameObject("LockstepGameDriver");
        var driver = go.AddComponent<LockstepGameDriver>();
        driver.InitSession(_pendingSeed, _pendingSeats, _pendingStartMs);
    }

    private void InitSession(ulong seed, IReadOnlyList<KeyValuePair<string, int>> seats, long serverStartMs)
    {
        Instance = this;
        _serverStartMs = serverStartMs;
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

        if (OfflinePlaySession.IsActive)
        {
            var seatIds = new List<int>(seats.Count);
            foreach (var kv in seats) seatIds.Add(kv.Value);
            _offlineRelay = new OfflineLockstepRelay(seatIds);
        }

        BuildViews();
        InitScoreboard();
        GameEventBus.GetInstance().Subscribe<PlayerLeftRoomEventData>(
            GameEventType.OnPlayerLeftRoom, OnPlayerLeftRoom);
        LockstepNetEvents.FrameInputsReceived += OnFrameInputs;

        _clock.Start();

        if (_serverStartMs > 0)
        {
            long lateMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _serverStartMs;
            if (lateMs > LockstepConfig.TICK_MS * 2)
                Debug.LogWarning($"[LockstepDriver] ⚠️ 比开赛时刻晚 {lateMs}ms 才建好会话，早封的权威帧不会重发，帧号会断档");
        }

        Debug.Log($"[LockstepDriver] ✅ 会话启动：座位{_localSeat}/{seats.Count}，种子{seed}，逻辑帧率{LockstepConfig.TICK_RATE}Hz" +
                  (_serverStartMs > 0 ? $"，等待统一开赛时刻 {_serverStartMs}" : "，单机模式不等开赛"));
    }

    /// <summary>建立座位 → 场景玩家对象的视图映射</summary>
    private void BuildViews()
    {
        var playerManager = PlayerManager.GetInstance();
        var local = playerManager?.GetLocalPlayer();
        if (local == null)
        {
            Debug.LogError("[LockstepDriver] ❌ 找不到本地玩家对象（GameInitializer 未生成？）");
            return;
        }

        // 本地玩家：输入仍由 PlayerInputController 采样；武器只保留特效接口
        _input = local.GetComponent<PlayerInputController>();
        var localWeapon = local.Weapon;

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
                    Anim = local.Anim,
                    Nameplate = local.Nameplate,
                    Weapon = localWeapon,
                    Player = local,
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
                view = new View
                {
                    Transform = remote.transform,
                    Anim = remote.Anim,
                    Nameplate = remote.Nameplate,
                    Weapon = remote.Weapon,
                    Player = remote,
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
                view.TargetBodyYawDeg = view.CurYawDeg;   // 出生时身体朝向 = 逻辑朝向
                view.VisualBodyYawDeg = view.TargetBodyYawDeg;
                view.Transform.SetPositionAndRotation(view.CurPos, Quaternion.Euler(0f, view.VisualBodyYawDeg, 0f));
            }
            _views[seat] = view;
        }
    }

    /// <summary>计分板：座位→显示名在开赛时定一次，数值每逻辑帧从逻辑世界推</summary>
    private void InitScoreboard()
    {
        _scoreboard = FindObjectOfType<ScoreboardUI>(true);
        if (_scoreboard == null)
        {
            var panel = GameObject.Find("Canvas_ScreenSpace/Panel_PlayerScore");
            if (panel == null)
            {
                Debug.LogWarning("[LockstepDriver] ⚠️ 没找到计分面板 Canvas_ScreenSpace/Panel_PlayerScore");
                return;
            }
            _scoreboard = panel.AddComponent<ScoreboardUI>();
        }

        var names = new Dictionary<int, string>();
        foreach (var kv in _seatToPlayerId)
        {
            string name = kv.Value;
            if (_views.TryGetValue(kv.Key, out var view) && view.Player != null &&
                !string.IsNullOrEmpty(view.Player.DisplayName))
            {
                name = view.Player.DisplayName;
            }
            names[kv.Key] = name;
        }

        _scoreboard.Build(names, _localSeat);
    }

    private void UpdateScoreboard()
    {
        if (_scoreboard == null) return;

        var players = World?.Players;
        if (players == null) return;

        for (int i = 0; i < players.Count; i++)
            _scoreboard.SetEntry(players[i].PlayerId, players[i].Kills, players[i].Deaths);
    }

    private void OnDestroy()
    {
        _scoreboard?.Clear();
        GameEventBus.GetInstance().Unsubscribe<PlayerLeftRoomEventData>(
            GameEventType.OnPlayerLeftRoom, OnPlayerLeftRoom);
        LockstepNetEvents.FrameInputsReceived -= OnFrameInputs;
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 对局中有人主动退出：座位和逻辑数据整局保留，只是不再有真值输入。
    /// 表现层把计分表该行隐藏，模型继续由逻辑世界驱动（不能销毁视图）。
    /// </summary>
    private void OnPlayerLeftRoom(PlayerLeftRoomEventData eventData)
    {
        foreach (var kv in _seatToPlayerId)
        {
            if (kv.Value != eventData.PlayerId) continue;

            _scoreboard?.SetEntryVisible(kv.Key, false);
            Debug.Log($"[LockstepDriver] 🚪 玩家 {eventData.PlayerId} 退出对局，座位 {kv.Key} 保留并补空输入");
            return;
        }
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

        SampleInput();//读取输入为全局位置相关变量赋值

        // 联机：全端等同一个开赛时刻再起表，帧号从 1 开始（服务器的封帧时钟也从这一刻起算）。
        // 提前推进会让各端帧号错位，服务器会把我们的指令当迟到包丢弃
        if (_serverStartMs > 0 &&
            System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _serverStartMs)
        {
            RenderFrame(1f);
            return;
        }

        // 固定步长推进（渲染帧率与逻辑帧率解耦）
        _accumulatorMs += Time.deltaTime * 1000.0;
        int guard = 0;
        while (_accumulatorMs >= LockstepConfig.TICK_MS && guard++ < 8)
        {
            _accumulatorMs -= LockstepConfig.TICK_MS;
            LogicTick(++_nextWallTick);//进行逻辑帧更新
        }

        RenderFrame((float)(_accumulatorMs / LockstepConfig.TICK_MS));
    }

    /// <summary>渲染帧输入采样：瞄准射线 + 摄像机相对移动轴 + 按键（换弹边沿做粘性捕获）</summary>
    private void SampleInput()
    {
        if (_input == null) return;

        // 菜单打开：人物停手停脚，但会话照常推进——帧同步不能把时钟停掉，
        // 否则服务端继续封帧，错过的权威帧不会补发
        if (GameMenuUI.Instance != null && GameMenuUI.Instance.IsOpen)
        {
            _moveAxes = Vector2.zero;
            _fireHeld = false;
            _sprintHeld = false;
            _jumpHeld = false;
            _reloadSticky = false;
            return;
        }

        // 换弹是边沿触发（WasPressedThisFrame 只在渲染帧有效），粘性捕获到下一个逻辑帧
        if (_input.ReloadPressed) _reloadSticky = true;

        _moveAxes = _input.MoveAxes;
        _fireHeld = _input.FireHeld;
        _sprintHeld = _input.SprintHeld;
        _jumpHeld = _input.JumpHeld;

        UpdateAim();
    }

    /// <summary>鼠标 → 世界瞄准点 → 目标朝向角</summary>
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
        //为本帧的逻辑帧生成指令，提交给客户端
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
        

        // 2. 待发指令 → 服务器（单机模式改为本地封帧后直接回喂）
        var pending = _client.DrainPendingSends();
        if (_offlineRelay != null)
        {
            _offlineRelay.Submit(pending, _localSeat);
            _offlineRelay.SealUpTo(wall, _client.OnAuthoritativeInputs);
        }
        else if (pending.Count > 0 && NetworkManager.Instance != null && NetworkManager.Instance.IsConnected)
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
        UpdateScoreboard();

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

        bool wasDead = view.Dead;
        view.CurPos = new Vector3(lp.X.ToFloat(), lp.Y.ToFloat(), lp.Z.ToFloat());
        view.CurYawDeg = FPAngle.ToDegrees(lp.Yaw);
        view.Dead = lp.IsDead;

        // 生死切换：插值起点贴到当前状态，别让模型从尸体位置滑到出生点
        if (wasDead != lp.IsDead)
        {
            view.PrevPos = view.CurPos;
            view.PrevYawDeg = view.CurYawDeg;

            if (!lp.IsDead)
            {
                view.TargetBodyYawDeg = view.CurYawDeg;
                view.VisualBodyYawDeg = view.CurYawDeg;
                if (view.IsLocal) _snapCameraPending = true;
            }
            return;
        }

        // 身体只跟位移：原地转准星不再带着全身转；静止/死亡保持上一次朝向
        Vector3 moved = view.CurPos - view.PrevPos;
        moved.y = 0f;
        if (!lp.IsDead && moved.sqrMagnitude > 0.0004f) // 0.02m/帧，滤掉碰撞推挤的碎位移
        {
            float deg = Mathf.Atan2(moved.x, moved.z) * Mathf.Rad2Deg;
            view.TargetBodyYawDeg = Mathf.Round(deg / 45f) * 45f; // 八向吸附
        }
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
        float t = Mathf.Clamp01(alpha);
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        foreach (var kv in _views)
        {
            var view = kv.Value;
            if (view.Transform == null) continue;

            Vector3 pos = Vector3.Lerp(view.PrevPos, view.CurPos, t);

            Vector3 delta = view.CurPos - view.PrevPos;
            delta.y = 0f;
            float speed = delta.magnitude / TickSeconds;

            // 身体朝向按角速度追目标：不在一个逻辑帧里瞬间转完（渲染帧推进，和 15Hz 解耦）
            float turnRate = Mathf.Lerp(bodyTurnRateIdle, bodyTurnRateMoving,
                                        Mathf.InverseLerp(0f, 5f, speed));
            float prevBodyYaw = view.VisualBodyYawDeg;
            if (!view.Dead)
                view.VisualBodyYawDeg = Mathf.MoveTowardsAngle(prevBodyYaw, view.TargetBodyYawDeg,
                                                               turnRate * dt);
            float bodyYawVel = Mathf.DeltaAngle(prevBodyYaw, view.VisualBodyYawDeg) / dt;

            view.Transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, view.VisualBodyYawDeg, 0f));

            // 头（连枪一起）单独追准星；死亡后交还给死亡动画
            if (view.Player != null)
            {
                if (view.Dead) view.Player.ClearHeadAim();
                else view.Player.SetHeadAim(Mathf.LerpAngle(view.PrevYawDeg, view.CurYawDeg, t));
            }

            UpdateAnimationView(view, delta, speed, bodyYawVel, dt);
        }

        // 复活瞬移：插值已把本地玩家摆到出生点，此时摄像机再直接落位
        if (_snapCameraPending)
        {
            _snapCameraPending = false;
            SnapCameraToTarget();
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
        {
            view.Anim.PlayReload();
            view.Weapon?.PlayReloadVisual();
        }
        view.PrevReloadTicks = lp.ReloadTicksLeft;

        if (view.IsLocal)
        {
            float reloadProgress = lp.ReloadTicksLeft > 0
                ? 1f - (float)lp.ReloadTicksLeft / LockstepConfig.RELOAD_TICKS
                : 1f;
            CrosshairUI.Instance?.SetReloadProgress(reloadProgress);
        }

        bool grounded = lp.IsGrounded;
        if (view.PrevGrounded && !grounded && lp.VerticalVelocity.Raw > 0) view.Anim.PlayJump();
        if (!view.PrevGrounded && grounded) view.Anim.PlayLand();
        view.PrevGrounded = grounded;
        view.Anim.SetGrounded(grounded);
        view.Anim.SetFalling(!grounded && lp.VerticalVelocity.ToFloat() * LockstepConfig.TICK_RATE < -0.5f);

        if (lp.IsDead && !view.Anim.IsDead) view.Anim.SetDead(true);
        else if (!lp.IsDead && view.Anim.IsDead) view.Anim.SetDead(false);
    }

    /// <summary>本地玩家瞬移（复活）：摄像机直接落位，不做平滑追赶</summary>
    private void SnapCameraToTarget()
    {
        _cameraFollow ??= FindObjectOfType<CameraFollow>();
        _cameraFollow?.SnapToTarget();
    }

    /// <summary>逐渲染帧动画参数：本地空间移动轴/速度/转身混合</summary>
    private void UpdateAnimationView(View view, Vector3 delta, float speed, float bodyYawVel, float dt)
    {
        if (view.Anim == null) return;

        // 世界位移 → 当前身体朝向的本地空间（转身途中 localDir 从斜后摆到正前，二维树自然演成拐弯）
        Vector3 localDir = view.Transform.InverseTransformDirection(delta.normalized);
        if (delta.sqrMagnitude > 0.000001f)
            view.Anim.SetMoveInput(localDir.x, localDir.z);
        else
            view.Anim.SetMoveInput(0f, 0f);
        view.Anim.SetMoveSpeed(speed);

        float sprintThreshold = (5f + 8f) * 0.5f; // 走/跑速度中值，超阈视为冲刺
        view.Anim.ApplyMoveAnimSpeed(speed > sprintThreshold);

        // turn 是 Locomotion 混合树的轴：-0.5 左手原地转 / 0 移动树 / +0.5 右手原地转，
        // 所以参数 = 目标片段权重 × 0.5。片段自带旋转，这里只借一部分补脚步，朝向由根节点负责
        float turnTarget = view.Dead
            ? 0f
            : Mathf.Clamp(bodyYawVel / TURN_FULL_DEG_PER_SEC, -1f, 1f) * turnClipMaxWeight * 0.5f;
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, turnBlendSmoothTime));
        view.TurnBlend = Mathf.Lerp(view.TurnBlend, turnTarget, k);
        view.Anim.SetTurn(view.TurnBlend);

        if (view.IsLocal) view.Anim.SetFiring(_fireHeld);
    }

    /// <summary>消费确定性事件：Fired=射手特效，Hit=受击反应，Died=死亡表现</summary>
    private void ConsumeEvents()
    {
        var events = World?.TickEvents;
        if (events == null || events.Count == 0) return;

        // 去重按帧，不按事件：同一帧里可能有 Fired→Hit→Died 多条，
        // 逐条推进闸门会把同帧后面的 Hit/Died 全吃掉。
        // 回滚重演只保证帧不重复，所以"整帧只播一次"就足够
        long frame = World.Frame;
        if (frame <= _lastConsumedEventFrame) return;
        _lastConsumedEventFrame = frame;

        foreach (var ev in events)
        {
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

        // 枪口统一取模型上的 muzzle（Head 的子物体，跟着瞄准转）。
        // 远端以前用的是 Transform.forward 推出来的假枪口，头身分离后 forward 变成了身体朝向，
        // 准星在侧面时弹道起点就偏出枪口，看起来"歪"
        Vector3 origin = view.Weapon != null && view.Weapon.muzzle != null
            ? view.Weapon.muzzle.position
            : view.Transform.position + Vector3.up * 1f;
        Vector3 end = new Vector3(ev.HitX.ToFloat(), 1f, ev.HitZ.ToFloat());
        Vector3 dir = end - origin;
        if (dir.sqrMagnitude < 0.0001f) dir = view.Transform.forward;
        dir.Normalize();

        // 墙面法线由逻辑层给出；打中玩家时没有受击法线，用 -dir 兜底（面向射手）
        Vector3 normal;
        if (ev.HitWall) normal = new Vector3(ev.HitNX.ToFloat(), 0f, ev.HitNZ.ToFloat());
        else if (ev.TargetId != 0) normal = -dir;
        else normal = Vector3.up;
        // 斜墙的法线是带小数的定点单位向量，这里再归一一次，弹孔贴面才不会歪
        if (normal.sqrMagnitude > 0.0001f) normal.Normalize();

        bool hitSomething = ev.TargetId != 0 || ev.HitWall;

        if (view.IsLocal)
        {
            if (view.Weapon != null)
                view.Weapon.PlayFireVisual(origin, dir, end, hitSomething, normal);
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
        else
        {
            // 远端表现：只播弹道/枪口/弹孔/开火动画，不参与命中判定
            view.Player?.PlayFireVisual(origin, dir, end, hitSomething, normal);
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
            CrosshairUI.Instance?.ShowHit();
            Vector2 localDir = view.Transform.InverseTransformDirection(shooterPos - view.Transform.position);
            view.Anim?.PlayDamage(localDir.normalized);
        }
        else
        {
            view.Player?.PlayDamage(shooterPos);
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
            view.Player?.SetDead(true);
        }

        if (ev.ShooterId == _localSeat)
            CrosshairUI.Instance?.ShowKill();
    }

    #endregion
}
