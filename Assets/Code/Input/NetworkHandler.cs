using UnityEngine;
using GameNetwork;
using System;

/// <summary>
/// 网络消息处理器（精简版）
/// 职责：只处理最底层的网络连接和心跳
/// 所有游戏逻辑（房间、玩家、帧同步）都通过 EventBus 分发到对应的 Manager
/// </summary>
public class NetworkHandler : MonoBehaviour
{
    #region 单例模式

    private static NetworkHandler _instance;
    public static NetworkHandler Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("NetworkHandler");
                _instance = go.AddComponent<NetworkHandler>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 延迟注册，确保 NetworkManager 已初始化
        Invoke(nameof(RegisterHandlers), 0.1f);
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    #endregion

    #region Handler 注册

    /// <summary>
    /// 注册所有消息处理器
    /// 核心原则：收到消息后，解析数据，然后通过 EventBus 发布事件
    /// 不直接操作任何游戏对象（Player、Room 等）
    /// </summary>
    private void RegisterHandlers()
    {
        var nm = NetworkManager.Instance;
        if (nm == null)
        {
            Debug.LogError("[NetworkHandler] ❌ NetworkManager 未初始化！");
            return;
        }

        // === 心跳 ===
        nm.RegisterHandler(MsgID.S2CHeartBeat, OnHeartBeat);

        // === 登录认证 → 直接发布 EventBus 事件 ===
        nm.RegisterHandler(MsgID.S2CLogin, OnLoginResponse);

        // === 房间管理 → 解析后发布 EventBus 事件 ===
        nm.RegisterHandler(MsgID.S2CCreateRoom, OnCreateRoom);
        nm.RegisterHandler(MsgID.S2CJoinRoom, OnJoinRoom);
        nm.RegisterHandler(MsgID.S2CLeaveRoom, OnLeaveRoom);
        nm.RegisterHandler(MsgID.S2CRoomList, OnRoomList);

        // === 广播通知 → 解析后发布 EventBus 事件 ===
        nm.RegisterHandler(MsgID.S2CPlayerJoin, OnPlayerJoin);
        nm.RegisterHandler(MsgID.S2CPlayerLeave, OnPlayerLeave);

        // === 帧同步 → 解析后发布 EventBus 事件 ===
        nm.RegisterHandler(MsgID.S2CFrameState, OnFrameState);

        // === 游戏控制 → 解析后发布 EventBus 事件 ===
        nm.RegisterHandler(MsgID.S2CGameStart, OnGameStart);
        nm.RegisterHandler(MsgID.S2CGameOver, OnGameOver);

        Debug.Log("[NetworkHandler] ✅ 已注册 10 个消息处理器（全部走 EventBus）");
    }

    #endregion

    #region 通用辅助方法：发布事件

    /// <summary>
    /// 获取 EventBus 实例的快捷方式
    /// </summary>
    private GameEventBus EventBus => GameEventBus.GetInstance();

    #endregion

    #region Handler 实现 - 心跳

    private void OnHeartBeat(byte[] data)
    {
        try
        {
            var resp = HeartBeatResp.Parser.ParseFrom(data);
            long ping = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - resp.ClientTime;

            if (ping > 200)
            {
                Debug.LogWarning($"[NetworkHandler] ⚠️ 高延迟: {ping}ms");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析心跳失败: {ex.Message}");
        }
    }

    #endregion

    #region Handler 实现 - 登录认证

    /// <summary>
    /// 收到登录响应 → 交给 NetworkManager 处理（NetworkManager 会发布 EventBus 事件）
    /// </summary>
    private void OnLoginResponse(byte[] data)
    {
        // NetworkManager.OnLoginResponse 会解析数据并发布 OnLoginSuccess/OnLoginFailed 事件
        NetworkManager.Instance.OnLoginResponse(data);
    }

    #endregion

    #region Handler 实现 - 房间管理（全部转 EventBus）

    private void OnCreateRoom(byte[] data)
    {
        try
        {
            var resp = CreateRoomResp.Parser.ParseFrom(data);

            if (resp.Success)
            {
                Debug.Log($"[NetworkHandler] ✅ 房间创建成功: {resp.RoomId}");

                // 发布房间创建事件
                EventBus.Publish(
                    GameEventType.OnRoomCreated,
                    RoomCreatedEventData.Create(resp.RoomId, resp.RoomId, isHost: true)
                );
            }
            else
            {
                Debug.LogError($"[NetworkHandler] ❌ 创建房间失败: {resp.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析创建房间响应失败: {ex.Message}");
        }
    }

    private void OnJoinRoom(byte[] data)
    {
        try
        {
            var resp = JoinRoomResp.Parser.ParseFrom(data);

            if (resp.Success && resp.RoomInfo != null)
            {
                Debug.Log($"[NetworkHandler] ✅ 加入房间: {resp.RoomInfo.RoomName}");

                // 收集玩家ID列表
                var playerIds = new System.Collections.Generic.List<string>();
                foreach (var player in resp.RoomInfo.Players)
                {
                    playerIds.Add(player.PlayerId);
                }

                // 发布加入房间事件
                EventBus.Publish(
                    GameEventType.OnJoinedRoom,
                    JoinedRoomEventData.Create(
                        resp.RoomInfo.RoomId,
                        resp.RoomInfo.RoomName,
                        resp.RoomInfo.HostId,
                        playerIds,
                        resp.RoomInfo.MaxPlayers
                    )
                );

                // 同时发布房间状态变更事件
                EventBus.Publish(
                    GameEventType.OnRoomStateChanged,
                    RoomStateChangedEventData.Create(
                        true,
                        resp.RoomInfo.RoomId,
                        resp.RoomInfo.CurrentPlayers
                    )
                );
            }
            else
            {
                Debug.LogError($"[NetworkHandler] ❌ 加入房间失败: {resp.Message}");

                EventBus.Publish(
                    GameEventType.OnJoinRoomFailed,
                    new JoinRoomFailedEventData() // 可以扩展为 JoinRoomFailedEventData
                );
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析加入房间响应失败: {ex.Message}");
        }
    }

    private void OnLeaveRoom(byte[] data)
    {
        try
        {
            var resp = LeaveRoomResp.Parser.ParseFrom(data);

            if (resp.Success)
            {
                Debug.Log("[NetworkHandler] ✅ 已离开房间");

                // LeaveRoomResp 没有 RoomId 字段，从 RoomManager 获取当前房间ID
                string currentRoomId = RoomManager.GetInstance()?.CurrentRoom?.RoomId ?? "";

                EventBus.Publish(
                    GameEventType.OnLeftRoom,
                    LeftRoomEventData.Create(currentRoomId)
                );
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析离开房间响应失败: {ex.Message}");
        }
    }

    private void OnRoomList(byte[] data)
    {
        try
        {
            var resp = RoomListResp.Parser.ParseFrom(data);

            Debug.Log($"[NetworkHandler] 📋 房间列表 ({resp.Rooms.Count}个):");

            var roomInfos = new System.Collections.Generic.List<RoomManager.RoomInfo>();
            foreach (var room in resp.Rooms)
            {
                Debug.Log($"   [{room.RoomId}] {room.RoomName} ({room.CurrentPlayers}/{room.MaxPlayers})");

                roomInfos.Add(new RoomManager.RoomInfo
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName,
                    HostPlayerId = room.HostId,
                    MaxPlayers = room.MaxPlayers
                });
            }

            EventBus.Publish(
                GameEventType.OnRoomListUpdated,
                RoomListUpdatedEventData.Create(roomInfos)
            );
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析房间列表失败: {ex.Message}");
        }
    }

    #endregion

    #region Handler 实现 - 玩家上下线（全部转 EventBus）

    private void OnPlayerJoin(byte[] data)
    {
        try
        {
            var notify = PlayerJoinNotify.Parser.ParseFrom(data);

            Debug.Log($"[NetworkHandler] 👋 玩家加入: {notify.Player.PlayerName} ({notify.Player.PlayerId})");

            EventBus.Publish(
                GameEventType.OnPlayerJoinedRoom,
                PlayerJoinedRoomEventData.Create(notify.Player.PlayerId, notify.Player.PlayerName)
            );
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析玩家加入通知失败: {ex.Message}");
        }
    }

    private void OnPlayerLeave(byte[] data)
    {
        try
        {
            var notify = PlayerLeaveNotify.Parser.ParseFrom(data);

            Debug.Log($"[NetworkHandler] 👋 玩家离开: {notify.PlayerId}");

            EventBus.Publish(
                GameEventType.OnPlayerLeftRoom,
                PlayerLeftRoomEventData.Create(notify.PlayerId)
            );
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析玩家离开通知失败: {ex.Message}");
        }
    }

    #endregion

    #region Handler 实现 - 帧同步（全部转 EventBus）

    private void OnFrameState(byte[] data)
    {
        try
        {
            var frameState = FrameStateResp.Parser.ParseFrom(data);

            // 发布帧同步更新事件
            // 注意：PlayerInput 只包含输入数据（MoveX, MoveY等），不包含位置
            // 位置计算由 FrameSyncManager / RemotePlayerMovement 处理
            var eventData = FrameSyncUpdateEventData.Create(
                (int)frameState.FrameId,
                frameState.ServerTime,
                frameState.PlayerInputs.Count
            );
            eventData.RawData = data;

            EventBus.Publish(GameEventType.OnFrameSyncUpdate, eventData);

            // 同时发布每个玩家的输入事件（供 RemotePlayerMovement 使用）
            foreach (var kvp in frameState.PlayerInputs)
            {
                string playerId = kvp.Key;
                var input = kvp.Value;

                EventBus.Publish(
                    GameEventType.OnPlayerInputReceived,
                    PlayerInputEventData.Create(
                        playerId,
                        (int)frameState.FrameId,
                        input.MoveX,
                        input.MoveY,
                        input.Jump,
                        input.Fire
                    )
                );
            }

            // 调试日志（每100帧输出一次）
            if (frameState.FrameId % 100 == 0)
            {
                Debug.Log($"[NetworkHandler] 🎮 帧 #{frameState.FrameId}, 在线输入: {frameState.PlayerInputs.Count}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 处理帧状态异常: {ex.Message}");
        }
    }

    #endregion

    #region Handler 实现 - 游戏控制（全部转 EventBus）

    private void OnGameStart(byte[] data)
    {
        try
        {
            var notify = GameStartNotify.Parser.ParseFrom(data);

            Debug.Log($"[NetworkHandler] 🎮 游戏开始! Room: {notify.RoomId}");

            EventBus.Publish(
                GameEventType.OnGameStart,
                GameStartedEventData.Create(notify.RoomId)
            );
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析游戏开始通知失败: {ex.Message}");
        }
    }

    private void OnGameOver(byte[] data)
    {
        try
        {
            var notify = GameOverNotify.Parser.ParseFrom(data);

            Debug.Log($"[NetworkHandler] 🏁 游戏结束! 胜利者: {notify.WinnerId}");

            EventBus.Publish(
                GameEventType.OnGameOver,
                GameEndedEventData.Create(notify.WinnerId)
            );
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 解析游戏结束通知失败: {ex.Message}");
        }
    }

    #endregion
}
