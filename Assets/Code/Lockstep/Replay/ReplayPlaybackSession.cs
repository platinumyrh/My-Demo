using System;
using GameLockstep;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 回放入口：大厅点“观看最近回放”→ 读最新录像 → 清掉房间态 → 进 Main 场景重演。
/// 跨场景挂一个 DontDestroyOnLoad 对象，等 GameInitializer 把管理器都建好后再启动。
/// </summary>
public class ReplayPlaybackSession : MonoBehaviour
{
    private const string PlaybackSceneName = "Main";

    private static ReplayPlaybackSession _instance;
    private static ReplayData _pending;
    private static bool _hasPending;

    /// <summary>大厅按钮调用：加载最新一份录像并切到 Main 场景</summary>
    public static (bool Success, string Message) BeginLatest()
    {
        string path = ReplayData.FindLatestFile();
        if (string.IsNullOrEmpty(path))
            return (false, "还没有录像，先打一局吧");

        ReplayData data;
        try
        {
            data = ReplayData.Load(path);
        }
        catch (Exception ex)
        {
            return (false, $"回放读取失败: {ex.Message}");
        }

        _pending = data;
        _hasPending = true;
        EnsureInstance();

        // 回放是本地重演：清掉房间态，避免 GameInitializer 按房间重建另一套玩家
        RoomManager.GetInstance()?.ClearRoomState();
        SceneManager.LoadScene(PlaybackSceneName);
        return (true, $"正在载入回放（{data.Frames.Count} 帧）...");
    }

    /// <summary>Main 场景管理器就绪后由 GameInitializer 调用</summary>
    public static void TryStartPending()
    {
        if (!_hasPending) return;

        _hasPending = false;
        var data = _pending;
        _pending = null;
        if (data == null) return;

        EnsureInstance().StartPlayback(data);
    }

    private static ReplayPlaybackSession EnsureInstance()
    {
        if (_instance != null) return _instance;

        var go = new GameObject("[ReplayPlaybackSession]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ReplayPlaybackSession>();
        return _instance;
    }

    private void StartPlayback(ReplayData data)
    {
        var pm = PlayerManager.GetInstance();
        if (pm == null)
        {
            Debug.LogError("[Replay] ❌ PlayerManager 未初始化，无法生成回放玩家");
            return;
        }

        SpawnViews(pm, data);

        if (!LockstepGameDriver.Instance)
        {
            var world = data.CreateWorld();
            LockstepSessionBootstrap.StartReplay(data, world, localSeat: 0);
        }
    }

    /// <summary>按录像座位表重建表现层玩家（与 GameInitializer 的圆形槽位规则一致）</summary>
    private static void SpawnViews(PlayerManager pm, ReplayData data)
    {
        pm.Initialize();
        pm.ClearAllRemotePlayers();

        int count = data.Seats.Count;
        float radius = pm.RemoteSpawnRadius <= 0f ? 5f : pm.RemoteSpawnRadius;

        Vector3 SlotOf(int index)
        {
            float angle = index * (360f / Mathf.Max(count, 1)) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, 7f, Mathf.Sin(angle) * radius);
        }

        var local = data.Seats[0];
        pm.LocalSpawnPosition = SlotOf(0);
        pm.CreateLocalPlayer(local.PlayerId, local.PlayerName);

        for (int i = 1; i < count; i++)
        {
            var seat = data.Seats[i];
            pm.CreateRemotePlayer(seat.PlayerId, seat.PlayerName, SlotOf(i));
        }
    }
}
