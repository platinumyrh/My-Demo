﻿using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 玩家管理器
/// 负责管理所有玩家对象（本地玩家 + 远程玩家）
/// 通过 EventBus 订阅网络事件，创建/销毁玩家
/// 
/// 🔑 继承 BaseManager 实现单例模式
/// </summary>
public class PlayerManager : BaseManager<PlayerManager>
{
    #region Inspector 配置（通过 GameInitializer 设置）

    /// <summary>
    /// 本地玩家预制体
    /// </summary>
    public GameObject LocalPlayerPrefab { get; set; }

    /// <summary>
    /// 远程玩家预制体
    /// </summary>
    public GameObject RemotePlayerPrefab { get; set; }

    /// <summary>
    /// 本地玩家出生位置
    /// </summary>
    public Vector3 LocalSpawnPosition { get; set; } = Vector3.zero;

    /// <summary>
    /// 远程玩家出生半径（圆形分布）
    /// </summary>
    public float RemoteSpawnRadius { get; set; } = 5f;

    #endregion

    #region 私有字段

    // 远程玩家字典 (playerId → RemotePlayerMovement)
    private Dictionary<string, RemotePlayerMovement> _remotePlayers = new Dictionary<string, RemotePlayerMovement>();

    // 本地玩家引用
    private LocalPlayerMovement _localPlayer;

    // 是否已初始化
    private bool _isInitialized = false;

    // 使用 MonoManager 来运行协程

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化（由 GameInitializer 调用）
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        SubscribeToEventBus();

        _isInitialized = true;
        Debug.Log("[PlayerManager] ✅ 初始化完成");
    }

    /// <summary>
    /// 订阅 EventBus 事件
    /// </summary>
    private void SubscribeToEventBus()
    {
        var eventBus = GameEventBus.GetInstance();

        // 其他玩家加入房间 → 创建远程玩家
        eventBus.Subscribe<PlayerJoinedRoomEventData>(
            GameEventType.OnPlayerJoinedRoom,
            OnRemotePlayerJoined
        );

        // 其他玩家离开房间 → 销毁远程玩家
        eventBus.Subscribe<PlayerLeftRoomEventData>(
            GameEventType.OnPlayerLeftRoom,
            OnRemotePlayerLeft
        );

        // 帧同步更新 → 更新所有远程玩家位置
        eventBus.Subscribe<FrameSyncUpdateEventData>(
            GameEventType.OnFrameSyncUpdate,
            OnFrameSyncUpdate
        );

        Debug.Log("[PlayerManager] 📡 已订阅 EventBus 事件");
    }

    /// <summary>
    /// 取消 EventBus 订阅
    /// </summary>
    public void UnsubscribeFromEventBus()
    {
        var eventBus = GameEventBus.GetInstance();

        eventBus.Unsubscribe<PlayerJoinedRoomEventData>(
            GameEventType.OnPlayerJoinedRoom,
            OnRemotePlayerJoined
        );

        eventBus.Unsubscribe<PlayerLeftRoomEventData>(
            GameEventType.OnPlayerLeftRoom,
            OnRemotePlayerLeft
        );

        eventBus.Unsubscribe<FrameSyncUpdateEventData>(
            GameEventType.OnFrameSyncUpdate,
            OnFrameSyncUpdate
        );
    }

    #endregion

    #region EventBus 事件处理器

    /// <summary>
    /// 其他玩家加入 → 创建远程玩家
    /// </summary>
    private void OnRemotePlayerJoined(PlayerJoinedRoomEventData eventData)
    {
        Debug.Log($"[PlayerManager] 👋 远程玩家加入: {eventData.PlayerName} ({eventData.PlayerId})");

        CreateRemotePlayer(eventData.PlayerId, eventData.PlayerName);
    }

    /// <summary>
    /// 其他玩家离开 → 销毁远程玩家
    /// </summary>
    private void OnRemotePlayerLeft(PlayerLeftRoomEventData eventData)
    {
        Debug.Log($"[PlayerManager] 👋 远程玩家离开: {eventData.PlayerId}");

        RemoveRemotePlayer(eventData.PlayerId);
    }

    /// <summary>
    /// 帧同步更新 → 更新远程玩家位置
    /// </summary>
    private void OnFrameSyncUpdate(FrameSyncUpdateEventData eventData)
    {
        // 这里可以处理帧同步数据
        // 实际的位置更新由 RemotePlayerMovement 自己处理
    }

    #endregion

    #region 玩家创建/销毁

    /// <summary>
    /// 创建本地玩家
    /// </summary>
    public LocalPlayerMovement CreateLocalPlayer(string playerId, string playerName)
    {
        // 如果已存在，先销毁
        if (_localPlayer != null)
        {
            Debug.LogWarning("[PlayerManager] ⚠️ 本地玩家已存在，销毁旧对象");
            Object.Destroy(_localPlayer.gameObject);
        }

        if (LocalPlayerPrefab == null)
        {
            Debug.LogError("[PlayerManager] ❌ 本地玩家预制体未设置！请在 GameInitializer 中配置");
            return null;
        }

        // 实例化
        GameObject playerObj = Object.Instantiate(LocalPlayerPrefab, LocalSpawnPosition, Quaternion.identity);
        playerObj.name = $"LocalPlayer_{playerId}";

        // 获取组件并初始化
        _localPlayer = playerObj.GetComponent<LocalPlayerMovement>();
        if (_localPlayer != null)
        {
            _localPlayer.Initialize(playerId, playerName);
            Debug.Log($"[PlayerManager] ✅ 本地玩家创建完成: {playerName}");
        }
        else
        {
            Debug.LogWarning("[PlayerManager] ⚠️ 预制体上没有 LocalPlayerMovement 组件");
        }

        return _localPlayer;
    }

    /// <summary>
    /// 创建远程玩家
    /// </summary>
    public RemotePlayerMovement CreateRemotePlayer(string playerId, string playerName)
    {
        // 检查是否已存在
        if (_remotePlayers.ContainsKey(playerId))
        {
            Debug.LogWarning($"[PlayerManager] ⚠️ 远程玩家 {playerId} 已存在");
            return _remotePlayers[playerId];
        }

        if (RemotePlayerPrefab == null)
        {
            Debug.LogError("[PlayerManager] ❌ 远程玩家预制体未设置！请在 GameInitializer 中配置");
            return null;
        }

        // 计算出生位置（圆形分布）
        int index = _remotePlayers.Count;
        float angle = index * (360f / 8f) * Mathf.Deg2Rad;
        Vector3 spawnPos = new Vector3(
            Mathf.Cos(angle) * RemoteSpawnRadius,
            0,
            Mathf.Sin(angle) * RemoteSpawnRadius
        );

        // 实例化
        GameObject playerObj = Object.Instantiate(RemotePlayerPrefab, spawnPos, Quaternion.identity);
        playerObj.name = $"RemotePlayer_{playerName}_{playerId}";

        // 获取组件并初始化
        var remoteMovement = playerObj.GetComponent<RemotePlayerMovement>();
        if (remoteMovement != null)
        {
            remoteMovement.SetPlayerId(playerId);
            remoteMovement.SetPosition(spawnPos);

            _remotePlayers[playerId] = remoteMovement;

            Debug.Log($"[PlayerManager] ✅ 远程玩家创建完成: {playerName} (共 {_remotePlayers.Count} 人)");
        }
        else
        {
            Debug.LogWarning("[PlayerManager] ⚠️ 预制体上没有 RemotePlayerMovement 组件");
        }

        return remoteMovement;
    }

    /// <summary>
    /// 移除远程玩家
    /// </summary>
    public void RemoveRemotePlayer(string playerId)
    {
        if (_remotePlayers.TryGetValue(playerId, out var remotePlayer))
        {
            if (remotePlayer != null && remotePlayer.gameObject != null)
            {
                Object.Destroy(remotePlayer.gameObject);
            }

            _remotePlayers.Remove(playerId);
            Debug.Log($"[PlayerManager] 💔 远程玩家移除: {playerId} (剩余 {_remotePlayers.Count} 人)");
        }
        else
        {
            Debug.LogWarning($"[PlayerManager] ⚠️ 找不到远程玩家: {playerId}");
        }
    }

    /// <summary>
    /// 清除所有远程玩家
    /// </summary>
    public void ClearAllRemotePlayers()
    {
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value != null && kvp.Value.gameObject != null)
            {
                Object.Destroy(kvp.Value.gameObject);
            }
        }

        _remotePlayers.Clear();
        Debug.Log("[PlayerManager] 🧹 已清除所有远程玩家");
    }

    #endregion

    #region 查询方法

    /// <summary>
    /// 获取本地玩家
    /// </summary>
    public LocalPlayerMovement GetLocalPlayer()
    {
        return _localPlayer;
    }

    /// <summary>
    /// 获取远程玩家
    /// </summary>
    public RemotePlayerMovement GetRemotePlayer(string playerId)
    {
        _remotePlayers.TryGetValue(playerId, out var player);
        return player;
    }

    /// <summary>
    /// 获取所有远程玩家
    /// </summary>
    public Dictionary<string, RemotePlayerMovement> GetAllRemotePlayers()
    {
        return new Dictionary<string, RemotePlayerMovement>(_remotePlayers);
    }

    /// <summary>
    /// 获取远程玩家数量
    /// </summary>
    public int GetRemotePlayerCount()
    {
        return _remotePlayers.Count;
    }

    /// <summary>
    /// 检查是否有指定玩家
    /// </summary>
    public bool HasPlayer(string playerId)
    {
        return _remotePlayers.ContainsKey(playerId);
    }

    #endregion

    #region 调试方法

    /// <summary>
    /// 打印当前状态
    /// </summary>
    public void PrintStatus()
    {
        Debug.Log("========== PlayerManager Status ==========");
        Debug.Log($"Local Player: {(_localPlayer != null ? _localPlayer.name : "None")}");
        Debug.Log($"Remote Players: {_remotePlayers.Count}");

        foreach (var kvp in _remotePlayers)
        {
            Debug.Log($"  - {kvp.Key}: {kvp.Value?.name}");
        }

        Debug.Log("==========================================");
    }

    #endregion
}
