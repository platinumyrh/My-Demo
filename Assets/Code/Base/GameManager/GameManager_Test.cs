using UnityEngine;
using GameNetwork;
using System.Collections.Generic;
using System;

/// <summary>
/// GameManager_Test - 测试用游戏管理器
/// 负责网络初始化、房间创建/加入、远程玩家管理
/// 
/// 使用方法：
/// 1. 将此脚本挂载到场景中的空 GameObject 上
/// 2. 在 Inspector 中配置参数：
///    - Is Host Player: 勾选=房主，不勾=普通玩家
///    - Fixed Room Id: 写死的房间ID（普通玩家用）
///    - Remote Player Prefab: 拖入 RemotePlayer 预制体
/// 3. 运行游戏即可自动执行对应流程
/// </summary>
public class GameManager_Test : MonoBehaviour
{
    #region Inspector 配置字段

    [Header("网络配置")]
    [Tooltip("服务器IP地址")]
    public string serverIP = "127.0.0.1";
    
    [Tooltip("服务器端口")]
    public int serverPort = 7777;

    [Header("角色设置")]
    [Tooltip("是否为房主玩家（勾选=创建房间，不勾=加入房间）")]
    public bool isHostPlayer = true;

    [Header("房间配置")]
    [Tooltip("加入的房间ID（仅普通玩家使用，房主会自动生成）")]
    public string fixedRoomId = "TEST_ROOM_001";

    [Header("预制体引用")]
    [Tooltip("远程玩家的预制体（必须设置！）")]
    public GameObject remotePlayerPrefab;

    #endregion

    #region 内部状态

    /// <summary>
    /// 游戏状态枚举
    /// </summary>
    private enum GameState
    {
        Idle,           // 空闲状态
        Connecting,     // 连接中
        InLobby,        // 大厅/已加入房间
        Playing         // 游戏进行中
    }

    private GameState _currentState;
    private string _myRoomId;              // 当前房间ID
    private string _myPlayerId;             // 本地玩家ID
    
    /// <summary>
    /// 远程玩家对象列表
    /// </summary>
    private List<GameObject> _remotePlayers;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        // 确保单例模式（可选）
        if (FindObjectsOfType<GameManager_Test>().Length > 1)
        {
            Debug.LogWarning("[GM_Test] ⚠️ 已存在多个 GameManager_Test 实例，销毁多余的");
            Destroy(gameObject);
            return;
        }

        _currentState = GameState.Idle;
        _remotePlayers = new List<GameObject>();

        Debug.Log("[GM_Test] 🎮 GameManager_Test 初始化完成");
        Debug.Log($"[GM_Test]    配置: IP={serverIP}, Port={serverPort}, " +
                 $"IsHost={isHostPlayer}, RoomId={fixedRoomId}");
    }

    private void Start()
    {
        // 延迟一帧初始化网络（确保其他组件就绪）
        Invoke(nameof(InitializeNetwork), 0.5f);
    }

    private void Update()
    {
        // 可以在这里添加状态检查或调试信息显示
        if (_currentState == GameState.Playing && Input.GetKeyDown(KeyCode.P))
        {
            PrintStatus();
        }
    }

    private void OnDestroy()
    {
        // 清理远程玩家对象
        CleanupRemotePlayers();
        
        // 断开连接
        if (NetworkManager.Instance.IsConnected)
        {
            NetworkManager.Instance.Disconnect();
        }

        Debug.Log("[GM_Test] 🛑 GameManager_Test 销毁");
    }

    #endregion

    #region 初始化方法

    /// <summary>
    /// 初始化网络系统并开始流程
    /// </summary>
    private void InitializeNetwork()
    {
        Debug.Log("[GM_Test] ===============================");
        Debug.Log("[GM_Test] 🚀 开始初始化网络...");
        _currentState = GameState.Connecting;

        try
        {
            // 1. 配置 NetworkManager
            NetworkManager.Instance.serverIP = serverIP;
            NetworkManager.Instance.serverPort = serverPort;
            NetworkManager.Instance.Initialize();

            Debug.Log($"[GM_Test] ✅ NetworkManager 初始化完成");

            // 2. 连接服务器
            bool connected = NetworkManager.Instance.Connect();
            
            if (!connected)
            {
                Debug.LogError("[GM_Test] ❌ 连接服务器失败！请检查：");
                Debug.LogError("   1. 服务器是否启动？(dotnet run)");
                Debug.LogError("   2. IP和端口是否正确？");
                _currentState = GameState.Idle;
                return;
            }

            Debug.Log("[GM_Test] ✅ 连接服务器成功！");

            // 3. 注册消息处理器
            RegisterHandlers();
            Debug.Log("[GM_Test] ✅ 消息处理器注册完成");

            // 4. 根据角色分流
            _currentState = GameState.InLobby;
            
            if (isHostPlayer)
            {
                HostFlow_CreateRoom();
            }
            else
            {
                ClientFlow_JoinRoom();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 初始化异常: {ex.Message}\n{ex.StackTrace}");
            _currentState = GameState.Idle;
        }
    }

    /// <summary>
    /// 注册所有消息处理器
    /// </summary>
    private void RegisterHandlers()
    {
        var nm = NetworkManager.Instance;

        // === 心跳 ===
        nm.RegisterHandler(MsgID.S2CHeartBeat, OnHeartBeat);

        // === 房间管理 ===
        nm.RegisterHandler(MsgID.S2CCreateRoom, OnCreateRoomResult);
        nm.RegisterHandler(MsgID.S2CJoinRoom, OnJoinRoomResult);
        nm.RegisterHandler(MsgID.S2CRoomList, OnRoomList);

        // === 玩家上下线通知 ===
        nm.RegisterHandler(MsgID.S2CPlayerJoin, OnPlayerJoined);
        nm.RegisterHandler(MsgID.S2CPlayerLeave, OnPlayerLeave);

        // === 帧同步 ===
        nm.RegisterHandler(MsgID.S2CFrameState, OnFrameState);

        // === 游戏控制 ===
        nm.RegisterHandler(MsgID.S2CGameStart, OnGameStart);
        nm.RegisterHandler(MsgID.S2CGameOver, OnGameOver);

        Debug.Log($"[GM_Test] ✅ 已注册 {CountRegisteredHandlers()} 个消息处理器");
    }

    /// <summary>
    /// 统计注册的处理器数量（调试用）
    /// </summary>
    private int CountRegisteredHandlers()
    {
        return 10; // 心跳 + 房间(4) + 上下线(2) + 帧同步 + 游戏(2) = 10
    }

    #endregion

    #region 房主流程

    /// <summary>
    /// 房主流程：创建房间
    /// </summary>
    private void HostFlow_CreateRoom()
    {
        Debug.Log("[GM_Test] 👑 ========== 房主流程 ==========");
        Debug.Log("[GM_Test]    开始创建房间...");

        var req = new CreateRoomReq
        {
            RoomId = fixedRoomId,
            RoomName = $"TestRoom_{System.DateTime.Now:HHmmss}",
            Password = "" // 无密码
        };

        NetworkManager.Instance.Send(MsgID.C2SCreateRoom, req);
        Debug.Log("[GM_Test]    📤 已发送 C2SCreateRoom 请求");
    }

    #endregion

    #region 普通玩家流程

    /// <summary>
    /// 普通玩家流程：加入指定房间
    /// </summary>
    private void ClientFlow_JoinRoom()
    {
        Debug.Log("[GM_Test] 👥 ========== 客户端流程 ==========");
        Debug.Log($"[GM_Test]    尝试加入房间: {fixedRoomId}");

        var req = new JoinRoomReq
        {
            RoomId = fixedRoomId,
            Password = ""
        };

        NetworkManager.Instance.Send(MsgID.C2SJoinRoom, req);
        Debug.Log("[GM_Test]    📤 已发送 C2SJoinRoom 请求");
    }

    #endregion

    #region 消息处理器 - 房间管理

    /// <summary>
    /// 处理创建房间结果
    /// </summary>
    private void OnCreateRoomResult(byte[] data)
    {
        try
        {
            var resp = CreateRoomResp.Parser.ParseFrom(data);

            if (resp.Success)
            {
                _myRoomId = resp.RoomId;
                _myPlayerId = resp.PlayerId;
                Debug.Log($"[GM_Test] ✅ ========== 房间创建成功 ==========");
                Debug.Log($"[GM_Test]    房间ID: {_myRoomId}");
                Debug.Log($"[GM_Test]    我的玩家ID: {_myPlayerId}");

                // 房主创建成功后进入等待状态，等其他玩家加入后再开始游戏
            }
            else
            {
                Debug.LogError($"[GM_Test] ❌ 创建房间失败: {resp.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 解析 CreateRoomResp 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理加入房间结果
    /// </summary>
    private void OnJoinRoomResult(byte[] data)
    {
        try
        {
            var resp = JoinRoomResp.Parser.ParseFrom(data);

            if (resp.Success && resp.RoomInfo != null)
            {
                _myRoomId = resp.RoomId;
                _myPlayerId = resp.PlayerId;

                Debug.Log($"[GM_Test] ✅ ========== 加入房间成功 ==========");
                Debug.Log($"[GM_Test]    房间ID: {resp.RoomInfo.RoomId}");
                Debug.Log($"[GM_Test]    房间名: {resp.RoomInfo.RoomName}");
                Debug.Log($"[GM_Test]    房主: {resp.RoomInfo.HostId}");
                Debug.Log($"[GM_Test]    我的玩家ID: {_myPlayerId}");
                Debug.Log($"[GM_Test]    玩家数: {resp.RoomInfo.CurrentPlayers}/{resp.RoomInfo.MaxPlayers}");

                // 🎯 关键：初始化远程玩家
                InitializeRemotePlayers(resp.RoomInfo);
            }
            else
            {
                Debug.LogError($"[GM_Test] ❌ 加入房间失败: {resp.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 解析 JoinRoomResp 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理房间列表（备用）
    /// </summary>
    private void OnRoomList(byte[] data)
    {
        try
        {
            var resp = RoomListResp.Parser.ParseFrom(data);

            Debug.Log($"[GM_Test] 📋 ========== 房间列表 ==========");
            Debug.Log($"[GM_Test]    共 {resp.Rooms.Count} 个房间:");

            foreach (var room in resp.Rooms)
            {
                Debug.Log($"[GM_TEST]    [{room.RoomId}] {room.RoomName} " +
                             $"({room.CurrentPlayers}/{room.MaxPlayers}) " +
                             $"{(room.IsPlaying ? "游戏中" : "等待中")}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 解析 RoomListResp 异常: {ex.Message}");
        }
    }

    #endregion

    #region 消息处理器 - 玩家上下线

    /// <summary>
    /// 处理新玩家加入通知
    /// </summary>
    private void OnPlayerJoined(byte[] data)
    {
        try
        {
            var notify = PlayerJoinNotify.Parser.ParseFrom(data);

            Debug.Log($"[GM_Test] 👋 ========== 新玩家加入 ==========");
            Debug.Log($"[GM_Test]    玩家名: {notify.Player?.PlayerName ?? "未知"}");
            Debug.Log($"[GM_Test]    玩家ID: {notify.Player?.PlayerId ?? "未知"}");

            // 如果在 Lobby 状态且还没正式开始游戏，可以在这里也创建
            if (_currentState == GameState.InLobby)
            {
                SpawnRemotePlayer(notify.Player, _remotePlayers.Count);

                // 房主：有玩家加入后开始游戏
                if (isHostPlayer)
                {
                    Debug.Log("[GM_Test] 👑 房主检测到玩家加入，开始游戏...");
                    NetworkManager.Instance.Send(MsgID.C2SStartGame, new StartGameReq());
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 处理 PlayerJoinNotify 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理玩家离开通知
    /// </summary>
    private void OnPlayerLeave(byte[] data)
    {
        try
        {
            var notify = PlayerLeaveNotify.Parser.ParseFrom(data);

            Debug.Log($"[GM_Test] 👋 ========== 玩家离开 ==========");
            Debug.Log($"[GM_Test]    离开玩家ID: {notify.PlayerId}");

            // 销查并移除对应的远程玩家对象
            RemoveRemotePlayer(notify.PlayerId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 处理 PlayerLeaveNotify 异常: {ex.Message}");
        }
    }

    #endregion

    #region 消息处理器 - 帧同步

    /// <summary>
    /// 处理帧状态数据
    /// </summary>
    private void OnFrameState(byte[] data)
    {
        try
        {
            var frameState = FrameStateResp.Parser.ParseFrom(data);

            // 只在游戏进行中处理帧同步
            if (_currentState == GameState.Playing)
            {
                // 更新所有远程玩家
                foreach (var remoteGO in _remotePlayers)
                {
                    if (remoteGO == null) continue;

                    var movement = remoteGO.GetComponent<RemotePlayerMovement>();
                    if (movement != null)
                    {
                        movement.OnReceiveFrameState(frameState);
                    }
                }
            }

            // 调试日志（每100帧输出一次）
            if (frameState.FrameId % 100 == 0)
            {
                Debug.Log($"[GM_Test] 🎮 帧 #{frameState.FrameId}, " +
                         $"输入数: {frameState.PlayerInputs.Count}, " +
                         $"远程玩家: {_remotePlayers.Count}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 处理 FrameStateResp 异常: {ex.Message}");
        }
    }

    #endregion

    #region 消息处理器 - 其他

    /// <summary>
    /// 处理心跳响应
    /// </summary>
    private void OnHeartBeat(byte[] data)
    {
        try
        {
            var resp = HeartBeatResp.Parser.ParseFrom(data);
            long ping = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - resp.ClientTime;

            // TODO: 可以在 UI 上显示延迟
            // PingText.text = $"Ping: {ping}ms";

            if (ping > 200)
            {
                Debug.LogWarning($"[GM_Test] ⚠️ 高延迟: {ping}ms");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 处理 HeartBeatResp 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理游戏开始通知
    /// </summary>
    private void OnGameStart(byte[] data)
    {
        try
        {
            var notify = GameStartNotify.Parser.ParseFrom(data);

            Debug.Log($"[GM_Test] 🎮 ========== 游戏开始！===========");
            Debug.Log($"[GM_Test]    房间: {notify.RoomId}");

            _currentState = GameState.Playing;

            // TODO: 可以在这里加载游戏场景或启用输入
            EnableInputForAllPlayers();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 处理 GameStartNotify 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理游戏结束通知
    /// </summary>
    private void OnGameOver(byte[] data)
    {
        try
        {
            var notify = GameOverNotify.Parser.ParseFrom(data);

            Debug.Log($"[GM_Test] 🏁 ========== 游戏结束 ==========");
            Debug.Log($"[GM_Test]    胜利者: {notify.WinnerId}");
            Debug.Log($"[GM_Test]    原因: {notify.Reason}");

            _currentState = GameState.InLobby;

            // TODO: 显示结果界面
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GM_Test] ❌ 处理 GameOverNotify 异常: {ex.Message}");
        }
    }

    #endregion

    #region 远程玩家管理

    /// <summary>
    /// 从房间信息初始化所有远程玩家（加入房间时调用）
    /// </summary>
    private void InitializeRemotePlayers(RoomInfo roomInfo)
    {
        Debug.Log($"[GM_Test] 🎭 ========== 初始化远程玩家 ==========");

        if (roomInfo.Players == null || roomInfo.Players.Count == 0)
        {
            Debug.LogWarning("[GM_Test] ⚠️ 房间内没有其他玩家");
            return;
        }

        // 本地玩家ID已在加入/创建房间响应中由服务端分配
        Debug.Log($"[GM_Test]    本地玩家ID: {_myPlayerId}");

        int spawnIndex = 0;

        foreach (var playerInfo in roomInfo.Players)
        {
            // 跳过自己
            if (playerInfo.PlayerId == _myPlayerId)
            {
                Debug.Log($"[GM_Test]    ⏭️ 跳过自己 ({playerInfo.PlayerName})");
                continue;
            }

            // 创建远程玩家
            SpawnRemotePlayer(playerInfo, spawnIndex++);
        }

        Debug.Log($"[GM_Test] ✅ 远程玩家初始化完成，共 {_remotePlayers.Count} 人");
    }

    /// <summary>
    /// 实例化单个远程玩家预制体
    /// </summary>
    private void SpawnRemotePlayer(PlayerInfo playerInfo, int index)
    {
        // 检查预制体是否设置
        if (remotePlayerPrefab == null)
        {
            Debug.LogError("[GM_Test] ❌ RemotePlayer 预制体未设置！请在 Inspector 中拖入！");
            return;
        }

        // 实例化
        GameObject remoteGO = Instantiate(remotePlayerPrefab);
        
        // 设置出生位置（圆形分布）
        Vector3 spawnPos = GetSpawnPosition(index);
        remoteGO.transform.position = spawnPos;

        // 设置脚本属性
        var remoteMovement = remoteGO.GetComponent<RemotePlayerMovement>();
        if (remoteMovement != null)
        {
            remoteMovement.SetPlayerId(playerInfo.PlayerId);
            remoteMovement.SetPosition(spawnPos);
        }

        // 保存到列表
        _remotePlayers.Add(remoteGO);

        // 设置名称便于识别
        remoteGO.name =  $"Remote_{playerInfo.PlayerName}_{playerInfo.PlayerId}";

        Debug.Log($"[GM_Test]    ➕ 创建远程玩家: {playerInfo.PlayerName}" +
                     $" (ID:{playerInfo.PlayerId}) at {spawnPos}");
    }

    /// <summary>
    /// 移除指定的远程玩家
    /// </summary>
    private void RemoveRemotePlayer(string playerId)
    {
        for (int i = _remotePlayers.Count - 1; i >= 0; i--)
        {
            GameObject remoteGO = _remotePlayers[i];
            if (remoteGO == null) continue;

            var movement = remoteGO.GetComponent<RemotePlayerMovement>();
            if (movement != null && movement.playerId == playerId)
            {
                Debug.Log($"[GM_Test]    💔 销毁远程玩家: {playerId}");
                Destroy(remoteGO);
                _remotePlayers.RemoveAt(i);
                return;
            }
        }

        Debug.LogWarning($"[GM_Test] ⚠️ 未找到玩家 {playerId} 的远程对象");
    }

    /// <summary>
    /// 清理所有远程玩家
    /// </summary>
    private void CleanupRemotePlayers()
    {
        foreach (var go in _remotePlayers)
        {
            if (go != null) Destroy(go);
        }
        _remotePlayers.Clear();
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 获取出生位置（圆形分布）
    /// </summary>
    private Vector3 GetSpawnPosition(int index)
    {
        // 最多支持8个玩家均匀分布
        float angle = index * (360f / 8f) * Mathf.Deg2Rad;
        float radius = 5f; // 出生半径5米

        return new Vector3(
            Mathf.Cos(angle) * radius,
            0f,
            Mathf.Sin(angle) * radius
        );
    }

    /// <summary>
    /// 启用所有玩家的输入控制
    /// </summary>
    private void EnableInputForAllPlayers()
    {
        // 启用本地玩家的输入
        var localInput = FindObjectOfType<PlayerInputController>();
        if (localInput != null)
        {
            localInput.enabled = true;
            Debug.Log("[GM_Test] ✅ 本地玩家输入已启用");
        }

        // 远程玩家的输入由 RemotePlayerMovement 的 HandleActions 处理
        Debug.Log($"[GM_Test] ✅ 共激活 {_remotePlayers.Count + 1} 个玩家");
    }

    /// <summary>
    /// 打印当前状态（调试用）
    /// </summary>
    [ContextMenu("GM Test/Print Status")]
    public void PrintStatus()
    {
        Debug.Log($"[GM_Test] ===============================");
        Debug.Log($"[GM_Test] 状态: {_currentState}");
        Debug.Log($"[GM_Test] 本地玩家ID: {_myPlayerId ?? "未分配"}");
        Debug.Log($"[GM_Test] 房间ID: {_myRoomId ?? "未加入"}");
        Debug.Log($"[GM_Test] 是否房主: {isHostPlayer}");
        Debug.Log($"[GM_Test] 远程玩家数: {_remotePlayers?.Count ?? 0}");
        Debug.Log($"[GM_Test] 网络连接: {NetworkManager.Instance.IsConnected}");
        Debug.Log($"[GM_Test] ===============================");
    }

    #endregion
}
