using UnityEngine;

/// <summary>
/// 游戏初始化器（只在 Main/GameMenu 场景存在）
/// 🔑 职责：
/// 1. 初始化所有 Manager（PlayerManager、RoomManager、FrameSyncManager）
/// 2. 配置各 Manager 的依赖（预制体等）
/// 3. 订阅 EventBus 事件，协调各 Manager 的启动顺序
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

        // 延迟一帧确保所有组件就绪
        Invoke(nameof(LateInitialize), 0.1f);
    }

    private void OnDestroy()
    {
        // 取消所有 EventBus 订阅
        PlayerManager.GetInstance()?.UnsubscribeFromEventBus();
        RoomManager.GetInstance()?.UnsubscribeFromEventBus();
        FrameSyncManager.GetInstance()?.Dispose();

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

        // 4️⃣ 初始化 FrameSyncManager（通过 MonoManager 获得 Update 驱动）
        InitFrameSyncManager();

        // 5️⃣ 订阅 EventBus 事件（用于协调各 Manager）
        SubscribeToEventBus();

        _isInitialized = true;

        Debug.Log("[GameInitializer] ✅ 游戏组件初始化完成");
        Debug.Log("   📋 初始化顺序: NetworkHandler → PlayerManager → RoomManager → FrameSyncManager");
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

        // 初始化（订阅 EventBus，使用 MonoManager 运行协程）
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

    /// <summary>
    /// 初始化 FrameSyncManager
    /// </summary>
    private void InitFrameSyncManager()
    {
        var frameSyncManager = FrameSyncManager.GetInstance();
        if (frameSyncManager == null)
        {
            Debug.LogError("[GameInitializer] ❌ FrameSyncManager 创建失败！");
            return;
        }

        // 通过 MonoManager 自动获得 Update 驱动
        frameSyncManager.Initialize();
        Debug.Log("[GameInitializer] ✅ FrameSyncManager 已初始化");
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

        // 订阅游戏开始事件（用于启动帧同步）
        eventBus.Subscribe<GameStartedEventData>(
            GameEventType.OnGameStart,
            OnGameStarted
        );

        // 订阅游戏结束事件（用于停止帧同步）
        eventBus.Subscribe<GameEndedEventData>(
            GameEventType.OnGameOver,
            OnGameEnded
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

    #region EventBus 事件处理器（协调级别）

    /// <summary>
    /// 登录成功 → 打印日志，各 Manager 会自动处理
    /// </summary>
    private void OnLoginSuccess(LoginSuccessEventData eventData)
    {
        Debug.Log("[GameInitializer] 🎉 登录成功事件触发");
        Debug.Log($"   👤 UserId: {eventData.UserId}");
        Debug.Log($"   😊 Username: {eventData.Username}");
        Debug.Log("   📋 PlayerManager 会自动创建本地玩家");
    }

    /// <summary>
    /// 游戏开始 → 启动帧同步
    /// </summary>
    private void OnGameStarted(GameStartedEventData eventData)
    {
        Debug.Log("[GameInitializer] 🎮 游戏开始事件触发");
        Debug.Log("   📋 FrameSyncManager 会自动启动帧同步");
    }

    /// <summary>
    /// 游戏结束 → 停止帧同步
    /// </summary>
    private void OnGameEnded(GameEndedEventData eventData)
    {
        Debug.Log("[GameInitializer] 🏁 游戏结束事件触发");
        Debug.Log("   📋 FrameSyncManager 会自动停止帧同步");
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
        Debug.Log($"FrameSyncManager Ready: {FrameSyncManager.GetInstance() != null}");
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
