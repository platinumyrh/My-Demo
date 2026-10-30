using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 玩家表现层生命周期管理：只负责根据房间成员创建/销毁 PlayerView。
/// 位置、血量、开火和死亡全部由 lockstep 逻辑世界 + LockstepGameDriver 驱动。
/// </summary>
public class PlayerManager : BaseManager<PlayerManager>
{
    public GameObject LocalPlayerPrefab { get; set; }
    public GameObject RemotePlayerPrefab { get; set; }
    public Vector3 LocalSpawnPosition { get; set; } = Vector3.zero;
    public float RemoteSpawnRadius { get; set; } = 5f;

    private readonly Dictionary<string, PlayerView> _remotePlayers = new();
    private readonly Dictionary<string, string> _playerNames = new();
    private PlayerView _localPlayer;
    private bool _subscribedToEventBus;

    public void Initialize()
    {
        if (_subscribedToEventBus) return;
        SubscribeToEventBus();
        _subscribedToEventBus = true;
        Debug.Log("[PlayerManager] ✅ 初始化完成");
    }

    private void SubscribeToEventBus()
    {
        var eventBus = GameEventBus.GetInstance();
        eventBus.Subscribe<PlayerJoinedRoomEventData>(GameEventType.OnPlayerJoinedRoom, OnRemotePlayerJoined);
        eventBus.Subscribe<PlayerLeftRoomEventData>(GameEventType.OnPlayerLeftRoom, OnRemotePlayerLeft);
        Debug.Log("[PlayerManager] 📡 已订阅房间成员事件");
    }

    public void UnsubscribeFromEventBus()
    {
        if (!_subscribedToEventBus) return;
        var eventBus = GameEventBus.GetInstance();
        eventBus.Unsubscribe<PlayerJoinedRoomEventData>(GameEventType.OnPlayerJoinedRoom, OnRemotePlayerJoined);
        eventBus.Unsubscribe<PlayerLeftRoomEventData>(GameEventType.OnPlayerLeftRoom, OnRemotePlayerLeft);
        _subscribedToEventBus = false;
    }

    private void OnRemotePlayerJoined(PlayerJoinedRoomEventData eventData)
    {
        RegisterPlayerName(eventData.PlayerId, eventData.PlayerName);

        // 视图可能已经按房间成员列表建好了（名字后到）：补刷名牌，不重建对象
        if (_remotePlayers.TryGetValue(eventData.PlayerId, out var existing) && existing != null)
        {
            existing.SetDisplayName(ResolveDisplayName(eventData.PlayerId, eventData.PlayerName));
            return;
        }

        CreateRemotePlayer(eventData.PlayerId, eventData.PlayerName);
    }

    private void OnRemotePlayerLeft(PlayerLeftRoomEventData eventData)
    {
        // 对局中座位是固定的：逻辑世界整局不删人，视图也不能销毁，
        // 否则驱动里 View.Transform 会变成 null。只标离线，整局继续模拟。
        if (LockstepGameDriver.Instance != null) return;

        RemoveRemotePlayer(eventData.PlayerId);
    }

    public PlayerView CreateLocalPlayer(string playerId, string playerName)
    {
        if (_localPlayer != null)
        {
            Debug.LogWarning("[PlayerManager] ⚠️ 本地玩家已存在，销毁旧对象");
            Object.Destroy(_localPlayer.gameObject);
        }
        if (LocalPlayerPrefab == null)
        {
            Debug.LogError("[PlayerManager] ❌ 本地玩家预制体未设置");
            return null;
        }

        GameObject playerObj = Object.Instantiate(LocalPlayerPrefab, LocalSpawnPosition, Quaternion.identity);
        _localPlayer = playerObj.GetComponent<PlayerView>();
        if (_localPlayer == null) _localPlayer = playerObj.AddComponent<PlayerView>();
        _localPlayer.Initialize(playerId, playerName, isLocal: true);
        Debug.Log($"[PlayerManager] ✅ 本地玩家创建完成: {playerName}");
        return _localPlayer;
    }

    public void RecordPlayerNames(GameNetwork.RoomInfo roomInfo)
    {
        if (roomInfo == null) return;
        foreach (var player in roomInfo.Players)
            if (!string.IsNullOrEmpty(player.PlayerName))
                _playerNames[player.PlayerId] = player.PlayerName;
    }

    public void RegisterPlayerName(string playerId, string playerName)
    {
        if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(playerName))
            _playerNames[playerId] = playerName;
    }

    public string ResolveDisplayName(string playerId, string fallback)
    {
        if (_playerNames.TryGetValue(playerId, out var name) && !string.IsNullOrEmpty(name)) return name;
        return string.IsNullOrEmpty(fallback) ? playerId : fallback;
    }

    public PlayerView CreateRemotePlayer(string playerId, string playerName, Vector3? spawnPosition = null)
    {
        if (_remotePlayers.TryGetValue(playerId, out var existing))
        {
            // 对象已在、昵称后到：补刷显示名，避免名牌一直停在 playerId
            if (existing != null) existing.SetDisplayName(ResolveDisplayName(playerId, playerName));
            return existing;
        }
        if (RemotePlayerPrefab == null)
        {
            Debug.LogError("[PlayerManager] ❌ 远程玩家预制体未设置");
            return null;
        }

        Vector3 spawnPos = spawnPosition ?? CircleSpawn(_remotePlayers.Count);
        GameObject playerObj = Object.Instantiate(RemotePlayerPrefab, spawnPos, Quaternion.identity);
        var view = playerObj.GetComponent<PlayerView>();
        if (view == null) view = playerObj.AddComponent<PlayerView>();
        view.Initialize(playerId, ResolveDisplayName(playerId, playerName), isLocal: false);
        view.SetPosition(spawnPos);
        _remotePlayers[playerId] = view;
        Debug.Log($"[PlayerManager] ✅ 远程玩家创建完成: {playerId} at {spawnPos}");
        return view;
    }

    private Vector3 CircleSpawn(int index)
    {
        float angle = index * (360f / 8f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle) * RemoteSpawnRadius, 3f, Mathf.Sin(angle) * RemoteSpawnRadius);
    }

    public void RemoveRemotePlayer(string playerId)
    {
        if (!_remotePlayers.TryGetValue(playerId, out var view)) return;
        if (view != null) Object.Destroy(view.gameObject);
        _remotePlayers.Remove(playerId);
    }

    public void ClearAllRemotePlayers()
    {
        foreach (var view in _remotePlayers.Values)
            if (view != null) Object.Destroy(view.gameObject);
        _remotePlayers.Clear();
    }

    public PlayerView GetLocalPlayer() => _localPlayer;

    public PlayerView GetRemotePlayer(string playerId)
    {
        _remotePlayers.TryGetValue(playerId, out var view);
        return view;
    }

    public Dictionary<string, PlayerView> GetAllRemotePlayers() => new Dictionary<string, PlayerView>(_remotePlayers);
    public int GetRemotePlayerCount() => _remotePlayers.Count;
    public bool HasPlayer(string playerId) => _remotePlayers.ContainsKey(playerId);

    public void PrintStatus()
    {
        Debug.Log("========== PlayerManager Status ==========");
        Debug.Log($"Local Player: {(_localPlayer != null ? _localPlayer.name : "None")}");
        Debug.Log($"Remote Players: {_remotePlayers.Count}");
        foreach (var kvp in _remotePlayers) Debug.Log($"  - {kvp.Key}: {kvp.Value?.name}");
    }
}
