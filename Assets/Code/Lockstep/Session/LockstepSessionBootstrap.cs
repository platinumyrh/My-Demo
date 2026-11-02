using System.Collections.Generic;
using GameLockstep;
using UnityEngine;

/// <summary>
/// 会话启动器：把 GameStart 带来的种子/座位表变成真正的 LogicWorld + 驱动实例。
/// GameStart 可能早于 Main 场景加载，所以先缓存，等 PlayerView 建好再补启动。
/// </summary>
public static class LockstepSessionBootstrap
{
    private static bool _hasPendingSession;
    private static ulong _pendingSeed;
    private static bool _pendingOnline;
    private static List<KeyValuePair<string, int>> _pendingSeats;

    public static void BeginSession(ulong seed, int tickRate, IReadOnlyList<KeyValuePair<string, int>> seats, bool online = false)
    {
        if (seed == 0 || seats == null || seats.Count == 0)
        {
            Debug.LogWarning("[LockstepBootstrap] 服务器未提供 lockstep 种子/座位表，无法启动帧同步");
            return;
        }
        if (tickRate > 0 && tickRate != LockstepConfig.TICK_RATE)
        {
            Debug.LogError($"[LockstepBootstrap] ❌ 逻辑帧率不一致：服务器 {tickRate} vs 本地 {LockstepConfig.TICK_RATE}，拒进");
            return;
        }

        _pendingSeed = seed;
        _pendingOnline = online;
        _pendingSeats = new List<KeyValuePair<string, int>>(seats);
        _hasPendingSession = true;
        TryStartPendingSession();
    }

    /// <summary>Main 场景生成 PlayerView 后调用；GameStart 先到时会在这里补启动。</summary>
    public static void TryStartPendingSession()
    {
        if (!_hasPendingSession || LockstepGameDriver.Instance != null) return;

        var local = PlayerManager.GetInstance()?.GetLocalPlayer();
        if (local == null) return;

        int localSeat = -1;
        foreach (var kv in _pendingSeats)
            if (kv.Key == local.PlayerId) localSeat = kv.Value;

        _hasPendingSession = false;
        if (localSeat < 0)
        {
            Debug.LogError($"[LockstepBootstrap] ❌ 座位表中找不到本地玩家 [{local.PlayerId}]");
            return;
        }

        // 逻辑世界：种子 + 按座位取确定性出生点（全端一致）
        var world = new LogicWorld(_pendingSeed);
        for (int seat = 0; seat < _pendingSeats.Count; seat++)
        {
            LogicWorld.GetSpawnPoint(seat, out var x, out var z);
            world.AddPlayer(seat, x, z, 0, LockstepConfig.MAX_HP);
        }

        var go = new GameObject("LockstepGameDriver");
        var driver = go.AddComponent<LockstepGameDriver>();
        driver.InitSession(_pendingSeed, world, _pendingSeats, localSeat, _pendingOnline);
    }

    /// <summary>回放启动：世界由录像重建，驱动走 ReplayLockstepSession</summary>
    internal static void StartReplay(ReplayData data, LogicWorld world, int localSeat)
    {
        if (LockstepGameDriver.Instance != null) return;

        var go = new GameObject("LockstepGameDriver");
        var driver = go.AddComponent<LockstepGameDriver>();
        driver.InitReplaySession(data, world, localSeat);
    }

    /// <summary>重连启动：world 已按服务端历史快进到 resumeFrame，从该帧接回实时循环</summary>
    internal static void StartReconnect(LogicWorld world, IReadOnlyList<KeyValuePair<string, int>> seats,
                                        int localSeat, long resumeFrame,
                                        IReadOnlyDictionary<int, PlayerCommand> lastKnown)
    {
        if (LockstepGameDriver.Instance != null) return;

        var go = new GameObject("LockstepGameDriver");
        var driver = go.AddComponent<LockstepGameDriver>();
        driver.InitReconnectedSession(world, seats, localSeat, resumeFrame, lastKnown);
    }
}
