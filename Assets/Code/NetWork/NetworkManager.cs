using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using Google.Protobuf;
using GameNetwork;           // 引用 Proto 消息命名空间
// 🔑 注意：GameEventBus 是类名（不是命名空间），GameEventType 和 GameEventData 在全局命名空间
// 所以不需要 using GameEventBus; 直接写类名即可

/// <summary>
/// 客户端网络管理器
/// 负责与服务器通信（UDP + Protobuf）
/// </summary>
public class NetworkManager : MonoBehaviour
{
    #region 单例模式

    private static NetworkManager _instance;
    public static NetworkManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("NetworkManager");
                _instance = go.AddComponent<NetworkManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    #endregion

    #region 配置参数

    [Header("服务器配置")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 7777;
    
    [Header("心跳配置")]
    public float heartBeatInterval = 2.0f; // 心跳间隔（秒）

    #endregion

    #region 私有字段

    private UdpClient _udpClient;
    private IPEndPoint _serverEndpoint;
    private bool _isConnected;
    private float _heartBeatTimer;

    // 消息回调字典
    private Dictionary<MsgID, Action<byte[]>> _messageHandlers;

    // 🔑 本地玩家信息（登录后赋值）
    private string _localPlayerId = "";
    private string _localPlayerName = "";
    private string _sessionToken = "";

    // 登录状态
    private bool _isLoggedIn = false;

    #endregion

    #region 事件回调

    /// <summary>
    /// 登录成功事件 (userId, nickname)
    /// </summary>
    public event Action<string, string> OnLoginSuccess;

    /// <summary>
    /// 登录失败事件 (errorMessage)
    /// </summary>
    public event Action<string> OnLoginFailed;

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
            return;
        }

        Initialize();
    }

    private void Update()
    {
        if (!_isConnected) return;

        // 心跳检测
        _heartBeatTimer += Time.deltaTime;
        if (_heartBeatTimer >= heartBeatInterval)
        {
            SendHeartBeat();
            _heartBeatTimer = 0f;
        }

        // 接收数据（非阻塞）
        ReceiveData();
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }

    #endregion

    #region 公开方法 - 连接管理

    /// <summary>
    /// 初始化网络管理器
    /// </summary>
    public void Initialize()
    {
        _messageHandlers = new Dictionary<MsgID, Action<byte[]>>();
        _serverEndpoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);
        
        Debug.Log($"[NetworkManager] 初始化完成，目标服务器: {serverIP}:{serverPort}");
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public bool Connect()
    {
        if (_isConnected && _udpClient != null)
        {
            Debug.Log("[NetworkManager] 已连接，忽略重复连接");
            return true;
        }

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Connect(_serverEndpoint);
            _isConnected = true;

            Debug.Log($"[NetworkManager] ✅ 已连接到服务器: {serverIP}:{serverPort}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] ❌ 连接失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        _isConnected = false;
        _udpClient?.Close();
        _udpClient = null;
        
        Debug.Log("[NetworkManager] 已断开连接");
    }

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// 是否已登录
    /// </summary>
    public bool IsLoggedIn => _isLoggedIn;

    /// <summary>
    /// 🔑 获取本地玩家ID（供其他组件调用，如 NetworkHandler 区分本地/远程玩家）
    /// </summary>
    public string GetLocalPlayerId()
    {
        return _localPlayerId;
    }

    /// <summary>
    /// 获取本地玩家昵称
    /// </summary>
    public string GetLocalPlayerName()
    {
        return _localPlayerName;
    }

    #endregion

    #region 公开方法 - 登录认证

    /// <summary>
    /// 发送登录请求到服务器
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="password">密码（明文，服务端会进行MD5哈希验证）</param>
    public void Login(string username, string password)
    {
        if (!IsConnected)
        {
            Debug.LogError("[NetworkManager] 未连接服务器，无法登录");
            OnLoginFailed?.Invoke("未连接到服务器");
            return;
        }

        if (_isLoggedIn)
        {
            Debug.LogWarning("[NetworkManager] 已经登录，请勿重复登录");
            return;
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Debug.LogWarning("[NetworkManager] 用户名或密码不能为空");
            OnLoginFailed?.Invoke("用户名或密码不能为空");
            return;
        }

        try
        {
            var req = new LoginReq
            {
                Username = username,
                Password = password  // 明文传输，服务端会做MD5哈希验证
            };

            Send(MsgID.C2SLogin, req);

            Debug.Log($"[NetworkManager] 📤 发送登录请求: {username}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] ❌ 发送登录请求失败: {ex.Message}");
            OnLoginFailed?.Invoke("发送登录请求失败");
        }
    }

    /// <summary>
    /// 处理服务器返回的登录响应（由外部调用或注册回调后自动触发）
    /// 🔑 核心改动：通过 EventBus 发布事件，实现模块解耦
    /// </summary>
    /// <param name="data">Protobuf序列化的 LoginResp 数据</param>
    public void OnLoginResponse(byte[] data)
    {
        try
        {
            var resp = LoginResp.Parser.ParseFrom(data);

            if (resp.Success)
            {
                // ✅ 登录成功：保存本地玩家信息（纯数据，不依赖任何GameObject）
                _localPlayerId = resp.UserId;        // 👈 关键！保存服务端分配的唯一ID
                _localPlayerName = resp.Username;     // 保存昵称
                _sessionToken = resp.Token;           // 保存会话令牌
                _isLoggedIn = true;

                Debug.Log($"✅ [NetworkManager] 登录成功！");
                Debug.Log($"   👤 用户ID: {_localPlayerId}");
                Debug.Log($"   😊 昵称: {_localPlayerName}");
                Debug.Log($"   🔑 Token: {(_sessionToken.Length > 8 ? _sessionToken.Substring(0, 8) + "..." : _sessionToken)}");

                // ============================================
                // 🔑🔑🔑 关键改动：发布事件到 EventBus（而非直接调用委托）
                // ============================================
                
                // 1️⃣ 创建事件数据对象
                var loginEventData = LoginSuccessEventData.Create(
                    userId: _localPlayerId,
                    username: _localPlayerName,
                    token: _sessionToken,
                    expireTime: resp.ExpireTime
                );

                // 2️⃣ 发布登录成功事件到总线（所有订阅者都会收到）
                GameEventBus.GetInstance().Publish(
                    GameEventType.OnLoginSuccess, 
                    loginEventData
                );

                Debug.Log($"[NetworkManager] 📢 已发布 OnLoginSuccess 事件到 EventBus: {loginEventData}");

                // 3️⃣ （兼容性保留）同时触发旧的委托事件（后续可移除）
                OnLoginSuccess?.Invoke(_localPlayerId, _localPlayerName);
            }
            else
            {
                // ❌ 登录失败
                Debug.LogError($"❌ [NetworkManager] 登录失败: {resp.Message}");

                // 🔑 登录失败时断开连接，停止心跳
                Disconnect();
                Debug.Log("[NetworkManager] 🔌 登录失败，已断开连接并停止心跳");

                // 发布登录失败事件到 EventBus
                var failedEventData = LoginFailedEventData.Create(resp.Message);
                GameEventBus.GetInstance().Publish(
                    GameEventType.OnLoginFailed,
                    failedEventData
                );

                // （兼容性保留）旧的委托事件
                OnLoginFailed?.Invoke(resp.Message);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [NetworkManager] 处理登录响应异常: {ex.Message}");
            
            // 发布异常事件
            var errorEventData = LoginFailedEventData.Create("响应解析失败");
            GameEventBus.GetInstance().Publish(
                GameEventType.OnLoginFailed,
                errorEventData
            );
            
            OnLoginFailed?.Invoke("响应解析失败");
        }
    }

    /// <summary>
    /// 登出（清除本地玩家信息）
    /// </summary>
    public void Logout()
    {
        _localPlayerId = "";
        _localPlayerName = "";
        _sessionToken = "";
        _isLoggedIn = false;

        Debug.Log("[NetworkManager] 已登出");
    }

    #endregion

    #region 公开方法 - 消息发送

    /// <summary>
    /// 发送消息（使用反射/泛型）
    /// </summary>
    public void Send<T>(MsgID msgId, T message) where T : IMessage<T>
    {
        if (!_isConnected || _udpClient == null)
        {
            Debug.LogWarning("[NetworkManager] 未连接，无法发送消息");
            return;
        }

        try
        {
            // 序列化消息
            byte[] protoData = SerializeMessage(message);
            
            // 构建协议包：MsgID(4字节) + Length(4字节) + Data
            byte[] msgIdBytes = BitConverter.GetBytes((int)msgId);
            byte[] lengthBytes = BitConverter.GetBytes(protoData.Length);

            byte[] sendData = new byte[8 + protoData.Length];
            Buffer.BlockCopy(msgIdBytes, 0, sendData, 0, 4);
            Buffer.BlockCopy(lengthBytes, 0, sendData, 4, 4);
            Buffer.BlockCopy(protoData, 0, sendData, 8, protoData.Length);

            // 发送
            _udpClient.Send(sendData, sendData.Length);
            
            Debug.Log($"[NetworkManager] 发送消息: {msgId}, 大小: {sendData.Length} bytes");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] ❌ 发送失败 [{msgId}]: {ex.Message}");
        }
    }

    /// <summary>
    /// 注册消息处理器
    /// </summary>
    public void RegisterHandler(MsgID msgId, Action<byte[]> handler)
    {
        if (_messageHandlers.ContainsKey(msgId))
        {
            _messageHandlers[msgId] = handler;
        }
        else
        {
            _messageHandlers.Add(msgId, handler);
        }
    }

    /// <summary>
    /// 移除消息处理器
    /// </summary>
    public void UnregisterHandler(MsgID msgId)
    {
        _messageHandlers.Remove(msgId);
    }

    /// <summary>
    /// 发送玩家输入到服务器（帧同步用）
    /// </summary>
    public void SendPlayerInput(GameNetwork.PlayerInput input)
    {
        Send(MsgID.C2SPlayerInput, input);
    }

    /// <summary>
    /// 请求房间列表
    /// </summary>
    public void SendGetRoomList()
    {
        if (!_isLoggedIn)
        {
            Debug.LogWarning("[NetworkManager] 未登录，无法获取房间列表");
            return;
        }

        var req = new GetRoomListReq();
        Send(MsgID.C2SGetRoomList, req);
        Debug.Log("[NetworkManager] 📤 发送获取房间列表请求");
    }

    /// <summary>
    /// 创建房间
    /// </summary>
    /// <param name="roomName">房间名称</param>
    /// <param name="password">房间密码（空表示无密码）</param>
    public void SendCreateRoom(string roomName = "", string password = "")
    {
        if (!_isLoggedIn)
        {
            Debug.LogWarning("[NetworkManager] 未登录，无法创建房间");
            return;
        }

        var req = new CreateRoomReq
        {
            RoomName = roomName,
            Password = password
        };
        Send(MsgID.C2SCreateRoom, req);
        Debug.Log($"[NetworkManager] 📤 发送创建房间请求: {roomName}");
    }

    #endregion

    #region 私有方法 - 核心功能

    /// <summary>
    /// 发送心跳
    /// </summary>
    private void SendHeartBeat()
    {
        var req = new HeartBeatReq
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        Send(MsgID.C2SHeartBeat, req);
    }

    /// <summary>
    /// 接收数据（非阻塞）
    /// </summary>
    private void ReceiveData()
    {
        if (!_isConnected || _udpClient == null || _udpClient.Available <= 0) return;

        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = _udpClient.Receive(ref remoteEP);
            
            ProcessReceivedData(data);
        }
        catch (Exception)
        {
            // UDP接收异常通常可以忽略
            // 空捕获：避免 CS0168 警告（未使用的变量 ex）
        }
    }

    /// <summary>
    /// 处理接收到的数据
    /// </summary>
    private void ProcessReceivedData(byte[] data)
    {
        try
        {
            if (data.Length < 8) return;

            int msgId = BitConverter.ToInt32(data, 0);
            int length = BitConverter.ToInt32(data, 4);

            if (length < 0 || data.Length < 8 + length) return;

            byte[] payload = new byte[length];
            Buffer.BlockCopy(data, 8, payload, 0, length);

            // 分发到对应的处理器
            MsgID messageId = (MsgID)msgId;
            if (_messageHandlers.TryGetValue(messageId, out var handler))
            {
                handler?.Invoke(payload);
            }
            else
            {
                Debug.Log($"[NetworkManager] 收到未处理的消息: {messageId}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] ❌ 处理接收数据异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 序列化消息（简化版）
    /// </summary>
    /// <summary>
    /// 序列化 Protobuf 消息
    /// </summary>
    private byte[] SerializeMessage<T>(T message) where T : IMessage<T>
    {
        // 使用 Google.Protobuf 的 ToByteArray() 方法序列化
        return message.ToByteArray();
    }

    #endregion
}
