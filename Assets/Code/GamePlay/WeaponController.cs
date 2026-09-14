using System;
using UnityEngine;

/// <summary>
/// 武器控制器（挂枪模型物体上，本地/远程玩家的枪都挂同一个组件）
/// 职责：一把枪"能怎么打"——射速节奏、弹匣、换弹计时、开火表现（枪口火光/弹道/命中特效）
/// 不负责：谁在按扳机（LocalPlayerCombat/网络事件的事）、往哪打（调用方传入瞄准射线）
///
/// 本地：LocalPlayerCombat 调 TryFire/TryReload
/// 远端：远程表现层直接调 PlayFireVisual/PlayReloadVisual（跳过弹匣和射速判定，
///       判定归拥有方客户端——与位置权威同一套哲学）
/// </summary>
public class WeaponController : MonoBehaviour
{
    #region 配置

    [Tooltip("载具配置（配置后武器数值以 SO 为准，覆盖下方默认值）")]
    [SerializeField] private PlayerProfileSO profile;

    [Header("射击参数")]
    [Tooltip("射速（发/秒）")]
    public float fireRate = 8f;

    [Tooltip("弹匣容量")]
    public int magazineSize = 30;

    [Tooltip("换弹耗时（秒）")]
    public float reloadTime = 1.5f;

    [Tooltip("单发伤害（结算系统接入后生效）")]
    public int damage = 10;

    [Tooltip("最大射程（米）")]
    public float maxRange = 50f;

    [Tooltip("打空后自动换弹")]
    public bool autoReload = true;

    [Header("引用")]
    [Tooltip("枪口挂点（枪管尖端建一个空子物体拖这里；留空用射线起点")]
    public Transform muzzle;

    [Tooltip("命中检测的层")]
    public LayerMask hitLayers = ~0;

    [Header("特效（拖 EffectCore 同色同尺寸的三件套）")]
    [Tooltip("弹道拖尾（Projectile prefab）")]
    public GameObject tracerPrefab;

    [Tooltip("枪口火光（MuzzleFlare prefab）")]
    public GameObject muzzleFlashPrefab;

    [Tooltip("命中火花（Impact prefab）")]
    public GameObject impactPrefab;

    [Tooltip("特效存活时间（秒）")]
    public float vfxLifetime = 1.5f;

    [Tooltip("拖尾起始比例（掐掉弹道前段，枪口附近的斜线最显眼；后续有散布时可归零）")]
    [Range(0f, 0.5f)]
    public float tracerStartRatio = 0.25f;

    [Header("调试可视化")]
    [Tooltip("画出每次开火的命中射线（红线，停留1秒）")]
    public bool drawShotRay = true;

    [Tooltip("枪口火光轴向偏移：EffectCore 火光轴向特殊，-90°X 为实测校正值；仍不对时在此微调")]
    [SerializeField] private Vector3 muzzleFlashRotationOffset = new Vector3(-90f, 0f, 0f);

    #endregion

    #region 状态

    /// <summary>当前弹匣内子弹数</summary>
    public int CurrentAmmo { get; private set; }

    /// <summary>是否正在换弹</summary>
    public bool IsReloading { get; private set; }

    /// <summary>换弹进度（0..1）</summary>
    public float ReloadProgress { get; private set; }

    private float _fireCooldown;

    #endregion

    #region 事件出口（本地逻辑与表现、网络上报都从这订阅）

    /// <summary>开火成功（含命中信息；hit == null 表示没打中任何东西）</summary>
    public event Action<ShotInfo> OnFired;

    /// <summary>换弹开始</summary>
    public event Action OnReloadStarted;

    /// <summary>换弹完成</summary>
    public event Action OnReloadFinished;

    /// <summary>弹匣变化（当前/容量）</summary>
    public event Action<int, int> OnAmmoChanged;

    /// <summary>一次射击的完整信息</summary>
    public struct ShotInfo
    {
        public Vector3 Origin;        // 弹道起点（枪口）
        public Vector3 Direction;     // 飞行方向
        public Vector3 EndPoint;      // 终点（命中点或射程尽头）
        public Collider HitCollider;  // 命中的碰撞体（未命中为 null）
        public string HitPlayerId;    // 命中的玩家ID（非玩家为 null）
        public Vector3 HitNormal;     // 命中表面法线（未命中为 -Direction）
    }

    #endregion

    private void Awake()
    {
        // 载具配置优先：SO 里的武器数值覆盖 Inspector 默认值（没配置则保持原值，兼容旧预制体）
        if (profile != null)
        {
            fireRate = profile.weaponFireRate;
            magazineSize = profile.weaponMagazineSize;
            reloadTime = profile.weaponReloadTime;
            damage = profile.weaponDamage;
            maxRange = profile.weaponMaxRange;
        }

        CurrentAmmo = magazineSize;
    }

    private void Update()
    {
        // 射速冷却
        if (_fireCooldown > 0f) _fireCooldown -= Time.deltaTime;

        // 换弹计时
        if (IsReloading)
        {
            ReloadProgress += Time.deltaTime / reloadTime;
            if (ReloadProgress >= 1f)
            {
                IsReloading = false;
                ReloadProgress = 0f;
                CurrentAmmo = magazineSize;
                OnAmmoChanged?.Invoke(CurrentAmmo, magazineSize);
                OnReloadFinished?.Invoke();
            }
        }
    }

    #region 本地接口（带判定）

    /// <summary>
    /// 尝试开火（本地权威）。条件不满足静默返回 false
    /// </summary>
    public bool TryFire(Ray aimRay)
    {
        if (IsReloading || _fireCooldown > 0f) return false;

        if (CurrentAmmo <= 0)
        {
            if (autoReload) TryReload();
            return false;
        }

        _fireCooldown = 1f / fireRate;
        CurrentAmmo--;
        OnAmmoChanged?.Invoke(CurrentAmmo, magazineSize);

        FireInternal(aimRay);
        return true;
    }

    /// <summary>
    /// 尝试换弹
    /// </summary>
    public bool TryReload()
    {
        if (IsReloading || CurrentAmmo >= magazineSize) return false;

        IsReloading = true;
        ReloadProgress = 0f;
        OnReloadStarted?.Invoke();
        return true;
    }

    #endregion

    #region 远端接口（纯表现，无判定）

    /// <summary>
    /// 远端玩家开火表现：跳过弹匣/射速判定，直接按给定弹道播放。
    /// 同时广播 OnFired 事件——武器动画控制器等纯表现监听者据此触发开火动作
    /// （注意：远端预制体不要挂 LocalPlayerCombat，否则会重复上报网络事件）
    /// </summary>
    public void PlayFireVisual(Vector3 origin, Vector3 direction, Vector3 endPoint, bool hitSomething, Vector3 impactNormal)
    {
        SpawnMuzzleFlash(origin, direction);
        SpawnTracer(origin, direction, Vector3.Distance(origin, endPoint));

        if (hitSomething)
        {
            Vector3 normal = impactNormal;
            if (normal.sqrMagnitude < 0.001f) normal = -direction;
            normal.Normalize();
            SpawnImpact(endPoint, normal);
        }

        OnFired?.Invoke(new ShotInfo
        {
            Origin = origin,
            Direction = direction,
            EndPoint = endPoint,
            HitCollider = null,
            HitPlayerId = "",
            HitNormal = -direction
        });
    }

    /// <summary>远端换弹表现（重置弹匣显示）</summary>
    public void PlayReloadVisual()
    {
        CurrentAmmo = magazineSize;
    }

    #endregion

    #region 内部

    /// <summary>
    /// 真正的开火：命中判定 + 特效 + 事件
    /// </summary>
    private void FireInternal(Ray aimRay)
    {
        Vector3 origin = muzzle != null ? muzzle.position : aimRay.origin;

        // 命中判定：跳过自己身上的碰撞体
        if (Physics.Raycast(aimRay, out RaycastHit hit, maxRange, hitLayers, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(RootWithoutWeapon()))
        {
            var shot = new ShotInfo
            {
                Origin = origin,
                Direction = aimRay.direction,
                EndPoint = hit.point,
                HitCollider = hit.collider,
                HitPlayerId = GetPlayerId(hit.collider),
                HitNormal = hit.normal
            };

            SpawnMuzzleFlash(origin, aimRay.direction);
            SpawnTracer(origin, aimRay.direction, Vector3.Distance(origin, hit.point));
            SpawnImpact(hit.point, hit.normal);

            if (drawShotRay) Debug.DrawLine(origin, hit.point, Color.red, 1f);
            OnFired?.Invoke(shot);
        }
        else
        {
            // 未命中：弹道打到射程尽头
            var shot = new ShotInfo
            {
                Origin = origin,
                Direction = aimRay.direction,
                EndPoint = aimRay.origin + aimRay.direction * maxRange,
                HitCollider = null,
                HitPlayerId = null,
                HitNormal = -aimRay.direction
            };

            SpawnMuzzleFlash(origin, aimRay.direction);
            SpawnTracer(origin, aimRay.direction, Vector3.Distance(origin, shot.EndPoint));

            if (drawShotRay) Debug.DrawLine(origin, shot.EndPoint, Color.red, 1f);
            OnFired?.Invoke(shot);
        }
    }

    /// <summary>武器属于哪个玩家根节点（用于命中过滤自己）</summary>
    private Transform RootWithoutWeapon()
    {
        // LocalPlayerMovement/RemotePlayerMovement 挂在玩家根上
        var root = GetComponentInParent<LocalPlayerMovement>();
        if (root != null) return root.transform;
        var remote = GetComponentInParent<RemotePlayerMovement>();
        if (remote != null) return remote.transform;
        return transform.root;
    }

    private static string GetPlayerId(Collider collider)
    {
        var remote = collider.GetComponentInParent<RemotePlayerMovement>();
        return remote != null ? remote.playerId : null;
    }

    private void SpawnMuzzleFlash(Vector3 position, Vector3 direction)
    {
        if (muzzleFlashPrefab == null) return;
        // 火光沿射击方向摆放（EffectCore 火光默认朝 +Y，用轴向偏移翻正）
        Quaternion rot = Quaternion.LookRotation(direction) * Quaternion.Euler(muzzleFlashRotationOffset);
        PooledVFX.Spawn(muzzleFlashPrefab, position, rot, vfxLifetime);
    }

    private void SpawnTracer(Vector3 origin, Vector3 direction, float distance)
    {
        if (tracerPrefab == null) return;

        // 方案B：掐掉弹道前段——枪口到准星线的斜线段在近处最明显，
        // 从 tracerStartRatio 处开始画，远段本来就近乎与准星线重合
        float startDistance = distance * tracerStartRatio;
        float remain = distance - startDistance;
        if (remain < 0.05f) return; // 目标太近，剩余段太短就不画

        ProjectileVFX.Spawn(tracerPrefab, origin + direction * startDistance, direction, remain);
    }

    private void SpawnImpact(Vector3 position, Vector3 normal)
    {
        if (impactPrefab == null) return;
        // 走 PoolManager 池化，播完自动归还
        PooledVFX.Spawn(impactPrefab, position, Quaternion.LookRotation(normal), vfxLifetime);
    }

    #endregion
}
