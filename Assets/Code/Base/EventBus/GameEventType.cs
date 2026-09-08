using System;
using UnityEngine;

/// <summary>
/// 游戏事件类型枚举
/// 定义所有可能的事件类型，用于 EventBus 的订阅和发布
/// </summary>
public enum GameEventType
{
    // === 测试事件 ===
    TestEvent,              // 测试事件

    // === 输入事件（玩家操作）===

    /// <summary>
    /// 移动输入（WASD/摇杆）
    /// </summary>
    OnMoveInput,

    /// <summary>
    /// 移动取消（停止移动）
    /// </summary>
    OnMoveCanceled,

    // === 动作输入 ===

    /// <summary>
    /// 跳跃输入
    /// </summary>
    OnJumpInput,

    /// <summary>
    /// 攻击输入
    /// </summary>
    OnAttackInput,

    /// <summary>
    /// 交互输入（拾取物品、开门等）
    /// </summary>
    OnInteractInput,

    // === 瞄准/视角 ===

    /// <summary>
    /// 视角旋转输入（鼠标移动/右摇杆）
    /// </summary>
    OnLookInput,

    // === 系统/菜单 ===

    /// <summary>
    /// 暂停游戏
    /// </summary>
    OnPauseInput,

    #region 🔑 网络连接事件（新增）

    /// <summary>
    /// 成功连接到服务器
    /// </summary>
    OnConnected,

    /// <summary>
    /// 与服务器断开连接
    /// </summary>
    OnDisconnected,

    /// <summary>
    /// 连接超时
    /// </summary>
    OnConnectionTimeout,

    #endregion

    #region 🔑 登录认证事件（新增）

    /// <summary>
    /// 登录成功
    /// </summary>
    OnLoginSuccess,

    /// <summary>
    /// 登录失败
    /// </summary>
    OnLoginFailed,

    /// <summary>
    /// 登出（主动断开）
    /// </summary>
    OnLogout,

    #endregion

    #region 🔑 房间管理事件（新增）

    /// <summary>
    /// 房间创建成功
    /// </summary>
    OnRoomCreated,

    /// <summary>
    /// 房间创建失败
    /// </summary>
    OnRoomCreateFailed,

    /// <summary>
    /// 加入房间成功
    /// </summary>
    OnJoinedRoom,

    /// <summary>
    /// 加入房间失败
    /// </summary>
    OnJoinRoomFailed,

    /// <summary>
    /// 离开房间
    /// </summary>
    OnLeftRoom,

    /// <summary>
    /// 其他玩家加入房间（广播通知）
    /// </summary>
    OnPlayerJoinedRoom,

    /// <summary>
    /// 其他玩家离开房间（广播通知）
    /// </summary>
    OnPlayerLeftRoom,

    /// <summary>
    /// 房间列表更新
    /// </summary>
    OnRoomListUpdated,

    /// <summary>
    /// 房间状态变更
    /// </summary>
    OnRoomStateChanged,

    #endregion

    #region 🔑 帧同步事件

    // 注意：帧同步数据（S2C_FrameState）是高频消息，不走 EventBus，
    // 由 NetworkHandler 解析后直接调用 FrameSyncManager.OnServerFrame

    #endregion

    #region 🔑 战斗事件（新增）

    /// <summary>
    /// 远端玩家开火（收到 S2C_FireEvent 广播，低频走 EventBus）
    /// </summary>
    OnRemoteFired,

    #endregion

    #region 🔑 游戏控制事件（新增）

    /// <summary>
    /// 游戏开始（房主点击开始游戏）
    /// </summary>
    OnGameStart,

    /// <summary>
    /// 游戏结束
    /// </summary>
    OnGameOver,

    /// <summary>
    /// 场景加载完成
    /// </summary>
    OnSceneLoaded,

    #endregion
}
