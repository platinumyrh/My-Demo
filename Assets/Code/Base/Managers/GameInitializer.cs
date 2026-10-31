using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 游戏初始化器（只在 Main/GameMenu 场景存在）
/// 🔑 职责：
/// 1. 初始化 NetworkHandler、PlayerManager、RoomManager
/// 2. 配置玩家表现层预制体
/// 3. 生成 PlayerView 后补启动 lockstep 会话
/// 
/// 设计理念：将"登录逻辑"和"游戏逻辑"完全解耦
/// - LoginScene: 只负责登录 UI + 跳转
/// - Main Scene: 本类负责初始化游戏世界，所有逻辑通过 EventBus 驱动
/// </summary>
public class GameInitializer : MonoBehaviour
{
    #region Inspector 配置

    [Header("玩家预制体")]
    [Tooltip("本地玩家预制体（用于实例化本地控制的角色）")]
    public GameObject localPlayerPrefab;

    [Tooltip("远程玩家预制体（用于实例化其他玩家的角色）")]
    public GameObject remotePlayerPrefab;

    [Header("生成位置")]
    [Tooltip("本地玩家出生点")]
    public Vector3 localPlayerSpawnPosition = Vector3.zero;

    [Tooltip("远程玩家生成半径（圆形分布）")]
    public float remotePlayerSpawnRadius = 5f;

    #endregion

    #region 私有字段

    private bool _isInitialized = false;

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        Debug.Log("[GameInitializer] 🎮 开始初始化游戏组件...");

        // Esc 菜单跟随场景出现；在这里挂可以在首帧渲染前把面板藏起来
        GameMenuUI.Ensure();

        // 延迟一帧确保所有组件就绪
        Invoke(nameof(LateInitialize), 0.1f);
    }

    private void OnDestroy()
    {
        // 取消所有 EventBus 订阅
        UnsubscribeFromEventBus();
        PlayerManager.GetInstance()?.UnsubscribeFromEventBus();
        RoomManager.GetInstance()?.UnsubscribeFromEventBus();

        _isInitialized = false;
        Debug.Log("[GameInitializer] 游戏初始化器已销毁");
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 延迟初始化（确保 NetworkManager 等单例已创建）
    /// </summary>
    private void LateInitialize()
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[GameInitializer] ⚠️ 已初始化，跳过重复初始化");
            return;
        }

        // 1️⃣ 初始化 NetworkHandler（注册消息处理器）
        InitNetworkHandler();

        // 2️⃣ 初始化 PlayerManager（配置预制体）
        InitPlayerManager();

        // 3️⃣ 初始化 RoomManager
        InitRoomManager();

        // 4️⃣ 订阅 EventBus 事件（用于协调各 Manager）
        SubscribeToEventBus();

        // 5️⃣ 如果已在房间，生成本地/远程 PlayerView
        //    断线重连时不走房间槽位：玩家由服务端座位表决定，交给 ReconnectSession 重建
        if (ReconnectSession.IsPending)
            Debug.Log("[GameInitializer] 🔌 重连中，玩家视图由 ReconnectSession 按座位表重建");
        else
            SetupPlayersForCurrentRoom();

        // 6️⃣ GameStart 可能早于 Main 场景加载：PlayerView 建好后补启动 lockstep
        LockstepSessionBootstrap.TryStartPendingSession();

        // 7️⃣ 回放入口：等管理器就绪后按录像重建玩家并起回放会话
        ReplayPlaybackSession.TryStartPending();

        // 8️⃣ 断线重连：Main 已就绪，建视图并请服务器发历史帧
        ReconnectSession.TryStartPending();

        _isInitialized = true;

        Debug.Log("[GameInitializer] ✅ 游戏组件初始化完成");
        Debug.Log("   📋 初始化顺序: NetworkHandler → PlayerManager → RoomManager → PlayerView → LockstepSessionBootstrap");
    }

    /// <summary>
    /// 按当前房间状态生成玩家
    /// 远程玩家在之前的场景里可能已经生成过（切场景时 GameObject 已销毁），
    /// 所以先清空再按 RoomManager 的成员列表重建
    ///
    /// 出生点规则（确定性）：以房间成员列表的顺序分配圆形槽位，
    /// 每个玩家在自己的客户端和别人的客户端都用同一个槽位——
    /// 本地玩家在自己的槽位出生，远程玩家在对方的槽位出生，两端镜像一致
    /// </summary>
    private void SetupPlayersForCurrentRoom()
    {
        var roomManager = RoomManager.GetInstance();
        var playerManager = PlayerManager.GetInstance();
        var networkManager = NetworkManager.Instance;

        if (roomManager == null || playerManager == null || networkManager == null) return;

        if (!roomManager.IsInRoom)
        {
            Debug.LogWarning("[GameInitializer] ⚠️ 当前不在房间中，跳过玩家生成");
            return;
        }

        // 清掉上一场景遗留的远程玩家引用，按房间成员重建
        playerManager.ClearAllRemotePlayers();

        string localId = networkManager.GetLocalPlayerId();
        string localName = networkManager.GetLocalPlayerName();
        if (string.IsNullOrEmpty(localName)) localName = localId;

        // 确定性槽位：成员列表顺序（服务端维护，各端一致）→ 圆形分布
        List<string> members = roomManager.RoomPlayerIds;
        int slotCount = Mathf.Max(members.Count, 1);

        Vector3 SlotOf(int index)
        {
            float angle = index * (360f / slotCount) * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Cos(angle) * remotePlayerSpawnRadius,
                7f, // 所有出生点抬高，避免生成时陷入地面
                Mathf.Sin(angle) * remotePlayerSpawnRadius
            );
        }

        // 本地玩家在自己的槽位出生
        int localIndex = members.IndexOf(localId);
        if (localIndex < 0) localIndex = 0;
        Vector3 localSlot = SlotOf(localIndex);
        playerManager.LocalSpawnPosition = localSlot;

        var localPlayer = playerManager.CreateLocalPlayer(localId, localName);
        if (localPlayer != null)
        {
            Debug.Log($"[GameInitializer] ✅ 本地玩家已生成: {localName} ({localId})，槽位{localIndex} {localSlot}");
        }

        // 远程玩家在各自的槽位出生
        foreach (string playerId in members)
        {
            if (playerId == localId) continue;

            int remoteIndex = members.IndexOf(playerId);
            playerManager.CreateRemotePlayer(playerId,
                playerManager.ResolveDisplayName(playerId, playerId),
                SlotOf(remoteIndex));
        }

        Debug.Log($"[GameInitializer] 📋 远程玩家已按房间成员重建，共 {members.Count - 1} 人");
    }

    #endregion

    #region Manager 初始化

    /// <summary>
    /// 初始化 NetworkHandler
    /// </summary>
    private void InitNetworkHandler()
    {
        var handler = NetworkHandler.Instance;
        if (handler == null)
        {
            Debug.LogError("[GameInitializer] ❌ NetworkHandler 创建失败！");
            return;
        }

        Debug.Log("[GameInitializer] ✅ NetworkHandler 已就绪");
    }

    /// <summary>
    /// 初始化 PlayerManager（配置预制体和生成位置）
    /// </summary>
    private void InitPlayerManager()
    {
        var playerManager = PlayerManager.GetInstance();
        if (playerManager == null)
        {
            Debug.LogError("[GameInitializer] ❌ PlayerManager 创建失败！");
            return;
        }

        // 配置预制体
        playerManager.LocalPlayerPrefab = localPlayerPrefab;
        playerManager.RemotePlayerPrefab = remotePlayerPrefab;
        playerManager.LocalSpawnPosition = localPlayerSpawnPosition;
        playerManager.RemoteSpawnRadius = remotePlayerSpawnRadius;

        // 初始化（订阅房间成员事件）
        playerManager.Initialize();

        Debug.Log("[GameInitializer] ✅ PlayerManager 已配置并初始化");
    }

    /// <summary>
    /// 初始化 RoomManager
    /// </summary>
    private void InitRoomManager()
    {
        var roomManager = RoomManager.GetInstance();
        if (roomManager == null)
        {
            Debug.LogError("[GameInitializer] ❌ RoomManager 创建失败！");
            return;
        }

        roomManager.Initialize();
        Debug.Log("[GameInitializer] ✅ RoomManager 已初始化");
    }

    #endregion

    #region EventBus 订阅管理

    /// <summary>
    /// 订阅协调事件（用于 GameInitializer 级别的逻辑）
    /// 各 Manager 自己订阅自己的事件，这里只订阅需要跨 Manager 协调的事件
    /// </summary>
    private void SubscribeToEventBus()
    {
        var eventBus = GameEventBus.GetInstance();

        // 订阅登录成功事件（用于打印日志、做全局初始化）
        eventBus.Subscribe<LoginSuccessEventData>(
            GameEventType.OnLoginSuccess,
            OnLoginSuccess
        );


        Debug.Log("[GameInitializer] 📡 已订阅 EventBus 协调事件");
    }

    /// <summary>
    /// 取消 EventBus 订阅
    /// </summary>
    private void UnsubscribeFromEventBus()
    {
        var eventBus = GameEventBus.GetInstance();

        eventBus.Unsubscribe<LoginSuccessEventData>(
            GameEventType.OnLoginSuccess,
            OnLoginSuccess
        );

    }

    #endregion

    #region EventBus 事件处理器（协调级别）

    /// <summary>
    /// 登录成功 → 打印日志，各 Manager 会自动处理
    /// </summary>
    private void OnLoginSuccess(LoginSuccessEventData eventData)
    {
        Debug.Log("[GameInitializer] 🎉 登录成功事件触发");
        Debug.Log($"   👤 UserId: {eventData.UserId}");
        Debug.Log($"   😊 Username: {eventData.Username}");
        Debug.Log("   📋 进入 Main 场景后由 GameInitializer 创建 PlayerView");
    }

    #endregion

    #region 辅助方法（调试用）

    /// <summary>
    /// 打印当前初始化状态（可在 Inspector 右键菜单调用）
    /// </summary>
    [ContextMenu("GameInitializer/Print Status")]
    public void PrintStatus()
    {
        Debug.Log("========== GameInitializer Status ==========");
        Debug.Log($"Initialized: {_isInitialized}");
        Debug.Log($"Has LocalPlayer Prefab: {localPlayerPrefab != null}");
        Debug.Log($"Has RemotePlayer Prefab: {remotePlayerPrefab != null}");
        Debug.Log($"Is Logged In: {(NetworkManager.Instance?.IsLoggedIn ?? false)}");
        Debug.Log($"Local Player ID: {NetworkManager.Instance?.GetLocalPlayerId() ?? "N/A"}");
        Debug.Log($"PlayerManager Ready: {PlayerManager.GetInstance() != null}");
        Debug.Log($"RoomManager Ready: {RoomManager.GetInstance() != null}");
        Debug.Log($"LockstepDriver Ready: {LockstepGameDriver.Instance != null}");
        Debug.Log("=============================================");
    }

    /// <summary>
    /// 强制重新初始化所有 Manager（调试用）
    /// </summary>
    [ContextMenu("GameInitializer/Force Reinitialize")]
    public void ForceReinitialize()
    {
        _isInitialized = false;
        LateInitialize();
    }

    #endregion
}
