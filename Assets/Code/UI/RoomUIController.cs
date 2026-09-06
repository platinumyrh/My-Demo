using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 房间界面控制器
/// 职责：
/// 1. 显示当前房间信息和房间内玩家列表（数据来自 RoomManager.CurrentRoom）
/// 2. 房主点击"开始游戏"→ RoomManager.StartGame()
/// 3. 收到 OnGameStart → 跳转 Main 场景
/// 4. 离开房间 → 回大厅
///
/// 注意：RoomManager 是纯 C# 单例，状态跨场景保留，
/// 本界面只做展示和转发，不维护任何业务状态
/// </summary>
public class RoomUIController : MonoBehaviour
{
    #region UI组件引用（在Inspector中拖拽赋值）

    [Header("房间信息")]
    [Tooltip("房间信息文本（房间ID/名称等）")]
    public Text Text_RoomInfo;

    [Header("玩家列表")]
    [Tooltip("玩家条目的父物体")]
    public Transform PlayerListContent;

    [Tooltip("玩家条目预制体（带一个Text子对象）")]
    public GameObject PlayerItemPrefab;

    [Header("按钮")]
    [Tooltip("开始游戏按钮（非房主自动禁用）")]
    public Button Btn_StartGame;

    [Tooltip("离开房间按钮")]
    public Button Btn_LeaveRoom;

    [Header("提示信息")]
    [Tooltip("提示文本")]
    public Text Text_Message;

    [Header("配置")]
    [Tooltip("游戏开始后跳转的场景名")]
    public string gameSceneName = "Main";

    [Tooltip("离开房间后返回的场景名")]
    public string lobbySceneName = "Lobby";

    #endregion

    #region 私有字段

    private List<GameObject> _playerItems = new List<GameObject>();
    private bool _isStartingGame = false;

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        // 绑定按钮
        if (Btn_StartGame != null)
        {
            Btn_StartGame.onClick.AddListener(OnStartGameClick);
        }

        if (Btn_LeaveRoom != null)
        {
            Btn_LeaveRoom.onClick.AddListener(OnLeaveRoomClick);
        }

        // 订阅 EventBus 事件
        var eventBus = GameEventBus.GetInstance();
        eventBus.Subscribe<RoomStateChangedEventData>(
            GameEventType.OnRoomStateChanged,
            HandleRoomStateChanged
        );
        eventBus.Subscribe<GameStartedEventData>(
            GameEventType.OnGameStart,
            HandleGameStarted
        );
        eventBus.Subscribe<LeftRoomEventData>(
            GameEventType.OnLeftRoom,
            HandleLeftRoom
        );
        // 兜底：加入/创建事件若在进场景后才到达，也能刷新显示
        eventBus.Subscribe<JoinedRoomEventData>(
            GameEventType.OnJoinedRoom,
            HandleJoinedRoomLate
        );
        eventBus.Subscribe<RoomCreatedEventData>(
            GameEventType.OnRoomCreated,
            HandleRoomCreatedLate
        );

        // 进入场景时按当前房间状态渲染一次
        // （加入房间成功发生在 Lobby 场景，OnJoinedRoom 在那里已经消费完了）
        RefreshRoomDisplay();

        Debug.Log("[RoomUI] 房间界面初始化完成");
    }

    private void OnDestroy()
    {
        if (Btn_StartGame != null)
        {
            Btn_StartGame.onClick.RemoveListener(OnStartGameClick);
        }

        if (Btn_LeaveRoom != null)
        {
            Btn_LeaveRoom.onClick.RemoveListener(OnLeaveRoomClick);
        }

        var eventBus = GameEventBus.GetInstance();
        eventBus.Unsubscribe<RoomStateChangedEventData>(
            GameEventType.OnRoomStateChanged,
            HandleRoomStateChanged
        );
        eventBus.Unsubscribe<GameStartedEventData>(
            GameEventType.OnGameStart,
            HandleGameStarted
        );
        eventBus.Unsubscribe<LeftRoomEventData>(
            GameEventType.OnLeftRoom,
            HandleLeftRoom
        );
        eventBus.Unsubscribe<JoinedRoomEventData>(
            GameEventType.OnJoinedRoom,
            HandleJoinedRoomLate
        );
        eventBus.Unsubscribe<RoomCreatedEventData>(
            GameEventType.OnRoomCreated,
            HandleRoomCreatedLate
        );
    }

    #endregion

    #region 按钮点击事件

    /// <summary>
    /// 点击"开始游戏"（服务端会校验房主身份，这里提前禁用非房主按钮）
    /// </summary>
    private void OnStartGameClick()
    {
        if (_isStartingGame)
        {
            return;
        }

        var roomManager = RoomManager.GetInstance();
        string localId = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetLocalPlayerId()
            : "";

        if (roomManager.CurrentRoom == null)
        {
            ShowMessage("⚠️ 当前不在任何房间中", Color.yellow);
            return;
        }

        if (!roomManager.IsHost(localId))
        {
            ShowMessage("⚠️ 只有房主才能开始游戏", Color.yellow);
            return;
        }

        _isStartingGame = true;
        ShowMessage("正在开始游戏...", Color.cyan);
        roomManager.StartGame();

        // 5秒无响应则恢复按钮（服务器拒绝等情况）
        Invoke(nameof(ResetStartState), 5f);
    }

    /// <summary>
    /// 点击"离开房间"→ 回大厅
    /// </summary>
    private void OnLeaveRoomClick()
    {
        ShowMessage("正在离开房间...", Color.cyan);
        RoomManager.GetInstance().LeaveRoom();
    }

    #endregion

    #region EventBus 事件处理

    /// <summary>
    /// 房间状态变更（玩家加入/离开时由 RoomManager 发布）→ 刷新显示
    /// </summary>
    private void HandleRoomStateChanged(RoomStateChangedEventData eventData)
    {
        RefreshRoomDisplay();
    }

    /// <summary>
    /// 游戏开始 → 跳转 Main 场景（玩家生成由 Main 场景的 GameInitializer 负责）
    /// </summary>
    private void HandleGameStarted(GameStartedEventData eventData)
    {
        _isStartingGame = false;
        CancelInvoke(nameof(ResetStartState));

        Debug.Log($"[RoomUI] 🎮 游戏开始，跳转场景: {gameSceneName}");
        ShowMessage("游戏开始！", Color.green);

        if (!string.IsNullOrEmpty(gameSceneName))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameSceneName);
        }
    }

    /// <summary>
    /// 已离开房间 → 回大厅
    /// </summary>
    private void HandleLeftRoom(LeftRoomEventData eventData)
    {
        Debug.Log("[RoomUI] 已离开房间，返回大厅");

        if (!string.IsNullOrEmpty(lobbySceneName))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(lobbySceneName);
        }
    }

    /// <summary>
    /// 兜底：进场景后才收到加入成功事件 → 刷新显示
    /// </summary>
    private void HandleJoinedRoomLate(JoinedRoomEventData eventData)
    {
        Debug.Log("[RoomUI] 收到（晚到的）加入房间事件，刷新显示");
        RefreshRoomDisplay();
    }

    /// <summary>
    /// 兜底：进场景后才收到创建成功事件 → 刷新显示
    /// </summary>
    private void HandleRoomCreatedLate(RoomCreatedEventData eventData)
    {
        Debug.Log("[RoomUI] 收到（晚到的）创建房间事件，刷新显示");
        RefreshRoomDisplay();
    }

    #endregion

    #region 私有方法 - 界面刷新

    /// <summary>
    /// 按 RoomManager 的当前房间状态刷新整个界面
    /// </summary>
    private void RefreshRoomDisplay()
    {
        var room = RoomManager.GetInstance().CurrentRoom;

        if (room == null)
        {
            ShowMessage("⚠️ 未加入任何房间（正常流程应从大厅加入房间进入本场景）", Color.yellow);
            SetStartButtonInteractable(false);
            return;
        }

        // 房间信息
        if (Text_RoomInfo != null)
        {
            Text_RoomInfo.text = $"{room.RoomName}  ({room.CurrentPlayerCount}/{room.MaxPlayers})";
        }

        // 玩家列表
        RebuildPlayerList(room);

        // 只有房主能点开始
        string localId = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetLocalPlayerId()
            : "";
        SetStartButtonInteractable(RoomManager.GetInstance().IsHost(localId));
    }

    /// <summary>
    /// 重建玩家列表
    /// </summary>
    private void RebuildPlayerList(RoomManager.RoomInfo room)
    {
        if (PlayerListContent == null) return;

        ClearPlayerList();

        string localId = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetLocalPlayerId()
            : "";

        foreach (string playerId in room.PlayerIds)
        {
            if (PlayerItemPrefab == null) break;

            GameObject item = Instantiate(PlayerItemPrefab, PlayerListContent);
            _playerItems.Add(item);

            var text = item.GetComponentInChildren<Text>();
            if (text != null)
            {
                string suffix = "";
                if (playerId == room.HostPlayerId) suffix = "  (房主)";
                else if (playerId == localId) suffix = "  (我)";

                text.text = playerId + suffix;
            }
        }
    }

    /// <summary>
    /// 清空玩家列表
    /// </summary>
    private void ClearPlayerList()
    {
        foreach (var item in _playerItems)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }
        _playerItems.Clear();
    }

    private void SetStartButtonInteractable(bool interactable)
    {
        if (Btn_StartGame != null)
        {
            Btn_StartGame.interactable = interactable;
        }
    }

    private void ResetStartState()
    {
        _isStartingGame = false;
        ShowMessage("开始游戏无响应，请重试", Color.yellow);
    }

    private void ShowMessage(string message, Color color)
    {
        if (Text_Message != null)
        {
            Text_Message.text = message;
            Text_Message.color = color;
        }
        Debug.Log($"[RoomUI] {message}");
    }

    #endregion
}
