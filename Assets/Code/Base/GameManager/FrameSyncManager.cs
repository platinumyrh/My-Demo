using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 帧同步管理器
/// 负责处理帧同步逻辑：收集输入、发送输入、接收服务器帧、更新远程玩家
/// 通过 EventBus 订阅网络事件，驱动帧同步流程
/// 
/// 🔑 继承 BaseManager 实现单例模式
/// </summary>
public class FrameSyncManager : BaseManager<FrameSyncManager>
{
    #region 配置参数

    /// <summary>
    /// 服务器帧率（每秒帧数）
    /// </summary>
    public int ServerFrameRate { get; set; } = 20;

    /// <summary>
    /// 客户端预测：是否启用
    /// </summary>
    public bool EnableClientPrediction { get; set; } = true;

    /// <summary>
    /// 插值延迟（秒）
    /// </summary>
    public float InterpolationDelay { get; set; } = 0.1f;

    #endregion

    #region 属性

    /// <summary>
    /// 当前帧号
    /// </summary>
    public int CurrentFrameId { get; private set; }

    /// <summary>
    /// 是否正在帧同步中
    /// </summary>
    public bool IsSyncing { get; private set; }

    /// <summary>
    /// 服务器帧间隔（秒）
    /// </summary>
    public float ServerFrameInterval => 1f / ServerFrameRate;

    #endregion

    #region 私有字段

    // 帧同步定时器
    private float _frameTimer = 0f;

    // 本地玩家输入缓存
    private PlayerInput _currentInput;

    // 帧历史（用于回滚）
    private Dictionary<int, FrameData> _frameHistory = new Dictionary<int, FrameData>();

    // 是否已初始化
    private bool _isInitialized = false;

    // 使用 MonoManager 来驱动 Update

    #endregion

    #region 数据结构

    /// <summary>
    /// 玩家输入
    /// </summary>
    public struct PlayerInput
    {
        public float Horizontal;
        public float Vertical;
        public bool Jump;
        public bool Attack;

        public bool IsEmpty => Horizontal == 0 && Vertical == 0 && !Jump && !Attack;
    }

    /// <summary>
    /// 帧数据
    /// </summary>
    public class FrameData
    {
        public int FrameId;
        public Dictionary<string, PlayerInput> PlayerInputs = new Dictionary<string, PlayerInput>();
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        SubscribeToEventBus();

        // 注册到 MonoManager 的 Update 事件
        MonoManager.GetInstance().AddUpdateListener(OnUpdate);

        _isInitialized = true;
        Debug.Log("[FrameSyncManager] ✅ 初始化完成");
    }

    /// <summary>
    /// 清理（取消订阅）
    /// </summary>
    public void Dispose()
    {
        MonoManager.GetInstance().RemoveUpdateListener(OnUpdate);
        UnsubscribeFromEventBus();
    }

    /// <summary>
    /// 每帧更新（由 MonoManager 驱动）
    /// </summary>
    private void OnUpdate()
    {
        if (!IsSyncing) return;

        // 收集本地输入
        CollectLocalInput();

        // 发送输入到服务器
        SendInputToServer();

        // 帧同步逻辑（简化：在 Update 中执行，如需 FixedUpdate 节奏可自行实现定时器）
        ProcessFrameSync();
    }

    /// <summary>
    /// 订阅 EventBus 事件
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

        // 帧同步更新
        eventBus.Subscribe<FrameSyncUpdateEventData>(
            GameEventType.OnFrameSyncUpdate,
            OnFrameSyncUpdate
        );

        // 其他玩家输入
        eventBus.Subscribe<PlayerInputEventData>(
            GameEventType.OnPlayerInputReceived,
            OnPlayerInputReceived
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

        eventBus.Unsubscribe<FrameSyncUpdateEventData>(
            GameEventType.OnFrameSyncUpdate,
            OnFrameSyncUpdate
        );

        eventBus.Unsubscribe<PlayerInputEventData>(
            GameEventType.OnPlayerInputReceived,
            OnPlayerInputReceived
        );
    }

    #endregion

    // Update 循环已由 MonoManager 驱动（OnUpdate 方法）

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

    /// <summary>
    /// 收到服务器帧同步数据
    /// </summary>
    private void OnFrameSyncUpdate(FrameSyncUpdateEventData eventData)
    {
        // 更新当前帧号
        CurrentFrameId = eventData.FrameId;

        // 更新远程玩家位置
        ApplyServerFrame(eventData);
    }

    /// <summary>
    /// 收到其他玩家输入
    /// </summary>
    private void OnPlayerInputReceived(PlayerInputEventData eventData)
    {
        // 存储其他玩家的输入，用于预测和插值
        // 实际处理在 ApplyServerFrame 中
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
        _frameHistory.Clear();

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
    /// 收集本地玩家输入
    /// </summary>
    private void CollectLocalInput()
    {
        _currentInput = new PlayerInput
        {
            Horizontal = UnityEngine.Input.GetAxis("Horizontal"),
            Vertical = UnityEngine.Input.GetAxis("Vertical"),
            Jump = UnityEngine.Input.GetButtonDown("Jump"),
            Attack = UnityEngine.Input.GetButtonDown("Fire1")
        };
    }

    /// <summary>
    /// 发送输入到服务器
    /// </summary>
    private void SendInputToServer()
    {
        // 如果没有输入，不发送（节省带宽）
        if (_currentInput.IsEmpty) return;

        // 通过 NetworkManager 发送输入
        // 这里可以优化为批量发送或固定频率发送
        var networkManager = NetworkManager.Instance;
        if (networkManager != null && networkManager.IsConnected)
        {
            // 构建输入消息（使用 Protobuf 生成的 PlayerInput 类）
            // 注意：PlayerInput 没有 FrameId 字段，帧号由服务器分配
            var inputMsg = new GameNetwork.PlayerInput
            {
                MoveX = _currentInput.Horizontal,
                MoveY = _currentInput.Vertical,
                Fire = _currentInput.Attack,
                Jump = _currentInput.Jump,
            };

            networkManager.SendPlayerInput(inputMsg);
        }
    }

    /// <summary>
    /// 处理帧同步（FixedUpdate）
    /// </summary>
    private void ProcessFrameSync()
    {
        if (!EnableClientPrediction) return;

        // 客户端预测：根据当前输入预测下一帧位置
        var localPlayer = PlayerManager.GetInstance()?.GetLocalPlayer();
        if (localPlayer != null)
        {
            // 使用本地输入进行预测移动
            localPlayer.PredictMovement(_currentInput.Horizontal, _currentInput.Vertical);
        }
    }

    /// <summary>
    /// 应用服务器帧数据
    /// </summary>
    private void ApplyServerFrame(FrameSyncUpdateEventData frameData)
    {
        // 更新远程玩家位置
        foreach (var kvp in frameData.PlayerPositions)
        {
            string playerId = kvp.Key;
            Vector3 position = kvp.Value;

            // 跳过本地玩家（由客户端预测处理）
            var localPlayer = PlayerManager.GetInstance()?.GetLocalPlayer();
            if (localPlayer != null && localPlayer.PlayerId == playerId)
            {
                // 本地玩家：服务器回包校验
                localPlayer.ServerReconciliation(position);
                continue;
            }

            // 远程玩家：直接更新位置（带插值）
            var remotePlayer = PlayerManager.GetInstance()?.GetRemotePlayer(playerId);
            if (remotePlayer != null)
            {
                remotePlayer.SetTargetPosition(position);
            }
        }
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 获取当前输入
    /// </summary>
    public PlayerInput GetCurrentInput()
    {
        return _currentInput;
    }

    /// <summary>
    /// 获取指定帧的数据
    /// </summary>
    public FrameData GetFrameData(int frameId)
    {
        _frameHistory.TryGetValue(frameId, out var frameData);
        return frameData;
    }

    /// <summary>
    /// 清除帧历史
    /// </summary>
    public void ClearFrameHistory()
    {
        _frameHistory.Clear();
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
        Debug.Log($"ClientPrediction: {EnableClientPrediction}");
        Debug.Log($"FrameHistory: {_frameHistory.Count} frames");
        Debug.Log("=============================================");
    }

    #endregion
}
