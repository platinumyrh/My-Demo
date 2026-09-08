using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 网络相关事件数据
/// 定义所有网络通信事件需要传递的数据结构
/// </summary>
// 🔑 修复：移除 namespace GameEventBus 嵌套，避免命名空间冲突
// 所有事件数据类直接放在全局命名空间

#region 登录认证事件数据

/// <summary>
/// 登录成功事件数据
/// 当服务器验证账号密码成功后发布此事件
/// </summary>
public class LoginSuccessEventData : GameEventData
{
    /// <summary>
    /// 用户唯一ID（服务器分配的永久ID）
    /// </summary>
    public string UserId { get; set; } = "";

    /// <summary>
    /// 用户昵称/显示名称
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// 会话令牌（用于后续请求的身份验证）
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// 令牌过期时间（Unix时间戳毫秒）
    /// </summary>
    public long ExpireTime { get; set; }

    /// <summary>
    /// 便捷创建方法
    /// </summary>
    public static LoginSuccessEventData Create(string userId, string username, string token, long expireTime = 0)
    {
        return new LoginSuccessEventData
        {
            UserId = userId,
            Username = username,
            Token = token,
            ExpireTime = expireTime > 0 ? expireTime : DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeMilliseconds()
        };
    }

    public override string ToString()
    {
        return $"[LoginSuccess] UserId={UserId}, Username={Username}, Token={Token.Substring(0, Math.Min(8, Token.Length))}...";
    }
}

/// <summary>
/// 登录失败事件数据
/// </summary>
public class LoginFailedEventData : GameEventData
{
    public string ErrorMessage { get; set; } = "未知错误";
    public int ErrorCode { get; set; } = 0;

    public static LoginFailedEventData Create(string message, int errorCode = 0)
    {
        return new LoginFailedEventData
        {
            ErrorMessage = message,
            ErrorCode = errorCode
        };
    }

    public override string ToString()
    {
        return $"[LoginFailed] Error={ErrorMessage}, Code={ErrorCode}";
    }
}

/// <summary>
/// 登出事件数据（预留）
/// </summary>
public class LogoutEventData : GameEventData
{
    public string Reason { get; set; } = "主动登出";

    public static LogoutEventData Create(string reason = "主动登出")
    {
        return new LogoutEventData { Reason = reason };
    }
}

#endregion

#region 网络连接事件数据

/// <summary>
/// 连接服务器成功事件数据
/// </summary>
public class ConnectedEventData : GameEventData
{
    public string ServerIP { get; set; } = "";
    public int ServerPort { get; set; } = 0;
    public string LocalEndpoint { get; set; } = "";

    public static ConnectedEventData Create(string serverIp, int serverPort, string localEndpoint = "")
    {
        return new ConnectedEventData
        {
            ServerIP = serverIp,
            ServerPort = serverPort,
            LocalEndpoint = localEndpoint
        };
    }
}

/// <summary>
/// 断开连接事件数据
/// </summary>
public class DisconnectedEventData : GameEventData
{
    public string Reason { get; set; } = "未知原因";
    public bool IsIntentional { get; set; } = false;

    public static DisconnectedEventData Create(string reason, bool isIntentional = false)
    {
        return new DisconnectedEventData
        {
            Reason = reason,
            IsIntentional = isIntentional
        };
    }
}

#endregion

#region 房间管理事件数据

/// <summary>
/// 房间创建成功事件数据
/// </summary>
public class RoomCreatedEventData : GameEventData
{
    public string RoomId { get; set; } = "";
    public string RoomName { get; set; } = "";
    public bool IsHost { get; set; } = true;

    public static RoomCreatedEventData Create(string roomId, string roomName, bool isHost = true)
    {
        return new RoomCreatedEventData
        {
            RoomId = roomId,
            RoomName = roomName,
            IsHost = isHost
        };
    }
}

/// <summary>
/// 加入房间成功事件数据
/// </summary>
public class JoinedRoomEventData : GameEventData
{
    public string RoomId { get; set; } = "";
    public string RoomName { get; set; } = "";
    public string HostPlayerId { get; set; } = "";
    public List<string> PlayerIds { get; set; } = new List<string>();
    public int MaxPlayers { get; set; } = 4;

    public static JoinedRoomEventData Create(string roomId, string roomName, string hostPlayerId, List<string> playerIds, int maxPlayers = 4)
    {
        return new JoinedRoomEventData
        {
            RoomId = roomId,
            RoomName = roomName,
            HostPlayerId = hostPlayerId,
            PlayerIds = playerIds,
            MaxPlayers = maxPlayers
        };
    }
}

/// <summary>
/// 加入房间失败事件数据
/// </summary>
public class JoinRoomFailedEventData : GameEventData
{
    public string RoomId { get; set; } = "";
    public string Reason { get; set; } = "";

    public static JoinRoomFailedEventData Create(string roomId, string reason = "")
    {
        return new JoinRoomFailedEventData { RoomId = roomId, Reason = reason };
    }
}

/// <summary>
/// 创建房间失败事件数据
/// </summary>
public class RoomCreateFailedEventData : GameEventData
{
    public string Reason { get; set; } = "";

    public static RoomCreateFailedEventData Create(string reason = "")
    {
        return new RoomCreateFailedEventData { Reason = reason };
    }
}

/// <summary>
/// 离开房间事件数据
/// </summary>
public class LeftRoomEventData : GameEventData
{
    public string RoomId { get; set; } = "";
    public string Reason { get; set; } = "";

    public static LeftRoomEventData Create(string roomId, string reason = "")
    {
        return new LeftRoomEventData { RoomId = roomId, Reason = reason };
    }
}

/// <summary>
/// 房间状态变更事件数据
/// </summary>
public class RoomStateChangedEventData : GameEventData
{
    public bool IsInRoom { get; set; } = false;
    public string RoomId { get; set; } = "";
    public int PlayerCount { get; set; } = 0;

    public static RoomStateChangedEventData Create(bool isInRoom, string roomId, int playerCount)
    {
        return new RoomStateChangedEventData
        {
            IsInRoom = isInRoom,
            RoomId = roomId,
            PlayerCount = playerCount
        };
    }
}

/// <summary>
/// 房间列表更新事件数据
/// </summary>
public class RoomListUpdatedEventData : GameEventData
{
    public List<RoomManager.RoomInfo> Rooms { get; set; } = new List<RoomManager.RoomInfo>();

    public static RoomListUpdatedEventData Create(List<RoomManager.RoomInfo> rooms)
    {
        return new RoomListUpdatedEventData { Rooms = rooms };
    }
}

/// <summary>
/// 其他玩家加入房间事件数据
/// </summary>
public class PlayerJoinedRoomEventData : GameEventData
{
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";

    public static PlayerJoinedRoomEventData Create(string playerId, string playerName)
    {
        return new PlayerJoinedRoomEventData
        {
            PlayerId = playerId,
            PlayerName = playerName
        };
    }
}

/// <summary>
/// 其他玩家离开房间事件数据
/// </summary>
public class PlayerLeftRoomEventData : GameEventData
{
    public string PlayerId { get; set; } = "";

    public static PlayerLeftRoomEventData Create(string playerId)
    {
        return new PlayerLeftRoomEventData { PlayerId = playerId };
    }
}

#endregion

#region 游戏控制事件数据

/// <summary>
/// 远端玩家开火事件数据（NetworkHandler 解析 S2C_FireEvent 后发布）
/// </summary>
public class RemoteFiredEventData : GameEventData
{
    public string PlayerId { get; set; } = "";
    public Vector3 Origin { get; set; }
    public Vector3 Direction { get; set; }
    public Vector3 EndPoint { get; set; }
    public bool HitSomething { get; set; }
    public string HitPlayerId { get; set; } = "";
    public Vector3 HitNormal { get; set; }

    public static RemoteFiredEventData Create(string playerId, Vector3 origin, Vector3 direction, Vector3 endPoint, bool hitSomething, string hitPlayerId, Vector3 hitNormal)
    {
        return new RemoteFiredEventData
        {
            PlayerId = playerId,
            Origin = origin,
            Direction = direction,
            EndPoint = endPoint,
            HitSomething = hitSomething,
            HitPlayerId = hitPlayerId,
            HitNormal = hitNormal
        };
    }
}

#endregion

#region 游戏控制事件数据

/// <summary>
/// 游戏开始事件数据
/// </summary>
public class GameStartedEventData : GameEventData
{
    public string RoomId { get; set; } = "";
    public long StartTime { get; set; } = 0;
    public int Seed { get; set; } = 0; // 随机种子，确保所有客户端一致

    public static GameStartedEventData Create(string roomId, int seed = 0)
    {
        return new GameStartedEventData
        {
            RoomId = roomId,
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Seed = seed
        };
    }
}

/// <summary>
/// 游戏结束事件数据
/// </summary>
public class GameEndedEventData : GameEventData
{
    public string WinnerId { get; set; } = "";
    public int Reason { get; set; } = 0;
    public string Message { get; set; } = "";

    public static GameEndedEventData Create(string winnerId, int reason = 0, string message = "")
    {
        return new GameEndedEventData
        {
            WinnerId = winnerId,
            Reason = reason,
            Message = message
        };
    }
}

#endregion
