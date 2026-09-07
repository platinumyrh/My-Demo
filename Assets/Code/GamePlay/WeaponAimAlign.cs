using UnityEngine;

/// <summary>
/// 武器跟随准星横向摆动（Aim Offset）
/// 原理：角色永远朝向准星（yaw），所以"玩家中心→准星"连线在角色本地空间就是 +Z 轴。
/// 本脚本把武器沿本地 X（右向）平移，让枪口落到这条线上——
/// 枪口在线上之后，"从枪口出发 + 会聚到准星 + 穿过准星"三者同时成立，
/// 弹道/准星/弹孔天然一条线。
///
/// 本地/远端通用：只需要玩家根节点的 yaw（远端本来就会同步朝向）。
/// 挂载建议：直接挂在枪物体上（weaponRoot 默认自身）；
/// 以后做武器闲置/开火动画时，插入一个 WeaponRoot 空节点：本脚本在 WeaponRoot 上，
/// 动画层级放它下面，摆动和动画就互不干扰。
/// </summary>
public class WeaponAimAlign : MonoBehaviour
{
    [Tooltip("要摆动的节点（默认自身）")]
    [SerializeField] private Transform weaponRoot;

    [Tooltip("枪口（默认取同物体上 WeaponController 的 muzzle）")]
    [SerializeField] private Transform muzzle;

    [Tooltip("最大横向偏移（米）")]
    [SerializeField] private float maxOffset = 0.3f;

    [Tooltip("补偿比例（0..1）：1=枪口完全滑到瞄准线上（枪会到身体中间），0=不动。0.4 左右枪留在身侧只轻微摆动")]
    [Range(0f, 1f)]
    [SerializeField] private float alignmentRatio = 0.4f;

    [Tooltip("跟随速度（越大贴得越紧）")]
    [SerializeField] private float smoothSpeed = 10f;

    private Transform _player;            // 玩家根节点（本地或远端）
    private Vector3 _authoredLocalPos;    // weaponRoot 的原始摆位
    private float _currentOffset;         // 当前已施加的横向偏移

    private void Awake()
    {
        if (weaponRoot == null) weaponRoot = transform;
        _authoredLocalPos = weaponRoot.localPosition;

        if (muzzle == null)
        {
            var weapon = GetComponent<WeaponController>();
            if (weapon != null) muzzle = weapon.muzzle;
        }
    }

    private void Start()
    {
        // 找玩家根：先本地后远端，都没有就用所在树的根
        var local = GetComponentInParent<LocalPlayerMovement>();
        if (local != null)
        {
            _player = local.transform;
        }
        else
        {
            var remote = GetComponentInParent<RemotePlayerMovement>();
            _player = remote != null ? remote.transform : transform.root;
        }
    }

    private void LateUpdate()
    {
        if (muzzle == null || _player == null || weaponRoot.parent == null) return;

        // 枪口在玩家本地空间的横向位置。注意减去当前已施加的偏移还原"原始枪口X"，
        // 否则补偿量只收敛一半（目标值随自身输出变化）
        Vector3 muzzleLocal = _player.InverseTransformPoint(muzzle.position);
        float authoredX = muzzleLocal.x - _currentOffset;          // 未摆动时的枪口X
        float target = Mathf.Clamp(-authoredX * alignmentRatio, -maxOffset, maxOffset);

        _currentOffset = Mathf.Lerp(_currentOffset, target, Mathf.Min(1f, smoothSpeed * Time.deltaTime));

        // 沿玩家右向施加偏移（换算到 weaponRoot 父节点的本地空间，兼容任意层级旋转）
        Vector3 rightLocal = weaponRoot.parent.InverseTransformDirection(_player.right);
        weaponRoot.localPosition = _authoredLocalPos + rightLocal * _currentOffset;
    }

    private void OnDrawGizmosSelected()
    {
        // 选中时画出瞄准线和枪口当前位置，方便在 Scene 视图检查对齐效果
        if (_player == null || !Application.isPlaying) return;

        Gizmos.color = Color.green;
        Gizmos.DrawRay(_player.position, _player.forward * 3f);

        if (muzzle != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(muzzle.position, 0.1f);
        }
    }
}
