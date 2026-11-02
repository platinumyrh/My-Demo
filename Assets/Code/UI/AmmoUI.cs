using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 弹药 HUD：把逻辑层的弹匣余量显示成 "当前 / 弹匣容量"。
/// 数据源是 LogicPlayer.Ammo（确定性状态，回滚会一起纠正），由 LockstepGameDriver 每逻辑帧推送；
/// 本类不参与任何开火/换弹判定，也不订阅事件（事件会被回滚重演，和真实弹匣对不上）。
/// 挂到 Main 场景 Panel_HUD 上即可，留空按名字自动找子节点 Text_Ammo。
/// </summary>
public class AmmoUI : MonoBehaviour
{
    public static AmmoUI Instance { get; private set; }

    [Header("引用（留空按节点名自动查找）")]
    [Tooltip("显示弹药的文本（Panel_HUD/Text_Ammo）")]
    [SerializeField] private Text ammoText;

    [Header("表现参数")]
    [Tooltip("弹匣打空时的文字颜色")]
    [SerializeField] private Color emptyColor = new Color(0.85f, 0.25f, 0.2f);

    private Color _normalColor;
    private int _lastAmmo = int.MinValue;
    private int _lastMax = int.MinValue;

    private void Awake()
    {
        Instance = this;

        if (ammoText == null)
        {
            var t = transform.Find("Text_Ammo");
            if (t != null) ammoText = t.GetComponent<Text>();
        }

        if (ammoText == null)
        {
            Debug.LogWarning($"[AmmoUI] ⚠️ [{name}] 没找到弹药文本（挂到 Panel_HUD 上，或把 Text_Ammo 拖到 Ammo Text）", this);
            return;
        }

        _normalColor = ammoText.color;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>设置弹匣余量；和上次一样就不刷新 UI</summary>
    public void SetAmmo(int ammo, int max)
    {
        if (ammoText == null) return;
        if (ammo == _lastAmmo && max == _lastMax) return;

        _lastAmmo = ammo;
        _lastMax = max;
        ammoText.text = $"{ammo} / {max}";
        ammoText.color = ammo > 0 ? _normalColor : emptyColor;
    }
}
