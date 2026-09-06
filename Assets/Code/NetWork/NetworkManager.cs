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
/// 客户端网络管理器（纯传输层）
/// 只负责：UDP 连接、心跳、协议打包/拆包、消息字节分发
/// 不解析任何具体业务消息、不发布业务事件——
/// 收到的消息字节由 NetworkHandler 解析后走 EventBus 或直调对应 Manager
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
        // 防止场景里误放的重复实例销毁时断开真身的连接
        if (_instance != this) return;

        Disconnect();
    }

    private void OnApplicationQuit()
    {
        if (_instance != this) return;

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
    /// 返回 false 表示请求根本没发出去（未连接/重复登录/参数为空），
    /// 服务器响应结果统一由 NetworkHandler 解析后发布 EventBus 事件
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="password">密码（明文，服务端会进行MD5哈希验证）</param>
    public bool Login(string username, string password)
    {
        if (!IsConnected)
        {
            Debug.LogError("[NetworkManager] 未连接服务器，无法登录");
            return false;
        }

        if (_isLoggedIn)
        {
            Debug.LogWarning("[NetworkManager] 已经登录，请勿重复登录");
            return true;
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Debug.LogWarning("[NetworkManager] 用户名或密码不能为空");
            return false;
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
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] ❌ 发送登录请求失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 记录登录成功后的会话信息（由 NetworkHandler 在收到 S2C_Login 成功响应后调用）
    /// NetworkManager 只保存状态，不做解析、不发事件
    /// </summary>
    public void ApplyLoginSuccess(string userId, string username, string token)
    {
        _localPlayerId = userId;
        _localPlayerName = username;
        _sessionToken = token;
        _isLoggedIn = true;
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
