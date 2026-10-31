using System.Collections.Generic;
using GameLockstep;
using UnityEngine;
using Diagnostics = System.Diagnostics;

/// <summary>
/// 帧同步会话驱动：世界推进 + 表现层。
///
/// 职责：
/// - 15Hz 固定步长推进 LockstepClient（输入由 LocalInputSampler 提供，网络收发由 LockstepNetworkBridge 负责）
/// - 渲染插值：Transform 在前后两个逻辑帧状态间按 accumulator 比例插值（本地/远端同一套）
/// - LogicEvent 消费：开火特效/受击动画/死亡/换弹表现（会话按帧/类型/射手去重后排队，回滚重演不会丢事件）
/// - 表现层只读取 PlayerView / 动画 / 名牌 / 武器特效入口
///
/// 网上只有指令；位置/血量/开火结果全部来自本地确定性模拟
/// </summary>
public class LockstepGameDriver : MonoBehaviour
{
    public static LockstepGameDriver Instance { get; private set; }

    private ILockstepSession _session;
    private LogicWorld World => _session?.World;
    private bool _subscribedRoomEvents;

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
        public Vector3 LastMoveDir;              // 上一次被采纳的移动方向（识别回滚纠错用）
        public int ReversalFrames;               // 连续反向位移帧数：回滚帧里第一次反向当纠错，第二次才是真转身
        public float TurnBlend;                  // 转身片段混合权重（已平滑）
        public bool Dead;
        public bool PrevGrounded = true;
        public int PrevReloadTicks;
        public int LastHp = int.MinValue;
        public int LastAmmo = int.MinValue;
    }

    private readonly Dictionary<int, View> _views = new();//座位 → 表现层视图

    private readonly Diagnostics.Stopwatch _clock = new Diagnostics.Stopwatch();
    private double _accumulatorMs;
    private long _logicFrame;                   // 本地已推进到的逻辑帧号（1起）
    private bool _serverDriven;                 // 联机：帧号跟着服务端封帧走；单机/回放：本地累加器自由跑
    private bool _matchStarted;                 // 联机：已等到第一个权威帧并完成起步补帧

    // 输入采样（渲染帧刷新，逻辑帧消费）
    private LocalInputSampler _inputSampler;
    private CameraFollow _cameraFollow;
    private bool _snapCameraPending;            // 复活瞬移：等插值写完玩家位置后再摆摄像机
    private ScoreboardUI _scoreboard;

    private float TickSeconds => LockstepConfig.TICK_MS / 1000f;

    /// <summary>单个渲染帧最多补多少逻辑帧（重连追帧/卡顿恢复），防主线程卡死</summary>
    private const int MAX_CATCHUP_TICKS_PER_RENDER = 32;

    /// <summary>单个渲染帧累加器最多推进多少帧（长时间卡顿时 deltaTime 会给出大跳）</summary>
    private const int MAX_ACCUMULATOR_TICKS_PER_RENDER = 8;

    /// <summary>补帧要领先服务端已封帧多少帧：服务端在帧时隙开始 FRAME_TIMEOUT_MS 后才强行封帧，
    /// 所以"已封到第 latest 帧"时，服务器时间轴其实已经走到 latest + 这个值了</summary>
    private const int CATCHUP_LEAD_FRAMES = LockstepConfig.FRAME_TIMEOUT_MS / LockstepConfig.TICK_MS;

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
    /// 接收已构造好的逻辑世界并启动会话。
    /// 种子/座位表 → 世界由 LockstepSessionBootstrap 负责，本类不关心消息从哪来。
    /// online = 联机（等服务器封出首帧后按封帧时间轴推进）；false = 单机（本地累加器自由跑）
    /// </summary>
    internal void InitSession(ulong seed, LogicWorld world, IReadOnlyList<KeyValuePair<string, int>> seats,
                             int localSeat, bool online)
    {
        Instance = this;
        _serverDriven = online;
        SetSeats(seats, localSeat);

        var seatIds = new List<int>(seats.Count);
        foreach (var kv in seats) seatIds.Add(kv.Value);
        _session = new LiveLockstepSession(
            new LockstepClient(localSeat, world), localSeat, OfflinePlaySession.IsActive, seatIds);

        BuildViews();
        InitScoreboard();
        GameEventBus.GetInstance().Subscribe<PlayerLeftRoomEventData>(
            GameEventType.OnPlayerLeftRoom, OnPlayerLeftRoom);
        GameEventBus.GetInstance().Subscribe<PlayerJoinedRoomEventData>(
            GameEventType.OnPlayerJoinedRoom, OnPlayerJoinedRoom);
        _subscribedRoomEvents = true;

        _clock.Start();

        Debug.Log($"[LockstepDriver] ✅ 会话启动：座位{_localSeat}/{seats.Count}，种子{seed}，逻辑帧率{LockstepConfig.TICK_RATE}Hz" +
                  (online ? "，联机：等服务器封出首帧" : "，单机：本地累加器"));
    }

    /// <summary>
    /// 断线重连：world 已按服务端历史快进到 resumeFrame。
    /// 从这里接回实时会话——不重新采样历史输入，只把各座位最后一条权威指令交给预测层。
    /// </summary>
    internal void InitReconnectedSession(LogicWorld world, IReadOnlyList<KeyValuePair<string, int>> seats,
                                         int localSeat, long resumeFrame,
                                         IReadOnlyDictionary<int, PlayerCommand> lastKnown)
    {
        Instance = this;
        _serverDriven = true;
        SetSeats(seats, localSeat);

        var seatIds = new List<int>(seats.Count);
        foreach (var kv in seats) seatIds.Add(kv.Value);
        _session = new LiveLockstepSession(world, localSeat, false, seatIds, resumeFrame, lastKnown);

        BuildViews();
        InitScoreboard();
        GameEventBus.GetInstance().Subscribe<PlayerLeftRoomEventData>(
            GameEventType.OnPlayerLeftRoom, OnPlayerLeftRoom);
        GameEventBus.GetInstance().Subscribe<PlayerJoinedRoomEventData>(
            GameEventType.OnPlayerJoinedRoom, OnPlayerJoinedRoom);
        _subscribedRoomEvents = true;

        _clock.Start();
        _logicFrame = resumeFrame;            // 下一逻辑帧 = 历史末尾 + 1
        _accumulatorMs = 0;                   // 补帧期间的历史事件不进会话队列，不用额外屏蔽

        Debug.Log($"[LockstepDriver] 🔌 重连恢复：座位{localSeat}/{seats.Count}，从帧 {resumeFrame} 接回实时循环");
    }

    /// <summary>回放会话：不采样输入、不连网络，由 ReplayLockstepSession 按录像逐帧重演</summary>
    internal void InitReplaySession(ReplayData data, LogicWorld world, int localSeat)
    {
        Instance = this;
        _serverDriven = false;

        var seats = new List<KeyValuePair<string, int>>(data.Seats.Count);
        foreach (var s in data.Seats)
            seats.Add(new KeyValuePair<string, int>(s.PlayerId, s.Seat));
        SetSeats(seats, localSeat);

        _session = new ReplayLockstepSession(data, world, localSeat);
        BuildViews();
        InitScoreboard();
        _clock.Start();

        if (data.TickRate > 0 && data.TickRate != LockstepConfig.TICK_RATE)
            Debug.LogWarning($"[LockstepDriver] ⚠️ 录像帧率 {data.TickRate}Hz 与本地 {LockstepConfig.TICK_RATE}Hz 不同，回放速度会有偏差");

        Debug.Log($"[LockstepDriver] ▶️ 回放开始：{data.Seats.Count} 个座位，{data.Frames.Count} 帧，" +
                  $"种子{data.Seed}，文件 {data.FilePath}");
    }

    private void SetSeats(IReadOnlyList<KeyValuePair<string, int>> seats, int localSeat)
    {
        _localSeat = localSeat;
        foreach (var kv in seats)
            _seatToPlayerId[kv.Value] = kv.Key;
    }

    /// <summary>建立座位 → 场景玩家对象的视图映射</summary>
    private void BuildViews()
    {
        var playerManager = PlayerManager.GetInstance();
        var local = playerManager?.GetLocalPlayer();
        _inputSampler = new LocalInputSampler();
        if (local == null)
        {
            Debug.LogError("[LockstepDriver] ❌ 找不到本地玩家对象（GameInitializer 未生成？）");
            return;
        }

        // 本地玩家：输入仍由 PlayerInputController 采样；武器只保留特效接口
        _inputSampler.Bind(local.GetComponent<PlayerInputController>(), local.transform);
        var localWeapon = local.Weapon;

        foreach (var kv in _seatToPlayerId)
        {
            int seat = kv.Key;
            string playerId = kv.Value;
            View view;
            if (seat == _localSeat)
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
        _session?.Dispose();
        if (_subscribedRoomEvents)
        {
            GameEventBus.GetInstance().Unsubscribe<PlayerLeftRoomEventData>(
                GameEventType.OnPlayerLeftRoom, OnPlayerLeftRoom);
            GameEventBus.GetInstance().Unsubscribe<PlayerJoinedRoomEventData>(
                GameEventType.OnPlayerJoinedRoom, OnPlayerJoinedRoom);
            _subscribedRoomEvents = false;
        }
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

    /// <summary>掉线的人重连回来：座位一直是它自己的，把计分表该行重新显示即可</summary>
    private void OnPlayerJoinedRoom(PlayerJoinedRoomEventData eventData)
    {
        foreach (var kv in _seatToPlayerId)
        {
            if (kv.Value != eventData.PlayerId) continue;

            _scoreboard?.SetEntryVisible(kv.Key, true);
            Debug.Log($"[LockstepDriver] 🔌 玩家 {eventData.PlayerId} 重连回对局，恢复座位 {kv.Key} 的计分显示");
            return;
        }
    }

    #endregion

    #region 帧循环

    private void Update()
    {
        if (_session == null) return;

        // 回放不采样本地输入；实时会话菜单打开时人物停手停脚，但时间轴照常推进
        if (!_session.IsReplay)
            _inputSampler?.Sample(GameMenuUI.Instance != null && GameMenuUI.Instance.IsOpen);

        if (_serverDriven)
        {
            UpdateServerDriven();
            return;
        }

        // 单机/回放：没有服务器可对齐，本地累加器按 15Hz 自由推进
        TickAccumulator(MAX_ACCUMULATOR_TICKS_PER_RENDER);
        RenderFrame((float)(_accumulatorMs / LockstepConfig.TICK_MS));
    }

    /// <summary>
    /// 联机帧时钟：帧号跟着服务端封出来的帧走，不用本机 UTC 时钟算。
    ///
    /// 服务端封帧泵按它自己的墙钟跑（全员指令到齐立即封，到期还没齐就补空封），
    /// 所以"已封到第 latest 帧"就是全端共同的逻辑时间轴。本地只做两件事：
    /// - 平时按累加器 15Hz 往前推（领先封帧 2 帧提交指令，正好卡在输入延迟上）
    /// - 落后于服务端（卡顿/网络抖动/重连接回）就补帧追到 latest + 超时帧数，
    ///   把"提交指令要领先封帧"的相位一起补回来
    ///
    /// 本机时钟和服务器差多少都不影响帧号：帧号偏了，提交的指令就会全被服务端当成过时帧丢掉
    /// </summary>
    private void UpdateServerDriven()
    {
        long latest = _session.LatestServerFrame;

        if (!_matchStarted)
        {
            if (latest <= 0)
            {
                RenderFrame(1f); // 服务器还没封出第一帧：对局未开始
                return;
            }

            // 起步补帧：第 1、2 帧没人提交（输入延迟 2 帧，最早只提交第 3 帧），
            // 只可能是到期补空封出来的——收到第一帧时，服务器时间轴已经走到 latest + 超时帧数。
            // 补到那里，本地帧号才和服务器同步，后续提交才不会被判成过时帧
            long startTarget = latest + CATCHUP_LEAD_FRAMES;
            int lead = 0;
            while (_logicFrame < startTarget && lead++ < MAX_CATCHUP_TICKS_PER_RENDER)
                TickFor(++_logicFrame, latest);
            if (_logicFrame < startTarget)
            {
                RenderFrame(1f);
                return; // 没补完，下一渲染帧接着补
            }

            _matchStarted = true;
            Debug.Log($"[LockstepDriver] ▶️ 服务端已封到帧 {latest}，本地补到帧 {_logicFrame} 后开始推进");
        }

        TickAccumulator(MAX_ACCUMULATOR_TICKS_PER_RENDER);

        // 落后补帧：只追平已封帧还不够——服务端是"帧时隙开始 200ms 后才超时封帧"，
        // 追平后提交的指令会晚于封帧时刻，照样被当成过时帧。所以要补到 latest + 超时帧数
        if (_logicFrame < latest - LockstepConfig.INPUT_DELAY_FRAMES)
        {
            long catchUpTarget = latest + CATCHUP_LEAD_FRAMES;
            int guard = 0;
            while (_logicFrame < catchUpTarget && guard++ < MAX_CATCHUP_TICKS_PER_RENDER)
            {
                _accumulatorMs = 0; // 补帧已经吃掉了这段时间，别再让累加器叠加推进
                TickFor(++_logicFrame, latest);
            }
        }

        RenderFrame((float)(_accumulatorMs / LockstepConfig.TICK_MS));
    }

    /// <summary>本地累加器固定步长推进（联机平时/单机/回放共用）。剩余时间留到下一渲染帧继续吃</summary>
    private void TickAccumulator(int maxTicks)
    {
        _accumulatorMs += Time.deltaTime * 1000.0;
        int ticks = 0;
        while (_accumulatorMs >= LockstepConfig.TICK_MS && ticks++ < maxTicks)
        {
            _accumulatorMs -= LockstepConfig.TICK_MS;
            LogicTick(++_logicFrame);
        }
    }

    /// <summary>补帧专用：已经封出去的帧再提交指令也没用，只推进模拟不回包</summary>
    private void TickFor(long frame, long latestServerFrame)
    {
        LogicTick(frame, submitLocal: frame + LockstepConfig.INPUT_DELAY_FRAMES > latestServerFrame);
    }

    /// <summary>一个逻辑帧：提交未来帧指令 → 交给网络桥 → 推进 → 消费事件 → 捕获插值状态</summary>
    private void LogicTick(long frame, bool submitLocal = true)
    {
        int rollbacksBefore = _session.RollbackCount;

        if (!_session.IsReplay)
        {
            // 1. 量化本地指令，生效帧 = 当前帧 + 输入延迟
            if (submitLocal)
                _session.SubmitLocal(frame + LockstepConfig.INPUT_DELAY_FRAMES, _inputSampler.BuildCommand());

            // 2. 待发指令交给网络桥（联机发包 / 单机本地封帧回喂）
            _session.FlushSends(frame);
        }

        // 3. 捕获推进前状态（插值起点 = 上一次的逻辑终点）
        foreach (var kv in _views) CaptureView(kv.Key, kv.Value, prev: true);

        // 4. 推进
        _session.Advance(frame);

        // 5. 从逻辑世界读回新状态 + 血量/换弹/生死表现。
        //    这一帧发生过回滚 → 位置可能被纠错改动，位移方向不能当真
        bool rollbacked = _session.RollbackCount != rollbacksBefore;
        foreach (var kv in _views)
        {
            CaptureView(kv.Key, kv.Value, prev: false, rollbacked);
            UpdateVitalsView(kv.Key, kv.Value);
        }
        UpdateScoreboard();

        // 6. 消费本帧事件（单调帧号闸门：回滚重演的旧帧不再消费）
        ConsumeEvents();
    }

    private void CaptureView(int seat, View view, bool prev, bool rollbacked = false)
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
            Vector3 dir = moved.normalized;

            // 回滚纠错不是移动：远端预测往前冲过头，权威位置把它拉回来，这一帧的位移是反方向的。
            // 拿它算朝向会让身体原地掉头（turn 片段），所以第一次反向不采样、插值直接贴到纠正后的位置；
            // 连续第二次反向说明是真的掉头/倒退，照常更新
            if (rollbacked && view.LastMoveDir.sqrMagnitude > 0f &&
                Vector3.Dot(dir, view.LastMoveDir) < 0f && ++view.ReversalFrames < 2)
            {
                view.PrevPos = view.CurPos;
                return;
            }

            view.ReversalFrames = 0;
            float deg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            view.TargetBodyYawDeg = Mathf.Round(deg / 45f) * 45f; // 八向吸附
            view.LastMoveDir = dir;
        }
        else
        {
            // 停稳后清掉移动方向：之后重新起步的第一帧不该和"上一次移动"比较，否则反向起步会被当成纠错
            view.ReversalFrames = 0;
            view.LastMoveDir = Vector3.zero;
        }
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
            // 弹匣余量直接读逻辑状态：回滚纠正过的值也在里面，和真实可开火次数一致
            if (lp.Ammo != view.LastAmmo)
            {
                view.LastAmmo = lp.Ammo;
                AmmoUI.Instance?.SetAmmo(lp.Ammo, LockstepConfig.MAGAZINE_SIZE);
            }

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

        if (view.IsLocal) view.Anim.SetFiring(_inputSampler != null && _inputSampler.FireHeld);
    }

    /// <summary>消费确定性事件：Fired=射手特效，Hit=受击反应，Died=死亡表现。
    /// 去重（帧/类型/射手）在会话里做完了，这里按入队顺序播即可——
    /// 回滚重演补出来的事件也在这条队列里，不会再被 Tick 清掉</summary>
    private void ConsumeEvents()
    {
        while (_session != null && _session.TryDequeueEvent(out var ev))
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
