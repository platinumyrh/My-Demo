using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using GameNetwork;

/// <summary>
/// 大厅界面控制器
/// 职责：处理大厅UI交互（创建房间、显示房间列表）
/// </summary>
public class LobbyUIController : MonoBehaviour
{
    #region UI组件引用（在Inspector中拖拽赋值）

    [Header("按钮")]
    [Tooltip("创建房间按钮")]
    public Button Btn_CreateRoom;

    [Tooltip("加入房间按钮")]
    public Button Btn_JoinRoom;

    [Header("房间列表面板")]
    [Tooltip("房间列表面板（场景中直接放置，默认隐藏）")]
    public GameObject Panel_RoomList;

    [Tooltip("关闭房间列表按钮")]
    public Button Btn_CloseRoomList;

    [Tooltip("房间列表内容容器（Panel_Rooms，房间条目的父物体）")]
    public Transform RoomListContent;

    [Tooltip("房间条目预制体（Panel_InfoColumn）")]
    public GameObject RoomItemPrefab;

    [Header("提示信息")]
    [Tooltip("提示文本")]
    public Text Text_Message;

    [Header("配置")]
    [Tooltip("加入房间成功后跳转的场景名")]
    public string roomSceneName = "Room";

    #endregion

    #region 私有字段

    private List<GameObject> _roomItemList = new List<GameObject>();
    private bool _isProcessing = false;

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        // 🔑 确保房间管理器在大厅就完成 EventBus 订阅。
        // RoomManager.Initialize() 原本只在 Main 场景的 GameInitializer 里调用，
        // 导致大厅里"加入房间成功"事件发出时 RoomManager 还没订阅，
        // CurrentRoom 不会被赋值，Room 场景的 UI 读不到房间数据。
        // RoomManager 是纯 C# 单例，Initialize 有 _isInitialized 防重入，
        // 这里提前初始化不影响 Main 场景的 GameInitializer 流程。
        RoomManager.GetInstance().Initialize();

        // 绑定按钮事件
        if (Btn_CreateRoom != null)
        {
            Btn_CreateRoom.onClick.AddListener(OnCreateRoomClick);
        }

        if (Btn_JoinRoom != null)
        {
            Btn_JoinRoom.onClick.AddListener(OnJoinRoomClick);
        }

        // 绑定关闭房间列表按钮
        if (Btn_CloseRoomList != null)
        {
            Btn_CloseRoomList.onClick.AddListener(OnCloseRoomListClick);
        }

        // 隐藏房间列表面板（初始状态）
        if (Panel_RoomList != null)
        {
            Panel_RoomList.SetActive(false);
        }

        // 订阅EventBus事件
        var eventBus = GameEventBus.GetInstance();
        eventBus.Subscribe<RoomListUpdatedEventData>(
            GameEventType.OnRoomListUpdated,
            HandleRoomListUpdated
        );
        eventBus.Subscribe<RoomCreatedEventData>(
            GameEventType.OnRoomCreated,
            HandleRoomCreated
        );
        eventBus.Subscribe<RoomCreateFailedEventData>(
            GameEventType.OnRoomCreateFailed,
            HandleRoomCreateFailed
        );
        eventBus.Subscribe<JoinRoomFailedEventData>(
            GameEventType.OnJoinRoomFailed,
            HandleJoinRoomFailed
        );
        eventBus.Subscribe<JoinedRoomEventData>(
            GameEventType.OnJoinedRoom,
            HandleJoinedRoom
        );

        ShowMessage("欢迎来到大厅", Color.white);
        Debug.Log("[LobbyUI] 大厅界面初始化完成");
    }

    private void OnDestroy()
    {
        // 取消按钮事件
        if (Btn_CreateRoom != null)
        {
            Btn_CreateRoom.onClick.RemoveListener(OnCreateRoomClick);
        }

        if (Btn_JoinRoom != null)
        {
            Btn_JoinRoom.onClick.RemoveListener(OnJoinRoomClick);
        }

        if (Btn_CloseRoomList != null)
        {
            Btn_CloseRoomList.onClick.RemoveListener(OnCloseRoomListClick);
        }

        // 取消EventBus订阅
        var eventBus = GameEventBus.GetInstance();
        eventBus.Unsubscribe<RoomListUpdatedEventData>(
            GameEventType.OnRoomListUpdated,
            HandleRoomListUpdated
        );
        eventBus.Unsubscribe<RoomCreatedEventData>(
            GameEventType.OnRoomCreated,
            HandleRoomCreated
        );
        eventBus.Unsubscribe<RoomCreateFailedEventData>(
            GameEventType.OnRoomCreateFailed,
            HandleRoomCreateFailed
        );
        eventBus.Unsubscribe<JoinRoomFailedEventData>(
            GameEventType.OnJoinRoomFailed,
            HandleJoinRoomFailed
        );
        eventBus.Unsubscribe<JoinedRoomEventData>(
            GameEventType.OnJoinedRoom,
            HandleJoinedRoom
        );
    }

    #endregion

    #region 按钮点击事件

    /// <summary>
    /// 点击"创建房间"按钮
    /// </summary>
    private void OnCreateRoomClick()
    {
        if (_isProcessing)
        {
            ShowMessage("正在处理中...", Color.yellow);
            return;
        }

        _isProcessing = true;
        ShowMessage("正在创建房间...", Color.cyan);

        // 通过 RoomManager 发送创建房间请求（默认房间名，无密码）
        RoomManager.GetInstance().CreateRoom(
            roomName: $"Room_{NetworkManager.Instance.GetLocalPlayerId()}",
            password: ""
        );

        // 2秒后重置处理状态（如果服务器没响应）
        Invoke(nameof(ResetProcessing), 3f);
    }

    /// <summary>
    /// 点击"加入房间"按钮
    /// </summary>
    private void OnJoinRoomClick()
    {
        if (_isProcessing)
        {
            ShowMessage("正在处理中...", Color.yellow);
            return;
        }

        // 显示房间列表面板（居中显示）
        if (Panel_RoomList != null)
        {
            Panel_RoomList.SetActive(true);
            
            // 设置面板居中
            RectTransform rectTransform = Panel_RoomList.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        // 请求房间列表
        ShowMessage("正在获取房间列表...", Color.cyan);
        RoomManager.GetInstance().RequestRoomList();
    }

    /// <summary>
    /// 点击"关闭房间列表"按钮
    /// </summary>
    private void OnCloseRoomListClick()
    {
        if (Panel_RoomList != null)
        {
            Panel_RoomList.SetActive(false);
        }
        
        // 清空房间列表（可选，下次打开时重新获取）
        ClearRoomList();
        
        ShowMessage("欢迎来到大厅", Color.white);
        Debug.Log("[LobbyUI] 关闭房间列表");
    }

    #endregion

    #region EventBus 事件处理

    /// <summary>
    /// 收到房间列表更新
    /// </summary>
    private void HandleRoomListUpdated(RoomListUpdatedEventData eventData)
    {
        _isProcessing = false;

        Debug.Log($"[LobbyUI] 收到房间列表: {eventData.Rooms.Count} 个房间");

        // 清空旧列表
        ClearRoomList();

        if (eventData.Rooms.Count == 0)
        {
            ShowMessage("暂无房间，点击创建房间创建一个吧！", Color.white);
            return;
        }

        // 生成房间条目
        foreach (var room in eventData.Rooms)
        {
            CreateRoomItem(room);
        }

        ShowMessage($"共 {eventData.Rooms.Count} 个房间", Color.green);
    }

    /// <summary>
    /// 房间创建成功 → 跳转房间等待界面（和加入房间走同一个 Room 场景）
    /// </summary>
    private void HandleRoomCreated(RoomCreatedEventData eventData)
    {
        _isProcessing = false;
        CancelInvoke(nameof(ResetProcessing));

        Debug.Log($"[LobbyUI] 房间创建成功: {eventData.RoomId}，跳转房间界面");
        ShowMessage($"房间创建成功！ID: {eventData.RoomId}", Color.green);

        if (!string.IsNullOrEmpty(roomSceneName))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(roomSceneName);
        }
    }

    /// <summary>
    /// 房间创建失败
    /// </summary>
    private void HandleRoomCreateFailed(RoomCreateFailedEventData eventData)
    {
        _isProcessing = false;
        CancelInvoke(nameof(ResetProcessing));

        ShowMessage($"❌ 创建房间失败: {eventData.Reason}", Color.red);
    }

    /// <summary>
    /// 加入房间失败
    /// </summary>
    private void HandleJoinRoomFailed(JoinRoomFailedEventData eventData)
    {
        ShowMessage($"❌ 加入房间失败: {eventData.Reason}", Color.red);
    }

    /// <summary>
    /// 加入房间成功 → 跳转房间等待界面
    /// （房间状态由 RoomManager 维护，RoomUIController 进场景后会自己读 CurrentRoom 渲染）
    /// </summary>
    private void HandleJoinedRoom(JoinedRoomEventData eventData)
    {
        Debug.Log($"[LobbyUI] 加入房间成功: {eventData.RoomId}，跳转房间界面");

        if (!string.IsNullOrEmpty(roomSceneName))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(roomSceneName);
        }
    }

    #endregion

    #region 私有方法 - UI更新

    /// <summary>
    /// 创建房间列表条目
    /// </summary>
    private void CreateRoomItem(RoomManager.RoomInfo roomInfo)
    {
        if (RoomItemPrefab == null || RoomListContent == null)
        {
            Debug.LogWarning("[LobbyUI] RoomItemPrefab 或 RoomListContent 未设置");
            return;
        }

        // 实例化条目
        GameObject item = Instantiate(RoomItemPrefab, RoomListContent);
        _roomItemList.Add(item);

        // 设置文本（预制体应有6个Text子对象：ID/名称/房主/人数/密码/状态）
        var texts = item.GetComponentsInChildren<Text>();
        if (texts.Length >= 6)
        {
            texts[0].text = roomInfo.RoomId;           // 房间ID
            texts[1].text = roomInfo.RoomName;         // 房间名称
            texts[2].text = roomInfo.HostPlayerId;     // 房主ID
            texts[3].text = $"{roomInfo.CurrentPlayerCount}/{roomInfo.MaxPlayers}"; // 人数
            texts[4].text = "无密码"; // 是否加密
            texts[5].text = roomInfo.IsInGame ? "游戏中" : "等待中"; // 游戏状态
        }

        // 添加点击事件（加入房间）
        var button = item.GetComponent<Button>();
        if (button == null)
        {
            button = item.AddComponent<Button>();
        }

        string roomId = roomInfo.RoomId;
        button.onClick.AddListener(() => OnRoomItemClick(roomId));
    }

    /// <summary>
    /// 点击房间条目（加入房间）
    /// </summary>
    private void OnRoomItemClick(string roomId)
    {
        Debug.Log($"[LobbyUI] 点击加入房间: {roomId}");
        ShowMessage($"正在加入房间 {roomId}...", Color.cyan);

        RoomManager.GetInstance().JoinRoom(roomId);
    }

    /// <summary>
    /// 清空房间列表
    /// </summary>
    private void ClearRoomList()
    {
        foreach (var item in _roomItemList)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }
        _roomItemList.Clear();
    }

    /// <summary>
    /// 显示提示信息
    /// </summary>
    private void ShowMessage(string message, Color color)
    {
        if (Text_Message != null)
        {
            Text_Message.text = message;
            Text_Message.color = color;
        }
        Debug.Log($"[LobbyUI] {message}");
    }

    /// <summary>
    /// 重置处理状态
    /// </summary>
    private void ResetProcessing()
    {
        _isProcessing = false;
    }

    #endregion
}
