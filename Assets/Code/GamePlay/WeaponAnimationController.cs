using UnityEngine;

/// <summary>
/// 武器动画驱动（挂在武器模型带 Animator 的节点上，与 WeaponController 同一武器层级）
/// 职责单一：订阅 WeaponController 的战斗事件 → 驱动武器 Animator 的触发器
///   开火事件（OnFired，本地命中与远端表现都会广播）→ fire 触发器
///   换弹开始（OnReloadStarted）→ reload 触发器
/// 参数按名字探测，Animator 里没有的自动跳过——旧武器没有 Animator 也无碍
/// </summary>
public class WeaponAnimationController : MonoBehaviour
{
    [Header("引用（留空自动查找）")]
    [Tooltip("武器 Animator（默认取自身/子物体）")]
    [SerializeField] private Animator animator;

    [Tooltip("武器控制器（默认从父链查找）")]
    [SerializeField] private WeaponController weapon;

    [Header("参数名（需与武器 Animator 面板一致，不存在的自动跳过）")]
    [SerializeField] private string fireTriggerParam = "fire";
    [SerializeField] private string reloadTriggerParam = "reload";

    private int _fireHash;
    private int _reloadHash;
    private bool _hasFire;
    private bool _hasReload;
    private bool _subscribed;
    private WeaponController _weapon;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
    }

    private void Start()
    {
        if (animator == null)
        {
            Debug.LogWarning($"[WeaponAnim] ⚠️ [{name}] 找不到 Animator，武器动画停用", this);
            enabled = false;
            return;
        }

        _fireHash = Animator.StringToHash(fireTriggerParam);
        _reloadHash = Animator.StringToHash(reloadTriggerParam);
        _hasFire = HasTrigger(fireTriggerParam);
        _hasReload = HasTrigger(reloadTriggerParam);

        if (_weapon == null) _weapon = GetComponentInParent<WeaponController>();
        if (_weapon == null)
        {
            Debug.LogWarning($"[WeaponAnim] ⚠️ [{name}] 父链上找不到 WeaponController，武器动画停用", this);
            enabled = false;
            return;
        }

        _weapon.OnFired += OnWeaponFired;
        _weapon.OnReloadStarted += OnWeaponReloadStarted;
        _subscribed = true;
    }

    private void OnDestroy()
    {
        if (_subscribed && _weapon != null)
        {
            _weapon.OnFired -= OnWeaponFired;
            _weapon.OnReloadStarted -= OnWeaponReloadStarted;
        }
    }

    private void OnWeaponFired(WeaponController.ShotInfo shot)
    {
        if (_hasFire) animator.SetTrigger(_fireHash);
    }

    private void OnWeaponReloadStarted()
    {
        if (_hasReload) animator.SetTrigger(_reloadHash);
    }

    private bool HasTrigger(string paramName)
    {
        if (string.IsNullOrEmpty(paramName)) return false;
        foreach (var p in animator.parameters)
        {
            if (p.name == paramName && p.type == AnimatorControllerParameterType.Trigger) return true;
        }
        return false;
    }
}
