using UnityEngine;
using GameNetwork;
using GameLockstep;
using Google.Protobuf;
using System;
using System.Collections.Generic;

/// <summary>
/// 网络消息处理器（解析层）
/// 职责单一：把 NetworkManager 分发出来的消息字节解析成 protobuf 对象，然后：
/// 1. 常规业务消息（登录/房间/广播/游戏控制）→ 解析后发布 EventBus 事件
/// 2. 高频消息（帧同步 S2C_FrameState）→ 直接调用 FrameSyncManager，不过 EventBus
/// 本类不保存任何业务状态，不直接操作游戏对象
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
    /// 泛型注册：内部完成 protobuf 解析 + 异常捕获，
    /// 各业务处理器只拿到强类型消息对象，不再手写 ParseFrom/try-catch 样板
    /// </summary>
    private void Register<T>(MsgID msgId, MessageParser<T> parser, Action<T> handler)
        where T : IMessage<T>
    {
        NetworkManager.Instance.RegisterHandler(msgId, data =>
        {
            try
            {
                handler(parser.ParseFrom(data));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkHandler] ❌ 处理 {typeof(T).Name} 失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 注册所有消息处理器
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
        Register(MsgID.S2CHeartBeat, HeartBeatResp.Parser, OnHeartBeat);

        // === 登录认证 ===
        Register(MsgID.S2CLogin, LoginResp.Parser, OnLoginResponse);

        // === 房间管理 ===
        Register(MsgID.S2CCreateRoom, CreateRoomResp.Parser, OnCreateRoom);
        Register(MsgID.S2CJoinRoom, JoinRoomResp.Parser, OnJoinRoom);
        Register(MsgID.S2CLeaveRoom, LeaveRoomResp.Parser, OnLeaveRoom);
        Register(MsgID.S2CRoomList, RoomListResp.Parser, OnRoomList);

        // === 广播通知 ===
        Register(MsgID.S2CPlayerJoin, PlayerJoinNotify.Parser, OnPlayerJoin);
        Register(MsgID.S2CPlayerLeave, PlayerLeaveNotify.Parser, OnPlayerLeave);

        // === 帧同步（高频路径，直调 FrameSyncManager）===
        Register(MsgID.S2CFrameState, FrameStateResp.Parser, OnFrameState);

        // === lockstep 帧同步（新链路，高频直调转换层→事件枢纽，与旧链路并行）===
        Register(MsgID.S2CLockstepInputs, LockstepInputsNotify.Parser, OnLockstepInputs);

        // === 战斗事件 ===
        Register(MsgID.S2CFireEvent, FireEventNotify.Parser, OnFireEvent);
        Register(MsgID.S2CPlayerDied, PlayerDiedNotify.Parser, OnPlayerDied);

        // === 游戏控制 ===
        Register(MsgID.S2CGameStart, GameStartNotify.Parser, OnGameStart);
        Register(MsgID.S2CGameOver, GameOverNotify.Parser, OnGameOver);

        Debug.Log("[NetworkHandler] ✅ 已注册 12 个消息处理器");
    }

    #endregion

    #region 通用辅助

    /// <summary>
    /// 获取 EventBus 实例的快捷方式
    /// </summary>
    private GameEventBus EventBus => GameEventBus.GetInstance();

    #endregion

    #region Handler 实现 - 心跳

    private void OnHeartBeat(HeartBeatResp resp)
    {
        long ping = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - resp.ClientTime;

        if (ping > 200)
        {
            Debug.LogWarning($"[NetworkHandler] ⚠️ 高延迟: {ping}ms");
        }
    }

    #endregion

    #region Handler 实现 - 登录认证

    private void OnLoginResponse(LoginResp resp)
    {
        if (resp.Success)
        {
            // 会话状态由 NetworkManager 保存（纯数据）
            NetworkManager.Instance.ApplyLoginSuccess(resp.UserId, resp.Username, resp.Token);

            Debug.Log($"[NetworkHandler] ✅ 登录成功: {resp.Username} ({resp.UserId})");

            EventBus.Publish(
                GameEventType.OnLoginSuccess,
                LoginSuccessEventData.Create(
                    userId: resp.UserId,
                    username: resp.Username,
                    token: resp.Token,
                    expireTime: resp.ExpireTime
                )
            );
        }
        else
        {
            Debug.LogError($"[NetworkHandler] ❌ 登录失败: {resp.Message}");

            // 登录失败时断开连接，停止心跳
            NetworkManager.Instance.Disconnect();

            EventBus.Publish(
                GameEventType.OnLoginFailed,
                LoginFailedEventData.Create(resp.Message)
            );
        }
    }

    #endregion

    #region Handler 实现 - 房间管理

    private void OnCreateRoom(CreateRoomResp resp)
    {
        if (resp.Success)
        {
            Debug.Log($"[NetworkHandler] ✅ 房间创建成功: {resp.RoomId}");

            EventBus.Publish(
                GameEventType.OnRoomCreated,
                RoomCreatedEventData.Create(resp.RoomId, resp.RoomId, isHost: true)
            );
        }
        else
        {
            Debug.LogError($"[NetworkHandler] ❌ 创建房间失败: {resp.Message}");

            EventBus.Publish(
                GameEventType.OnRoomCreateFailed,
                RoomCreateFailedEventData.Create(resp.Message)
            );
        }
    }

    private void OnJoinRoom(JoinRoomResp resp)
    {
        if (resp.Success && resp.RoomInfo != null)
        {
            Debug.Log($"[NetworkHandler] ✅ 加入房间: {resp.RoomInfo.RoomName}");

            // 记录房间成员的昵称（GameInitializer 重建远端玩家时显示用）
            PlayerManager.GetInstance()?.RecordPlayerNames(resp.RoomInfo);

            var playerIds = new List<string>();
            foreach (var player in resp.RoomInfo.Players)
            {
                playerIds.Add(player.PlayerId);
            }

            // 房间状态（RoomManager.CurrentRoom / OnRoomStateChanged）统一由
            // RoomManager 在收到本事件后维护，这里只负责转发原始信息
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
        }
        else
        {
            Debug.LogError($"[NetworkHandler] ❌ 加入房间失败: {resp.Message}");

            EventBus.Publish(
                GameEventType.OnJoinRoomFailed,
                JoinRoomFailedEventData.Create(resp.RoomId, resp.Message)
            );
        }
    }

    private void OnLeaveRoom(LeaveRoomResp resp)
    {
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

    private void OnRoomList(RoomListResp resp)
    {
        Debug.Log($"[NetworkHandler] 📋 房间列表 ({resp.Rooms.Count}个)");

        var roomInfos = new List<RoomManager.RoomInfo>();
        foreach (var room in resp.Rooms)
        {
            var info = new RoomManager.RoomInfo
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                HostPlayerId = room.HostId,
                MaxPlayers = room.MaxPlayers,
                IsInGame = room.IsPlaying
            };
            // 玩家列表也要填：界面显示的"当前人数"由 PlayerIds.Count 计算，漏填会永远显示 0
            foreach (var player in room.Players)
            {
                info.PlayerIds.Add(player.PlayerId);
            }
            roomInfos.Add(info);
        }

        EventBus.Publish(
            GameEventType.OnRoomListUpdated,
            RoomListUpdatedEventData.Create(roomInfos)
        );
    }

    #endregion

    #region Handler 实现 - 玩家上下线广播

    private void OnPlayerJoin(PlayerJoinNotify notify)
    {
        Debug.Log($"[NetworkHandler] 👋 玩家加入: {notify.Player.PlayerName} ({notify.Player.PlayerId})");

        EventBus.Publish(
            GameEventType.OnPlayerJoinedRoom,
            PlayerJoinedRoomEventData.Create(notify.Player.PlayerId, notify.Player.PlayerName)
        );
    }

    private void OnPlayerLeave(PlayerLeaveNotify notify)
    {
        Debug.Log($"[NetworkHandler] 👋 玩家离开: {notify.PlayerId}");

        EventBus.Publish(
            GameEventType.OnPlayerLeftRoom,
            PlayerLeftRoomEventData.Create(notify.PlayerId)
        );
    }

    #endregion

    #region Handler 实现 - 帧同步（高频路径：直调 FrameSyncManager，不走 EventBus）

    private void OnFrameState(FrameStateResp frameState)
    {
        FrameSyncManager.GetInstance()?.OnServerFrame(frameState);
    }

    #endregion

    #region Handler 实现 - 战斗事件

    private void OnFireEvent(FireEventNotify notify)
    {
        EventBus.Publish(
            GameEventType.OnRemoteFired,
            RemoteFiredEventData.Create(
                notify.PlayerId,
                new UnityEngine.Vector3(notify.OriginX, notify.OriginY, notify.OriginZ),
                new UnityEngine.Vector3(notify.DirX, notify.DirY, notify.DirZ),
                new UnityEngine.Vector3(notify.EndX, notify.EndY, notify.EndZ),
                notify.HitSomething,
                notify.HitPlayerId,
                new UnityEngine.Vector3(notify.HitNormalX, notify.HitNormalY, notify.HitNormalZ)
            )
        );
    }

    private void OnPlayerDied(PlayerDiedNotify notify)
    {
        EventBus.Publish(
            GameEventType.OnPlayerDied,
            PlayerDiedEventData.Create(notify.VictimId, notify.KillerId)
        );
    }

    #endregion

    #region Handler 实现 - lockstep（新链路）

    private void OnLockstepInputs(LockstepInputsNotify notify)
    {
        foreach (var frame in notify.Frames)
            LockstepNetEvents.RaiseFrameInputs(frame.Frame, LockstepProtoConvert.ToInputs(frame));
    }

    #endregion

    #region Handler 实现 - 游戏控制

    private void OnGameStart(GameStartNotify notify)
    {
        Debug.Log($"[NetworkHandler] 🎮 游戏开始! Room: {notify.RoomId}");

        // lockstep 初始化信息旁路给驱动层（种子/帧率/座位表；
        // 旧服务器这些字段为默认值，驱动层据此判断回退旧链路）
        var seats = new List<KeyValuePair<string, int>>(notify.Seats.Count);
        foreach (var s in notify.Seats)
            seats.Add(new KeyValuePair<string, int>(s.PlayerId, s.Seat));
        LockstepNetEvents.RaiseGameStart(notify.LockstepSeed, notify.TickRate, seats);

        EventBus.Publish(
            GameEventType.OnGameStart,
            GameStartedEventData.Create(notify.RoomId)
        );
    }

    private void OnGameOver(GameOverNotify notify)
    {
        Debug.Log($"[NetworkHandler] 🏁 游戏结束! 胜利者: {notify.WinnerId}");

        EventBus.Publish(
            GameEventType.OnGameOver,
            GameEndedEventData.Create(notify.WinnerId)
        );
    }

    #endregion
}
