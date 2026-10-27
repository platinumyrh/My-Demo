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

    private void Awake()
    {
        Anim = PlayerAnimationController.Resolve(transform);
        Nameplate = GetComponentInChildren<NameplateUI>(true);
        Weapon = GetComponentInChildren<WeaponController>(true);
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
