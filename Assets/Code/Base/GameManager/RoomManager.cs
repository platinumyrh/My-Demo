using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 房间管理器
/// 负责管理房间状态、房间列表、当前房间信息
/// 通过 EventBus 订阅网络事件，更新房间状态
/// 
/// 🔑 继承 BaseManager 实现单例模式
/// </summary>
public class RoomManager : BaseManager<RoomManager>
{
    #region 房间数据结构

    /// <summary>
    /// 房间信息
    /// </summary>
    public class RoomInfo
    {
        public string RoomId;
        public string RoomName;
        public string HostPlayerId;
        public List<string> PlayerIds = new List<string>();
        public int MaxPlayers = 4;
        public bool IsInGame;

        public int CurrentPlayerCount => PlayerIds.Count;
        public bool IsFull => CurrentPlayerCount >= MaxPlayers;
        public bool IsEmpty => CurrentPlayerCount == 0;
    }

    #endregion

    #region 属性

    /// <summary>
    /// 当前房间信息
    /// </summary>
    public RoomInfo CurrentRoom { get; private set; }

    /// <summary>
    /// 是否在某个房间中
    /// </summary>
    public bool IsInRoom => CurrentRoom != null;

    /// <summary>
    /// 当前房间的所有玩家ID列表
    /// </summary>
    public List<string> RoomPlayerIds => CurrentRoom?.PlayerIds ?? new List<string>();

    /// <summary>
    /// 当前房间玩家数量
    /// </summary>
    public int RoomPlayerCount => CurrentRoom?.CurrentPlayerCount ?? 0;

    #endregion

    #region 私有字段

    // 房间列表缓存
    private Dictionary<string, RoomInfo> _roomList = new Dictionary<string, RoomInfo>();

    // 是否已初始化
    private bool _isInitialized = false;

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        SubscribeToEventBus();

        _isInitialized = true;
        Debug.Log("[RoomManager] ✅ 初始化完成");
    }

    /// <summary>
    /// 订阅 EventBus 事件
    /// </summary>
    private void SubscribeToEventBus()
    {
        var eventBus = GameEventBus.GetInstance();

        // 登录成功
        eventBus.Subscribe<LoginSuccessEventData>(
            GameEventType.OnLoginSuccess,
            OnLoginSuccess
        );

        // 房间列表更新
        eventBus.Subscribe<RoomListUpdatedEventData>(
            GameEventType.OnRoomListUpdated,
            OnRoomListUpdated
        );

        // 加入房间成功
        eventBus.Subscribe<JoinedRoomEventData>(
            GameEventType.OnJoinedRoom,
            OnJoinedRoom
        );

        // 离开房间
        eventBus.Subscribe<LeftRoomEventData>(
            GameEventType.OnLeftRoom,
            OnLeftRoom
        );

        // 其他玩家加入
        eventBus.Subscribe<PlayerJoinedRoomEventData>(
            GameEventType.OnPlayerJoinedRoom,
            OnPlayerJoinedRoom
        );

        // 其他玩家离开
        eventBus.Subscribe<PlayerLeftRoomEventData>(
            GameEventType.OnPlayerLeftRoom,
            OnPlayerLeftRoom
        );

        // 游戏开始
        eventBus.Subscribe<GameStartedEventData>(
            GameEventType.OnGameStart,
            OnGameStarted
        );

        // 游戏结束
        eventBus.Subscribe<GameEndedEventData>(
            GameEventType.OnGameOver,
            OnGameEnded
        );

        Debug.Log("[RoomManager] 📡 已订阅 EventBus 事件");
    }

    /// <summary>
    /// 取消 EventBus 订阅
    /// </summary>
    public void UnsubscribeFromEventBus()
    {
        var eventBus = GameEventBus.GetInstance();

        eventBus.Unsubscribe<LoginSuccessEventData>(
            GameEventType.OnLoginSuccess,
            OnLoginSuccess
        );

        eventBus.Unsubscribe<RoomListUpdatedEventData>(
            GameEventType.OnRoomListUpdated,
            OnRoomListUpdated
        );

        eventBus.Unsubscribe<JoinedRoomEventData>(
            GameEventType.OnJoinedRoom,
            OnJoinedRoom
        );

        eventBus.Unsubscribe<LeftRoomEventData>(
            GameEventType.OnLeftRoom,
            OnLeftRoom
        );

        eventBus.Unsubscribe<PlayerJoinedRoomEventData>(
            GameEventType.OnPlayerJoinedRoom,
            OnPlayerJoinedRoom
        );

        eventBus.Unsubscribe<PlayerLeftRoomEventData>(
            GameEventType.OnPlayerLeftRoom,
            OnPlayerLeftRoom
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

    #region EventBus 事件处理器

    /// <summary>
    /// 登录成功
    /// </summary>
    private void OnLoginSuccess(LoginSuccessEventData eventData)
    {
        Debug.Log("[RoomManager] 🎉 登录成功，准备房间系统");
        // 可以在这里请求房间列表
    }

    /// <summary>
    /// 房间列表更新
    /// </summary>
    private void OnRoomListUpdated(RoomListUpdatedEventData eventData)
    {
        Debug.Log($"[RoomManager] 📋 房间列表更新: {eventData.Rooms.Count} 个房间");

        _roomList.Clear();
        foreach (var room in eventData.Rooms)
        {
            _roomList[room.RoomId] = room;
        }
    }

    /// <summary>
    /// 加入房间成功
    /// </summary>
    private void OnJoinedRoom(JoinedRoomEventData eventData)
    {
        Debug.Log($"[RoomManager] 🏠 加入房间成功: {eventData.RoomId}");

        CurrentRoom = new RoomInfo
        {
            RoomId = eventData.RoomId,
            RoomName = eventData.RoomName,
            HostPlayerId = eventData.HostPlayerId,
            PlayerIds = new List<string>(eventData.PlayerIds),
            MaxPlayers = eventData.MaxPlayers
        };

        // 发布房间状态变更事件
        GameEventBus.GetInstance().Publish(
            GameEventType.OnRoomStateChanged,
            new RoomStateChangedEventData
            {
                IsInRoom = true,
                RoomId = eventData.RoomId,
                PlayerCount = CurrentRoom.CurrentPlayerCount
            }
        );
    }

    /// <summary>
    /// 离开房间
    /// </summary>
    private void OnLeftRoom(LeftRoomEventData eventData)
    {
        Debug.Log($"[RoomManager] 🚪 离开房间: {eventData.RoomId}");

        string roomId = CurrentRoom?.RoomId;
        CurrentRoom = null;

        // 发布房间状态变更事件
        GameEventBus.GetInstance().Publish(
            GameEventType.OnRoomStateChanged,
            new RoomStateChangedEventData
            {
                IsInRoom = false,
                RoomId = roomId,
                PlayerCount = 0
            }
        );
    }

    /// <summary>
    /// 其他玩家加入房间
    /// </summary>
    private void OnPlayerJoinedRoom(PlayerJoinedRoomEventData eventData)
    {
        if (CurrentRoom == null) return;

        if (!CurrentRoom.PlayerIds.Contains(eventData.PlayerId))
        {
            CurrentRoom.PlayerIds.Add(eventData.PlayerId);
            Debug.Log($"[RoomManager] 👋 玩家加入房间: {eventData.PlayerName} (当前 {CurrentRoom.CurrentPlayerCount}/{CurrentRoom.MaxPlayers})");
        }

        // 发布房间状态变更事件
        GameEventBus.GetInstance().Publish(
            GameEventType.OnRoomStateChanged,
            new RoomStateChangedEventData
            {
                IsInRoom = true,
                RoomId = CurrentRoom.RoomId,
                PlayerCount = CurrentRoom.CurrentPlayerCount
            }
        );
    }

    /// <summary>
    /// 其他玩家离开房间
    /// </summary>
    private void OnPlayerLeftRoom(PlayerLeftRoomEventData eventData)
    {
        if (CurrentRoom == null) return;

        CurrentRoom.PlayerIds.Remove(eventData.PlayerId);
        Debug.Log($"[RoomManager] 👋 玩家离开房间: {eventData.PlayerId} (当前 {CurrentRoom.CurrentPlayerCount}/{CurrentRoom.MaxPlayers})");

        // 发布房间状态变更事件
        GameEventBus.GetInstance().Publish(
            GameEventType.OnRoomStateChanged,
            new RoomStateChangedEventData
            {
                IsInRoom = true,
                RoomId = CurrentRoom.RoomId,
                PlayerCount = CurrentRoom.CurrentPlayerCount
            }
        );
    }

    /// <summary>
    /// 游戏开始
    /// </summary>
    private void OnGameStarted(GameStartedEventData eventData)
    {
        if (CurrentRoom != null)
        {
            CurrentRoom.IsInGame = true;
        }

        Debug.Log("[RoomManager] 🎮 游戏开始！");
    }

    /// <summary>
    /// 游戏结束
    /// </summary>
    private void OnGameEnded(GameEndedEventData eventData)
    {
        if (CurrentRoom != null)
        {
            CurrentRoom.IsInGame = false;
        }

        Debug.Log("[RoomManager] 🏁 游戏结束！");
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 获取房间列表
    /// </summary>
    public Dictionary<string, RoomInfo> GetRoomList()
    {
        return new Dictionary<string, RoomInfo>(_roomList);
    }

    /// <summary>
    /// 获取指定房间信息
    /// </summary>
    public RoomInfo GetRoomInfo(string roomId)
    {
        _roomList.TryGetValue(roomId, out var room);
        return room;
    }

    /// <summary>
    /// 检查是否是房主
    /// </summary>
    public bool IsHost(string playerId)
    {
        return CurrentRoom != null && CurrentRoom.HostPlayerId == playerId;
    }

    /// <summary>
    /// 获取当前房间的玩家ID列表（不包含自己）
    /// </summary>
    public List<string> GetOtherPlayerIds(string myPlayerId)
    {
        var others = new List<string>();
        if (CurrentRoom == null) return others;

        foreach (var id in CurrentRoom.PlayerIds)
        {
            if (id != myPlayerId)
            {
                others.Add(id);
            }
        }

        return others;
    }

    /// <summary>
    /// 清除房间状态（用于断开连接时）
    /// </summary>
    public void ClearRoomState()
    {
        CurrentRoom = null;
        _roomList.Clear();
        Debug.Log("[RoomManager] 🧹 房间状态已清除");
    }

    #endregion

    #region 调试方法

    /// <summary>
    /// 打印当前状态
    /// </summary>
    public void PrintStatus()
    {
        Debug.Log("========== RoomManager Status ==========");
        Debug.Log($"IsInRoom: {IsInRoom}");

        if (CurrentRoom != null)
        {
            Debug.Log($"Current Room: {CurrentRoom.RoomName} ({CurrentRoom.RoomId})");
            Debug.Log($"Host: {CurrentRoom.HostPlayerId}");
            Debug.Log($"Players: {CurrentRoom.CurrentPlayerCount}/{CurrentRoom.MaxPlayers}");
            Debug.Log($"In Game: {CurrentRoom.IsInGame}");

            foreach (var playerId in CurrentRoom.PlayerIds)
            {
                Debug.Log($"  - {playerId}");
            }
        }
        else
        {
            Debug.Log("Current Room: None");
        }

        Debug.Log($"Cached Rooms: {_roomList.Count}");
        Debug.Log("========================================");
    }

    #endregion
}
