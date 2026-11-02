using System.Collections.Generic;
using GameLockstep;
using GameNetwork;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 断线重连（客户端）：登录后问服务器“我在不在对局里”，
/// 在就把 Main 载起来、按座位重建玩家视图，再把服务器整局权威帧快进一遍，
/// 最后从历史末尾接回实时预测——输入是唯一真值，快进阶段不采样、不预测。
/// </summary>
public static class ReconnectSession
{
    private const string MainSceneName = "Main";
    private const string LobbySceneName = "Lobby";
    private const int MAX_HISTORY_RETRIES = 2;

    private static bool _hasPending;
    private static bool _sceneReady;
    private static int _localSeat = -1;
    private static ReconnectInfoNotify _info;
    private static readonly List<LockstepFrameInputs> _frames = new();
    private static int _retries;

    public static bool IsPending => _hasPending;

    /// <summary>登录成功后调用：询问服务器有没有可恢复的对局。没有回复就正常进大厅。</summary>
    public static void Request()
    {
        if (LockstepGameDriver.Instance != null) return; // 已经在局内
        NetworkManager.Instance.Send(MsgID.C2SReconnect, new ReconnectReq());
        Debug.Log("[Reconnect] 📡 已询问服务器是否有可恢复的对局");
    }

    /// <summary>收到服务器对局信息：缓存并切到 Main。</summary>
    public static void Begin(ReconnectInfoNotify info)
    {
        if (info == null || info.LockstepSeed == 0 || info.Seats.Count == 0)
        {
            Debug.LogWarning("[Reconnect] ⚠️ 重连信息不完整，忽略");
            return;
        }
        if (LockstepGameDriver.Instance != null)
        {
            Debug.LogWarning("[Reconnect] ⚠️ 已在局内，忽略重连信息");
            return;
        }
        if (info.TickRate > 0 && info.TickRate != LockstepConfig.TICK_RATE)
        {
            Debug.LogError($"[Reconnect] ❌ 帧率不一致：服务器 {info.TickRate} vs 本地 {LockstepConfig.TICK_RATE}");
            return;
        }

        string localId = NetworkManager.Instance.GetLocalPlayerId();
        _localSeat = -1;
        foreach (var s in info.Seats)
            if (s.PlayerId == localId) _localSeat = s.Seat;
        if (_localSeat < 0)
        {
            Debug.LogError($"[Reconnect] ❌ 座位表里没有本地玩家 [{localId}]");
            return;
        }

        _info = info;
        _frames.Clear();
        _retries = 0;
        _sceneReady = false;
        _hasPending = true;

        // 恢复房间状态：离开对局/广播都靠它；玩家视图由 TryStartPending 按座位表重建
        var members = new List<KeyValuePair<string, string>>(info.Seats.Count);
        foreach (var s in info.Seats)
            members.Add(new KeyValuePair<string, string>(s.PlayerId, s.PlayerName));
        RoomManager.GetInstance()?.RestoreRoomState(info.RoomId, members);

        Debug.Log($"[Reconnect] 🔌 发现未结束对局 [{info.RoomId}]，服务器已封到帧 {info.LatestFrame}，进入 Main 重建");
        SceneManager.LoadScene(MainSceneName);
    }

    /// <summary>Main 场景管理器就绪后调用：建玩家视图，然后请服务器发历史帧。</summary>
    public static void TryStartPending()
    {
        if (!_hasPending || _sceneReady || _info == null) return;

        var pm = PlayerManager.GetInstance();
        var local = pm?.GetLocalPlayer();
        if (pm == null)
        {
            Fail("PlayerManager 未初始化");
            return;
        }

        // GameInitializer 已按房间把玩家建好（RestoreRoomState 之后会走那条路径）；
        // 本地视图已经存在就不再重复实例化，只清掉远端按座位表重建
        if (local != null)
            pm.ClearAllRemotePlayers();
        SpawnViews(pm);

        _sceneReady = true;
        _retries = 0;
        NetworkManager.Instance.Send(MsgID.C2SReconnectReady, new ReconnectReady());
        Debug.Log("[Reconnect] 📤 Main 已就绪，请求服务器发送历史帧");
    }

    /// <summary>收到历史帧分块：按帧号连续追加，done 后快进并接回实时循环。</summary>
    public static void OnFrames(ReconnectFramesNotify msg)
    {
        if (!_hasPending || !_sceneReady || _info == null || msg == null) return;

        foreach (var frame in msg.Frames)
        {
            if (frame.Frame <= _frames.Count) continue; // 重复/旧帧
            if (frame.Frame != _frames.Count + 1)
            {
                RetryHistory($"帧号不连续：期望 {_frames.Count + 1}，收到 {frame.Frame}");
                return;
            }
            _frames.Add(frame);
        }

        if (!msg.Done) return;

        // 历史为空 = 服务器刚开局还没封出第一帧：直接按开赛时刻起一个全新会话即可
        Finish();
    }

    private static void SpawnViews(PlayerManager pm)
    {
        pm.Initialize();

        int count = _info.Seats.Count;
        float radius = pm.RemoteSpawnRadius <= 0f ? 5f : pm.RemoteSpawnRadius;

        Vector3 SlotOf(int seat)
        {
            float angle = seat * (360f / Mathf.Max(count, 1)) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, 7f, Mathf.Sin(angle) * radius);
        }

        string localId = NetworkManager.Instance.GetLocalPlayerId();
        var local = pm.GetLocalPlayer();
        if (local == null)
        {
            var localSeatInfo = FindSeat(_localSeat);
            pm.LocalSpawnPosition = SlotOf(_localSeat);
            pm.CreateLocalPlayer(localSeatInfo?.PlayerId ?? localId, localSeatInfo?.PlayerName ?? localId);
        }

        foreach (var s in _info.Seats)
        {
            if (s.PlayerId == localId) continue;
            pm.CreateRemotePlayer(s.PlayerId, s.PlayerName, SlotOf(s.Seat));
        }
    }

    private static LockstepSeatInfo FindSeat(int seat)
    {
        foreach (var s in _info.Seats)
            if (s.Seat == seat) return s;
        return null;
    }

    /// <summary>把历史帧在本地跑一遍，得到 resumeFrame 的世界状态，然后接回实时会话</summary>
    private static void Finish()
    {
        var seats = new List<KeyValuePair<string, int>>(_info.Seats.Count);
        foreach (var s in _info.Seats)
            seats.Add(new KeyValuePair<string, int>(s.PlayerId, s.Seat));
        seats.Sort((a, b) => a.Value.CompareTo(b.Value));

        var world = new LogicWorld(_info.LockstepSeed);
        foreach (var s in seats)
        {
            LogicWorld.GetSpawnPoint(s.Value, out var x, out var z);
            world.AddPlayer(s.Value, x, z, 0, LockstepConfig.MAX_HP);
        }

        var lastKnown = new Dictionary<int, PlayerCommand>();
        foreach (var frame in _frames)
        {
            var inputs = LockstepProtoConvert.ToInputs(frame);
            foreach (var s in seats)
                if (!inputs.ContainsKey(s.Value)) inputs[s.Value] = PlayerCommand.Empty;

            world.Tick(inputs);
            lastKnown = inputs;
        }

        long resumeFrame = _frames.Count;
        Debug.Log($"[Reconnect] ⏩ 已快进 {resumeFrame} 帧，接回实时循环");

        LockstepSessionBootstrap.StartReconnect(
            world, seats, _localSeat, resumeFrame, lastKnown);

        _hasPending = false;
        _sceneReady = false;
        _info = null;
        _frames.Clear();
    }

    private static void RetryHistory(string reason)
    {
        _frames.Clear();

        if (++_retries > MAX_HISTORY_RETRIES)
        {
            Fail($"历史帧重试 {MAX_HISTORY_RETRIES} 次仍失败（{reason}）");
            return;
        }

        Debug.LogWarning($"[Reconnect] ⚠️ {reason}，重新请求历史帧（第 {_retries} 次）");
        NetworkManager.Instance.Send(MsgID.C2SReconnectReady, new ReconnectReady());
    }

    private static void Fail(string reason)
    {
        Debug.LogError($"[Reconnect] ❌ {reason}，返回大厅");
        _hasPending = false;
        _sceneReady = false;
        _info = null;
        _frames.Clear();
        RoomManager.GetInstance()?.ClearRoomState();
        SceneManager.LoadScene(LobbySceneName);
    }
}
