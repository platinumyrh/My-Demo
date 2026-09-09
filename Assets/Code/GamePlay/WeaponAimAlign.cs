using UnityEngine;

/// <summary>
/// 武器朝向对齐：武器的 Y 轴旋转跟随「武器位置 → 准星」方向
///
/// 角色身体朝向准星（LocalPlayerMovement 的 yaw），武器作为子物体在身侧有个横向偏移，
/// 身体朝向 ≠ 武器到准星的真实方向；本脚本让武器额外转一个小的偏航角，
/// 使枪管实际指向准星——弹道、枪口、准星三者视觉一致。
///
/// 本地玩家：用 LocalPlayerMovement.AimPoint（鼠标世界瞄准点）
/// 远端玩家：没有同步瞄准点，直接对齐身体前向（远端身体 yaw 本来就是同步过来的朝向）
/// </summary>
public class WeaponAimAlign : MonoBehaviour
{
    [Tooltip("要转向的节点（默认自身）")]
    [SerializeField] private Transform weaponRoot;

    [Tooltip("转向平滑速率（越大转得越快）")]
    [SerializeField] private float smoothSpeed = 14f;

    private Transform _player;
    private LocalPlayerMovement _local; // 本地玩家才有 AimPoint；远端为 null

    private void Awake()
    {
        if (weaponRoot == null) weaponRoot = transform;

        _local = GetComponentInParent<LocalPlayerMovement>();
        if (_local != null)
        {
            _player = _local.transform;
        }
        else
        {
            var remote = GetComponentInParent<RemotePlayerMovement>();
            _player = remote != null ? remote.transform : transform.root;
        }
    }

    private void LateUpdate()
    {
        if (_player == null || weaponRoot == null) return;

        // 目标方向：武器位置 → 准星（本地）；远端没有瞄准点，对齐身体前向
        Vector3 targetPoint;
        if (_local != null && _local.HasAimPoint)
        {
            targetPoint = _local.AimPoint;
        }
        else
        {
            targetPoint = _player.position + _player.forward * 10f;
        }

        Vector3 dir = targetPoint - weaponRoot.position;
        dir.y = 0f; // 只转偏航，不做俯仰
        if (dir.sqrMagnitude < 0.0004f) return; // 瞄准点几乎在武器正上/下方，保持当前朝向

        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        Quaternion target = Quaternion.Euler(0f, yaw, 0f);

        weaponRoot.rotation = Quaternion.Slerp(
            weaponRoot.rotation,
            target,
            Mathf.Min(1f, smoothSpeed * Time.deltaTime)
        );
    }

    private void OnDrawGizmosSelected()
    {
        // 选中时可视化：绿线=武器朝向，蓝点=瞄准点
        if (!Application.isPlaying || _player == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawRay(weaponRoot.position, weaponRoot.forward * 2f);

        if (_local != null && _local.HasAimPoint)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_local.AimPoint, 0.1f);
            Gizmos.DrawLine(weaponRoot.position, _local.AimPoint);
        }
    }
}
