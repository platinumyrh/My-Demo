using UnityEngine;
using GameNetwork;
using System.Collections.Generic;
using System;  // 添加 System 命名空间（包含 DateTimeOffset 和 Exception）

/// <summary>
/// 网络消息处理器
/// 负责注册和处理所有服务器消息
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

    #region 私有字段

    // 远程玩家字典 (playerId → RemotePlayerMovement)
    private Dictionary<string, RemotePlayerMovement> _remotePlayers;
    
    // 本地玩家引用
    private LocalPlayerMovement _localPlayer;

    // 帧同步数据（用于调试）
    private uint _lastReceivedFrameId;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            Initialize();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 延迟一帧，确保其他组件已初始化
        Invoke(nameof(LateInitialize), 0.1f);
    }

    #endregion

    #region 初始化

    private void Initialize()
    {
        _remotePlayers = new Dictionary<string, RemotePlayerMovement>();
        
        Debug.Log("[NetworkHandler] 初始化完成");
    }

    /// <summary>
    /// 延迟初始化（等待 NetworkManager 就绪）
    /// </summary>
    private void LateInitialize()
    {
        // 注册所有消息处理器
        RegisterHandlers();
        
        // 查找本地玩家
        _localPlayer = FindObjectOfType<LocalPlayerMovement>();
        
        Debug.Log($"[NetworkHandler] ✅ 已注册 {CountHandlers()} 个消息处理器");
    }

    #endregion

    #region Handler 注册

    private void RegisterHandlers()
    {
        var nm = NetworkManager.Instance;

        // === 心跳 ===
        nm.RegisterHandler(MsgID.S2CHeartBeat, OnHeartBeat);

        // === 房间管理 ===
        nm.RegisterHandler(MsgID.S2CCreateRoom, OnCreateRoom);
        nm.RegisterHandler(MsgID.S2CJoinRoom, OnJoinRoom);
        nm.RegisterHandler(MsgID.S2CLeaveRoom, OnLeaveRoom);
        nm.RegisterHandler(MsgID.S2CRoomList, OnRoomList);

        // === 广播通知 ===
        nm.RegisterHandler(MsgID.S2CPlayerJoin, OnPlayerJoin);
        nm.RegisterHandler(MsgID.S2CPlayerLeave, OnPlayerLeave);

        // === 帧同步 ⭐ 核心！===
        nm.RegisterHandler(MsgID.S2CFrameState, OnFrameState);

        // === 游戏控制 ===
        nm.RegisterHandler(MsgID.S2CGameStart, OnGameStart);
        nm.RegisterHandler(MsgID.S2CGameOver, OnGameOver);
    }

    private int CountHandlers()
    {
        return 10; // 当前注册的 Handler 数量
    }

    #endregion

    #region Handler 实现 - 心跳

    private void OnHeartBeat(byte[] data)
    {
        try
        {
            var resp = HeartBeatResp.Parser.ParseFrom(data);
            long ping = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - resp.ClientTime;
            
            // 更新UI显示延迟（如果有）
            // UIManager.Instance.UpdatePing(ping);
            
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

    #region Handler 实现 - 房间管理

    private void OnCreateRoom(byte[] data)
    {
        var resp = CreateRoomResp.Parser.ParseFrom(data);
        
        if (resp.Success)
        {
            Debug.Log($"[NetworkHandler] ✅ 房间创建成功: {resp.RoomId}");
            // TODO: 显示房间UI，等待其他玩家加入
        }
        else
        {
            Debug.LogError($"[NetworkHandler] ❌ 创建房间失败: {resp.Message}");
        }
    }

    private void OnJoinRoom(byte[] data)
    {
        var resp = JoinRoomResp.Parser.ParseFrom(data);
        
        if (resp.Success && resp.RoomInfo != null)
        {
            Debug.Log($"[NetworkHandler] ✅ 加入房间: {resp.RoomInfo.RoomName}");
            Debug.Log($"   房间内玩家数: {resp.RoomInfo.CurrentPlayers}/{resp.RoomInfo.MaxPlayers}");
            
            // 初始化所有玩家（包括自己）
            InitializePlayers(resp.RoomInfo);
        }
        else
        {
            Debug.LogError($"[NetworkHandler] ❌ 加入房间失败: {resp.Message}");
        }
    }

    private void OnLeaveRoom(byte[] data)
    {
        var resp = LeaveRoomResp.Parser.ParseFrom(data);
        
        if (resp.Success)
        {
            Debug.Log("[NetworkHandler] ✅ 已离开房间");
            ClearAllRemotePlayers();
        }
    }

    private void OnRoomList(byte[] data)
    {
        var resp = RoomListResp.Parser.ParseFrom(data);
        
        Debug.Log($"[NetworkHandler] 📋 房间列表 ({resp.Rooms.Count}个):");
        
        foreach (var room in resp.Rooms)
        {
            Debug.Log($"   [{room.RoomId}] {room.RoomName} " +
                     $"({room.CurrentPlayers}/{room.MaxPlayers}) " +
                     $"{(room.IsPlaying ? "游戏中" : "等待中")}");
        }
        
        // TODO: 更新房间列表 UI
    }

    #endregion

    #region Handler 实现 - 玩家上下线

    private void OnPlayerJoin(byte[] data)
    {
        var notify = PlayerJoinNotify.Parser.ParseFrom(data);
        
        string newPlayerId = notify.Player.PlayerId;
        Debug.Log($"[NetworkHandler] 👋 玩家加入: {notify.Player.PlayerName} ({newPlayerId})");
        
        // 创建远程玩家对象
        CreateRemotePlayer(newPlayerId, notify.Player);
    }

    private void OnPlayerLeave(byte[] data)
    {
        var notify = PlayerLeaveNotify.Parser.ParseFrom(data);
        
        Debug.Log($"[NetworkHandler] 👋 玩家离开: {notify.PlayerId}");
        
        // 销毁远程玩家对象
        RemoveRemotePlayer(notify.PlayerId);
    }

    #endregion

    #region Handler 实现 - 帧同步 ⭐⭐⭐ 最重要！

    private void OnFrameState(byte[] data)
    {
        try
        {
            var frameState = FrameStateResp.Parser.ParseFrom(data);
            _lastReceivedFrameId = frameState.FrameId;
            
            // 1. 更新所有远程玩家的位置
            foreach (var remotePlayer in _remotePlayers.Values)
            {
                remotePlayer.OnReceiveFrameState(frameState);
            }
            
            // 2. （可选）检查本地玩家是否需要校正
            // 如果服务器返回了权威位置，可以在这里校正
            
            // 调试日志（每100帧输出一次）
            if (frameState.FrameId % 100 == 0)
            {
                Debug.Log($"[NetworkHandler] 🎮 帧 #{frameState.FrameId}, " +
                         $"在线输入: {frameState.PlayerInputs.Count}, " +
                         $"远程玩家: {_remotePlayers.Count}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkHandler] ❌ 处理帧状态异常: {ex.Message}");
        }
    }

    #endregion

    #region Handler 实现 - 游戏控制

    private void OnGameStart(byte[] data)
    {
        var notify = GameStartNotify.Parser.ParseFrom(data);
        
        Debug.Log($"[NetworkHandler] 🎮 游戏开始! Room: {notify.RoomId}");
        // TODO: 开始游戏逻辑（倒计时、禁用UI等）
    }

    private void OnGameOver(byte[] data)
    {
        var notify = GameOverNotify.Parser.ParseFrom(data);
        
        Debug.Log($"[NetworkHandler] 🏁 游戏结束! 胜利者: {notify.WinnerId}");
        // TODO: 显示结果界面
    }

    #endregion

    #region 玩家管理方法

    /// <summary>
    /// 初始化房间内所有玩家（加入房间时调用）
    /// </summary>
    private void InitializePlayers(RoomInfo roomInfo)
    {
        ClearAllRemotePlayers();
        
        foreach (var playerInfo in roomInfo.Players)
        {
            // 跳过本地玩家（假设第一个是房主/自己）
            // 实际应该根据 playerId 判断
            if (playerInfo.PlayerId == GetLocalPlayerId())
            {
                continue; // 这是自己，不创建远程对象
            }
            
            CreateRemotePlayer(playerInfo.PlayerId, playerInfo);
        }
    }

    /// <summary>
    /// 创建远程玩家对象
    /// </summary>
    private void CreateRemotePlayer(string playerId, PlayerInfo playerInfo)
    {
        if (_remotePlayers.ContainsKey(playerId))
        {
            return; // 已存在
        }
        
        // 实例化预制体（需要先创建一个远程玩家预制体）
        GameObject remotePlayerGO = new GameObject($"RemotePlayer_{playerId}");
        remotePlayerGO.transform.position = Vector3.zero; // 初始位置，后续由帧同步更新
        
        var remoteMovement = remotePlayerGO.AddComponent<RemotePlayerMovement>();
        remoteMovement.SetPlayerId(playerId);
        
        _remotePlayers[playerId] = remoteMovement;
        
        Debug.Log($"[NetworkHandler] ➕ 创建远程玩家: {playerInfo.PlayerName} ({playerId})");
    }

    /// <summary>
    /// 移除远程玩家
    /// </summary>
    private void RemoveRemotePlayer(string playerId)
    {
        if (_remotePlayers.TryGetValue(playerId, out var remotePlayer))
        {
            Destroy(remotePlayer.gameObject);
            _remotePlayers.Remove(playerId);
        }
    }

    /// <summary>
    /// 清除所有远程玩家
    /// </summary>
    private void ClearAllRemotePlayers()
    {
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value.gameObject);
            }
        }
        _remotePlayers.Clear();
    }

    /// <summary>
    /// 获取本地玩家ID（简化版，实际应从登录系统获取）
    /// </summary>
    private string GetLocalPlayerId()
    {
        // TODO: 从用户系统获取真实 ID
        // 暂时使用固定值或随机生成
        return "LocalPlayer_001";
    }

    #endregion
}
