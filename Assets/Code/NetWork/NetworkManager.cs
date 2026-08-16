using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using Google.Protobuf;
using GameNetwork;

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
