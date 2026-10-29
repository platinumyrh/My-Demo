using System.Collections.Generic;
using GameLockstep;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 单机试玩入口：直接在 Main 场景按 Play 时，跳过登录/房间服务器，
/// 本地伪造会话信息与房间成员，按与服务器相同的种子/座位表启动 lockstep。
/// 正常从 Login → Room → Main 进来（已登录）时不会触发。
/// </summary>
public class OfflinePlaySession : MonoBehaviour
{
    public const string LocalPlayerId = "offline-local";
    public const string LocalPlayerName = "离线玩家";

    private const ulong SESSION_SEED = 20261029UL;
    private const int BOT_COUNT = 1;

    /// <summary>当前是否处于单机模式（LockstepGameDriver 据此走本地封帧）</summary>
    public static bool IsActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStartFromMainScene()
    {
        // 非单机入口一律复位（关闭 Domain Reload 时静态字段会跨 Play 残留）
        if (SceneManager.GetActiveScene().name != "Main") { IsActive = false; return; }

        var network = NetworkManager.Instance;
        if (network == null || network.IsLoggedIn || !string.IsNullOrEmpty(network.GetLocalPlayerId()))
        {
            IsActive = false;
            return;
        }

        var go = new GameObject("[OfflinePlaySession]");
        DontDestroyOnLoad(go);
        go.AddComponent<OfflinePlaySession>();
    }

    private void Start()
    {
        IsActive = true;

        // 绕过登录：直接写入本地玩家身份（网络未连接，不会有任何 C2S 发出）
        NetworkManager.Instance.ApplyLoginSuccess(LocalPlayerId, LocalPlayerName, "offline");

        var playerIds = new List<string> { LocalPlayerId };
        var playerManager = PlayerManager.GetInstance();
        for (int i = 0; i < BOT_COUNT; i++)
        {
            string botId = $"bot_{i + 1}";
            playerIds.Add(botId);
            playerManager.RegisterPlayerName(botId, $"机器人 {i + 1}");
        }

        // 绕过房间：本地构造房间状态，GameInitializer 据此生成 PlayerView
        RoomManager.GetInstance().EnterOfflineRoom("OFFLINE", "单机测试", playerIds, LocalPlayerId);

        var seats = new List<KeyValuePair<string, int>>(playerIds.Count);
        for (int i = 0; i < playerIds.Count; i++)
            seats.Add(new KeyValuePair<string, int>(playerIds[i], i));

        // 先缓存会话，GameInitializer 建好 PlayerView 后会补启动
        LockstepGameDriver.BeginSession(SESSION_SEED, LockstepConfig.TICK_RATE, seats);

        Debug.Log($"[OfflinePlay] 🎮 单机模式启动：{playerIds.Count} 个座位，种子 {SESSION_SEED}，无需服务器");
    }
}
