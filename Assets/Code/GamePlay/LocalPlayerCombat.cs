using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 本地玩家战斗编排（挂 Player 根节点，与 LocalPlayerMovement 平级）
/// 职责：把"谁在按扳机、往哪打"接起来——
///   PlayerInputController 的输入 → WeaponController 的判定与表现 → 反馈（准星/动画）
/// 相当于战斗侧的 LocalPlayerMovement；网络上报（C2S_FireEvent）接入时也挂在这里
/// </summary>
[RequireComponent(typeof(PlayerInputController))]
[RequireComponent(typeof(LocalPlayerMovement))]
public class LocalPlayerCombat : MonoBehaviour
{
    [Header("武器（留空自动在子物体里找）")]
    [Tooltip("武器控制器（枪模型物体上的那个）")]
    public WeaponController weapon;

    [Header("键位（临时直读键盘，后续迁到 InputActions）")]
    [Tooltip("换弹键")]
    public Key reloadKey = Key.R;

    private PlayerInputController _input;
    private LocalPlayerMovement _movement;
    private PlayerAnimationController _anim;

    private void Start()
    {
        _input = GetComponent<PlayerInputController>();
        _movement = GetComponent<LocalPlayerMovement>();
        _anim = PlayerAnimationController.Resolve(transform);

        if (weapon == null) weapon = GetComponentInChildren<WeaponController>();
        if (weapon == null)
        {
            Debug.LogWarning("[LocalPlayerCombat] ⚠️ 找不到 WeaponController，开火功能停用（枪模型物体上挂一个）");
            enabled = false;
            return;
        }

        // 武器事件 → 表现层
        weapon.OnFired += HandleFired;
        weapon.OnReloadStarted += HandleReloadStart;
        weapon.OnReloadFinished += HandleReloadEnd;
    }

    private void OnDestroy()
    {
        if (weapon != null)
        {
            weapon.OnFired -= HandleFired;
            weapon.OnReloadStarted -= HandleReloadStart;
            weapon.OnReloadFinished -= HandleReloadEnd;
        }
    }

    private void Update()
    {
        // 与移动同一道门：游戏开始（帧同步中）才允许战斗
        var frameSync = FrameSyncManager.GetInstance();
        if (frameSync == null || !frameSync.IsSyncing) return;

        // 开火（按住连发，节奏由武器的 fireRate 控制）
        // 瞄准射线：起点=枪口/角色前方，方向=枪口→鼠标瞄准点（会聚射线，见 LocalPlayerMovement.GetAimDirection）
        if (_input.FireHeld && weapon.TryFire(new Ray(_movement.GetMuzzlePosition(), _movement.GetAimDirection())))
        {
            // 命中/未命中的细分反馈在 OnFired 事件里处理
        }

        // 换弹
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            var key = keyboard.FindKeyOnCurrentKeyboardLayout(reloadKey.ToString());
            if (key != null && key.wasPressedThisFrame)
            {
                weapon.TryReload();
            }
        }

        // 持续状态上报（随 30Hz 输入包走，服务端并入 PlayerState 广播）：
        // 按住扳机但换弹中/打空时不视为开火
        _input.CombatFiring = _input.FireHeld && !weapon.IsReloading && weapon.CurrentAmmo > 0;
        _input.CombatReloading = weapon.IsReloading;

        // 开火姿态给动画（Test.controller 暂无 IsFiring 参数，门面会静默跳过）
        _anim?.SetFiring(_input.FireHeld);

        // 换弹进度给准星
        CrosshairUI.Instance?.SetReloadProgress(weapon.IsReloading ? weapon.ReloadProgress : 1f);
    }

    #region 武器事件 → 表现

    private void HandleFired(WeaponController.ShotInfo shot)
    {
        // 命中反馈（击杀反馈等伤害系统接入后在 shot.HitPlayerId 基础上扩展）
        if (shot.HitCollider != null)
        {
            CrosshairUI.Instance?.ShowHit();
        }

        // 上报开火事件：服务端转发给房间其他玩家重播弹道特效
        //（本地已即时播放，服务端不会回发给射手自己）
        try
        {
            var req = new GameNetwork.FireEventReq
            {
                OriginX = shot.Origin.x,
                OriginY = shot.Origin.y,
                OriginZ = shot.Origin.z,
                DirX = shot.Direction.x,
                DirY = shot.Direction.y,
                DirZ = shot.Direction.z,
                EndX = shot.EndPoint.x,
                EndY = shot.EndPoint.y,
                EndZ = shot.EndPoint.z,
                HitSomething = shot.HitCollider != null,
                HitPlayerId = shot.HitPlayerId ?? "",
                HitNormalX = shot.HitNormal.x,
                HitNormalY = shot.HitNormal.y,
                HitNormalZ = shot.HitNormal.z
            };
            NetworkManager.Instance.Send(GameNetwork.MsgID.C2SFireEvent, req);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LocalPlayerCombat] ❌ 上报开火事件失败: {ex.Message}");
        }
    }

    private void HandleReloadStart()
    {
        _anim?.PlayReload();

        // TODO（网络阶段）：上报换弹开始事件
    }

    private void HandleReloadEnd()
    {
        // TODO（网络阶段）：上报换弹结束事件
    }

    #endregion
}
