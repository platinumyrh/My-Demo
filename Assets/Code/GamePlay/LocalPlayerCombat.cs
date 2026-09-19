using UnityEngine;

/// <summary>
/// 本地玩家战斗编排（挂 Player 根节点，与 LocalPlayerMovement 平级）
/// 职责：把"谁在按扳机、往哪打"接起来——
///   PlayerInputController 的输入 → WeaponController 的判定与表现 → 反馈（准星/动画/摄像机）
/// 伤害结算在服务端（血量账本）：
///   本地只上报伤害结果（FireEvent.damage），自己的 hp/is_dead 从快照事件（OnLocalVitalsChanged）接收
/// </summary>
[RequireComponent(typeof(PlayerInputController))]
[RequireComponent(typeof(LocalPlayerMovement))]
public class LocalPlayerCombat : MonoBehaviour
{
    [Header("武器（留空自动在子物体里找）")]
    [Tooltip("武器控制器（枪模型物体上的那个）")]
    public WeaponController weapon;

    private PlayerInputController _input;
    private LocalPlayerMovement _movement;
    private PlayerAnimationController _anim;

    // 本地玩家死亡状态（服务端结算判定，随快照下发）
    private bool _isDead;

    // 头顶名牌（本地玩家的名字与血条）
    private NameplateUI _nameplate;

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

        // 名牌：名字 + 血条（血条由服务端结算的 hp 驱动）
        _nameplate = GetComponentInChildren<NameplateUI>(true);
        if (_nameplate != null)
        {
            string localName = NetworkManager.Instance != null
                ? NetworkManager.Instance.GetLocalPlayerName()
                : "";
            _nameplate.SetName(localName);
        }

        // 服务端结算结果 → 本地状态
        var eventBus = GameEventBus.GetInstance();
        eventBus.Subscribe<LocalVitalsEventData>(
            GameEventType.OnLocalVitalsChanged,
            HandleLocalVitalsChanged
        );
        eventBus.Subscribe<PlayerDiedEventData>(
            GameEventType.OnPlayerDied,
            HandlePlayerDied
        );

        // 被命中（射手开火广播，我是受击者）→ 播方向受击动画
        eventBus.Subscribe<RemoteFiredEventData>(
            GameEventType.OnRemoteFired,
            HandleRemoteHit
        );
    }

    private void OnDestroy()
    {
        if (weapon != null)
        {
            weapon.OnFired -= HandleFired;
            weapon.OnReloadStarted -= HandleReloadStart;
            weapon.OnReloadFinished -= HandleReloadEnd;
        }

        var eventBus = GameEventBus.GetInstance();
        eventBus.Unsubscribe<LocalVitalsEventData>(
            GameEventType.OnLocalVitalsChanged,
            HandleLocalVitalsChanged
        );
        eventBus.Unsubscribe<PlayerDiedEventData>(
            GameEventType.OnPlayerDied,
            HandlePlayerDied
        );
        eventBus.Unsubscribe<RemoteFiredEventData>(
            GameEventType.OnRemoteFired,
            HandleRemoteHit
        );
    }

    private void Update()
    {
        // 与移动同一道门：游戏开始（帧同步中）才允许战斗
        var frameSync = FrameSyncManager.GetInstance();
        if (frameSync == null || !frameSync.IsSyncing) return;

        // 死亡后停火停换弹（移动是否锁定由后续重生流程决定）
        if (_isDead) return;

        // 开火（按住连发，节奏由武器的 fireRate 控制）
        // 瞄准射线：起点=枪口/角色前方，方向=枪口→鼠标瞄准点（会聚射线，见 LocalPlayerMovement.GetAimDirection）
        if (_input.FireHeld && weapon.TryFire(new Ray(_movement.GetMuzzlePosition(), _movement.GetAimDirection())))
        {
            // 命中/未命中的细分反馈在 OnFired 事件里处理
        }

        // 换弹（InputActions 的 Reload action，默认 R）
        if (_input.ReloadPressed)
        {
            weapon.TryReload();
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

    #region 武器事件 → 表现/网络

    private void HandleFired(WeaponController.ShotInfo shot)
    {
        // 开火动画（新模型的 fire 触发器）
        _anim?.PlayFire();

        // 命中反馈
        if (shot.HitCollider != null)
        {
            CrosshairUI.Instance?.ShowHit();
        }

        // 上报开火事件：服务端对受击方结算伤害（damage = 本地 SO 配置的武器伤害）
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
                HitNormalZ = shot.HitNormal.z,
                Damage = weapon.damage
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

    #region 服务端结算结果 → 本地状态

    /// <summary>
    /// 本地生命值变化（服务端结算结果随快照下发）
    /// </summary>
    private void HandleLocalVitalsChanged(LocalVitalsEventData eventData)
    {
        // 头顶血条同步
        _nameplate?.SetHp(eventData.Hp);

        if (eventData.IsDead && !_isDead)
        {
            _isDead = true;
            Debug.Log("[LocalPlayerCombat] ☠️ 本地玩家死亡，停用战斗输入");

            // 死亡表现：动画门面会走 active=false 的 Inactive 状态（droid 模型）
            _anim?.SetDead(true);
            return;
        }

        if (!eventData.IsDead && _isDead)
        {
            _isDead = false;
            _anim?.Revive();
            return;
        }

        // 血量下降 → 受击反馈（摄像机预留的钩子）
        if (_lastHp > eventData.Hp)
        {
            CameraFollow.Instance?.NotifyDamaged(_lastHp - eventData.Hp, Vector3.zero);
        }
        _lastHp = eventData.Hp;
    }

    private int _lastHp = -1;

    /// <summary>
    /// 玩家死亡广播：击杀者是自己时给击杀反馈
    /// </summary>
    private void HandlePlayerDied(PlayerDiedEventData eventData)
    {
        string localId = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetLocalPlayerId()
            : "";

        if (eventData.KillerId == localId)
        {
            CrosshairUI.Instance?.ShowKill();
        }
    }

    /// <summary>
    /// 我是受击者（射手开火广播，hit_player_id == 我）→ 播方向受击动画
    /// 受击方向 = 「我的位置 → 射手枪口」向量转进角色本地空间：
    /// localZ +/− = 来自前方/后方，localX +/− = 来自右侧/左侧
    /// </summary>
    private void HandleRemoteHit(RemoteFiredEventData eventData)
    {
        string localId = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetLocalPlayerId()
            : "";

        if (string.IsNullOrEmpty(localId) || eventData.HitPlayerId != localId) return;

        // 攻击来源：射手枪口（世界坐标，双端场景一致）
        Vector3 incoming = eventData.Origin - transform.position;
        incoming.y = 0f;
        if (incoming.sqrMagnitude < 0.0001f) return;

        Vector3 local = transform.InverseTransformDirection(incoming.normalized);

        _anim?.PlayDamage(new Vector2(local.x, local.z));
    }

    #endregion
}
