using UnityEngine;
using System.Collections.Generic;
using GameNetwork;

/// <summary>
/// 帧同步管理器（客户端侧）
/// 职责：
/// 1. 监听游戏开始/结束事件，控制同步状态
/// 2. 接收服务器帧广播（由 NetworkHandler 解析后直接调用 OnServerFrame），分发给远程玩家
///
/// 注意：本地输入的收集和发送由 LocalPlayerMovement（挂玩家预制体）负责，
/// 本类不再重复收集输入/驱动本地移动，避免双重移动和重复发包
/// </summary>
public class FrameSyncManager : BaseManager<FrameSyncManager>
{
    #region 配置参数

    /// <summary>
    /// 服务器帧率（每秒帧数），需与服务端 FrameSyncManager 的帧率一致
    /// </summary>
    public int ServerFrameRate { get; set; } = 30;

    /// <summary>
    /// 是否正在帧同步中
    /// </summary>
    public bool IsSyncing { get; private set; }

    /// <summary>
    /// 服务器帧间隔（秒）
    /// </summary>
    public float ServerFrameInterval => 1f / ServerFrameRate;

    #endregion

    #region 属性

    /// <summary>
    /// 当前帧号（来自最近收到的服务器帧）
    /// </summary>
    public int CurrentFrameId { get; private set; }

    #endregion

    #region 私有字段

    // 是否已初始化
    private bool _isInitialized = false;

    // 当前是否已订阅 EventBus / 注册 Update（退订后允许重新订阅）
    private bool _subscribed = false;

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化
    /// </summary>
    public void Initialize()
    {
        if (_subscribed) return;

        SubscribeToEventBus();

        _isInitialized = true;
        _subscribed = true;
        Debug.Log("[FrameSyncManager] ✅ 初始化完成");

        // 状态追赶：OnGameStart 事件可能在本组件订阅之前就发过了
        //（典型场景：Room 场景收到 S2C_GameStart 后跳转 Main，本组件在 Main 才初始化）。
        // RoomManager.CurrentRoom.IsInGame 是跨场景保留的持久状态，以此补齐同步状态
        var room = RoomManager.GetInstance()?.CurrentRoom;
        if (room != null && room.IsInGame)
        {
            Debug.Log("[FrameSyncManager] ⏩ 检测到游戏已开始（事件早于本组件初始化），直接进入同步状态");
            StartFrameSync();
        }
    }

    /// <summary>
    /// 清理（取消订阅）
    /// </summary>
    public void Dispose()
    {
        if (!_subscribed) return;

        UnsubscribeFromEventBus();
        _subscribed = false;
    }

    /// <summary>
    /// 订阅 EventBus 事件
    /// 注意：帧同步数据（S2C_FrameState）不走 EventBus，
    /// 由 NetworkHandler 解析后直接调用 OnServerFrame
    /// </summary>
    private void SubscribeToEventBus()
    {
        var eventBus = GameEventBus.GetInstance();

        // 游戏开始 → 开始帧同步
        eventBus.Subscribe<GameStartedEventData>(
            GameEventType.OnGameStart,
            OnGameStarted
        );

        // 游戏结束 → 停止帧同步
        eventBus.Subscribe<GameEndedEventData>(
            GameEventType.OnGameOver,
            OnGameEnded
        );

        Debug.Log("[FrameSyncManager] 📡 已订阅 EventBus 事件");
    }

    /// <summary>
    /// 取消 EventBus 订阅
    /// </summary>
    public void UnsubscribeFromEventBus()
    {
        var eventBus = GameEventBus.GetInstance();

        eventBus.Unsubscribe<GameStartedEventData>(
            GameEventType.OnGameStart,
            OnGameStarted
        );

        eventBus.Unsubscribe<GameEndedEventData>(
            GameEventType.OnGameOver,
            OnGameEnded
        );
    }

    #endregion

    #region EventBus 事件处理器

    /// <summary>
    /// 游戏开始 → 启动帧同步
    /// </summary>
    private void OnGameStarted(GameStartedEventData eventData)
    {
        Debug.Log("[FrameSyncManager] 🎮 游戏开始，启动帧同步");
        StartFrameSync();
    }

    /// <summary>
    /// 游戏结束 → 停止帧同步
    /// </summary>
    private void OnGameEnded(GameEndedEventData eventData)
    {
        Debug.Log("[FrameSyncManager] 🏁 游戏结束，停止帧同步");
        StopFrameSync();
    }

    #endregion

    #region 帧同步核心逻辑

    /// <summary>
    /// 开始帧同步
    /// </summary>
    public void StartFrameSync()
    {
        IsSyncing = true;
        CurrentFrameId = 0;

        Debug.Log("[FrameSyncManager] ▶️ 帧同步已启动");
    }

    /// <summary>
    /// 停止帧同步
    /// </summary>
    public void StopFrameSync()
    {
        IsSyncing = false;
        CurrentFrameId = 0;

        Debug.Log("[FrameSyncManager] ⏹️ 帧同步已停止");
    }

    /// <summary>
    /// 收到服务器帧状态（由 NetworkHandler 解析 S2C_FrameState 后直接调用，高频路径不过 EventBus）
    /// 职责：更新本地帧号 + 把各玩家的权威位置分发给对应的远程玩家
    /// </summary>
    public void OnServerFrame(GameNetwork.FrameStateResp frameState)
    {
        CurrentFrameId = (int)frameState.FrameId;

        string localPlayerId = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetLocalPlayerId()
            : "";

        // ① 离散输入（开火 / 瞄准…）：位置里看不出来，必须单独分发，
        //    否则远端玩家永远不会播开火动画
        foreach (var kvp in frameState.PlayerInputs)
        {
            if (kvp.Key == localPlayerId) continue;

            var remote = PlayerManager.GetInstance()?.GetRemotePlayer(kvp.Key);
            remote?.OnServerInput(kvp.Value);
        }

        // ② 权威位置 + 朝向
        foreach (var kvp in frameState.PlayerStates)
        {
            // 本地玩家：位置由本地物理权威，忽略服务器回传的自己
            if (kvp.Key == localPlayerId) continue;

            // 远程玩家：向服务器持有的权威位置做插值/纠正
            var remote = PlayerManager.GetInstance()?.GetRemotePlayer(kvp.Key);
            if (remote == null)
            {
                // 诊断：帧内出现本地没有的玩家ID，说明服务端玩家标识和本地房间成员不一致
                if (frameState.FrameId % 30 == 0)
                {
                    Debug.LogWarning($"[FrameSyncManager] ⚠️ 帧#{frameState.FrameId} 内出现未知玩家 [{kvp.Key}]，" +
                                     $"本地ID=[{localPlayerId}]，状态被丢弃");
                }
                continue;
            }

            remote.OnServerState(kvp.Value);
        }

        // ③ 本地玩家生命值：hp/is_dead 是服务端结算结果，变化时发布事件供 UI/死亡表现消费
        if (frameState.PlayerStates.TryGetValue(localPlayerId, out var localState))
        {
            PublishLocalVitalsIfChanged(localState.Hp, localState.IsDead);
        }

        // 调试日志（每100帧输出一次）
        if (frameState.FrameId % 100 == 0)
        {
            //Debug.Log($"[FrameSyncManager] 🎮 帧 #{frameState.FrameId}, 位置状态: {frameState.PlayerStates.Count}");
        }
    }

    private int _lastLocalHp = -1;
    private bool _lastLocalDead;

    /// <summary>本地生命值变化时发布事件（快照每帧都带，仅变化时发布）</summary>
    private void PublishLocalVitalsIfChanged(int hp, bool isDead)
    {
        if (hp == _lastLocalHp && isDead == _lastLocalDead) return;

        _lastLocalHp = hp;
        _lastLocalDead = isDead;

        Debug.Log($"[FrameSyncManager] 💚 本地生命值变化: hp={hp}, dead={isDead}");
        GameEventBus.GetInstance().Publish(
            GameEventType.OnLocalVitalsChanged,
            LocalVitalsEventData.Create(hp, isDead)
        );
    }

    #endregion

    #region 调试方法

    /// <summary>
    /// 打印当前状态
    /// </summary>
    public void PrintStatus()
    {
        Debug.Log("========== FrameSyncManager Status ==========");
        Debug.Log($"IsSyncing: {IsSyncing}");
        Debug.Log($"CurrentFrameId: {CurrentFrameId}");
        Debug.Log($"ServerFrameRate: {ServerFrameRate}");
        Debug.Log($"FrameInterval: {ServerFrameInterval:F4}s");
        Debug.Log("=============================================");
    }

    #endregion
}
