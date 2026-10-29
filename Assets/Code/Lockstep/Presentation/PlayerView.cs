using UnityEngine;

/// <summary>
/// 玩家表现层视图：只承载身份、动画、名牌、武器特效入口。
/// 不参与任何网络/状态同步；位置由 LockstepGameDriver 按逻辑帧插值驱动。
/// </summary>
public class PlayerView : MonoBehaviour
{
    public string PlayerId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public bool IsLocal { get; private set; }

    public PlayerAnimationController Anim { get; private set; }
    public NameplateUI Nameplate { get; private set; }
    public WeaponController Weapon { get; private set; }

    [Header("头部瞄准（留空自动找 Droid/Body/HeadCtrl/Head）")]
    [SerializeField] private Transform head;

    // 头部静止时的世界朝向（已扣掉根节点 yaw）：根节点 yaw=0 时头部指向世界 +Z，正好是逻辑角 0°
    private Quaternion _headBaseRot = Quaternion.identity;
    private float _headAimYawDeg;
    private bool _hasHeadAim;

    private void Awake()
    {
        Anim = PlayerAnimationController.Resolve(transform);
        Nameplate = GetComponentInChildren<NameplateUI>(true);
        Weapon = GetComponentInChildren<WeaponController>(true);

        if (head == null) head = transform.Find("Droid/Body/HeadCtrl/Head");
        if (head != null)
        {
            // 只扣 yaw 不扣整体旋转：Droid/HeadCtrl 上那些 ±90° X 是用来翻正模型的，
            // 属于头部静止姿态的一部分
            _headBaseRot = Quaternion.Euler(0f, -transform.eulerAngles.y, 0f) * head.rotation;
        }
    }

    /// <summary>
    /// 头部瞄准角（逻辑瞄准角，绕世界 Y）。身体由移动方向驱动，头单独追准星。
    /// 实际旋转放在 LateUpdate：Animator 在 Update 阶段写子物体，这里后写才能压住
    /// </summary>
    public void SetHeadAim(float aimYawDeg)
    {
        _headAimYawDeg = aimYawDeg;
        _hasHeadAim = head != null;
    }

    /// <summary>死亡等场景：交还给动画，不再每帧覆盖头部朝向</summary>
    public void ClearHeadAim() => _hasHeadAim = false;

    private void LateUpdate()
    {
        if (!_hasHeadAim) return;
        head.rotation = Quaternion.Euler(0f, _headAimYawDeg, 0f) * _headBaseRot;
    }

    public void Initialize(string playerId, string displayName, bool isLocal)
    {
        PlayerId = playerId;
        DisplayName = string.IsNullOrEmpty(displayName) ? playerId : displayName;
        IsLocal = isLocal;
        gameObject.name = (isLocal ? "LocalPlayer_" : "RemotePlayer_") + PlayerId;
        Nameplate?.SetName(DisplayName);
    }

    public void SetDisplayName(string displayName)
    {
        DisplayName = string.IsNullOrEmpty(displayName) ? PlayerId : displayName;
        Nameplate?.SetName(DisplayName);
    }

    public void SetPosition(Vector3 position) => transform.position = position;

    public void PlayFireVisual(Vector3 origin, Vector3 direction, Vector3 endPoint,
                               bool hitSomething, Vector3 impactNormal)
    {
        if (Weapon != null)
            Weapon.PlayFireVisual(origin, direction, endPoint, hitSomething, impactNormal);
        Anim?.PlayFire();
    }

    public void PlayDamage(Vector3 shooterOrigin)
    {
        Vector3 incoming = shooterOrigin - transform.position;
        incoming.y = 0f;
        if (incoming.sqrMagnitude < 0.0001f) return;

        Vector3 local = transform.InverseTransformDirection(incoming.normalized);
        Anim?.PlayDamage(new Vector2(local.x, local.z));
    }

    public void SetDead(bool dead)
    {
        Anim?.SetDead(dead);
    }
}
