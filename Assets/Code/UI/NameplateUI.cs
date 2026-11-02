using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家名牌（名字 + 血条，世界空间 Canvas）
/// 挂在世界空间 Canvas（或其任意父节点）上，按节点名自动查找子引用：
///   Panel_PlayerInfo/Text_PlayerName   名字
///   Panel_PlayerInfo/PlayerHP_Fill     血条填充（Image，自动设为 Filled 水平填充）
/// 职责：
/// 1. Billboard + 位置锚定：每帧面向摄像机并重锚到玩家根节点上方（位置与玩家朝向解耦）
/// 2. SetName / SetHp：本地玩家从生命值事件喂，远端玩家从快照（OnServerState）喂
/// 3. 位置参数（高度/横向）运行时可调，右键组件「保存名牌布局」持久化到 PlayerPrefs
/// </summary>
public class NameplateUI : MonoBehaviour
{
    /// <summary>血条驱动方式</summary>
    public enum HpBarMode
    {
        /// <summary>UGUI 填充（需要 Sprite + Filled 类型，九宫格不变形但需要图）</summary>
        FillAmount,

        /// <summary>改 Fill 宽度（配合父级 Horizontal Layout；Sliced 纯色都可用）</summary>
        Width
    }

    [Header("血条")]
    [Tooltip("血量上限（血条百分比用；不同载具 maxHp 不同时需按类型设置）")]
    [SerializeField] private int maxHp = 100;

    [Tooltip("Width = 改 Fill 宽度（配合父级 Horizontal Layout，Sliced 图用这个）；FillAmount = UGUI 填充")]
    [SerializeField] private HpBarMode barMode = HpBarMode.Width;

    [Header("引用（留空按节点名自动查找）")]
    [Tooltip("名字文本")]
    [SerializeField] private Text nameText;

    [Tooltip("血条填充图（Image.Type 必须是 Filled；留空自动找 PlayerHP_Fill）")]
    [SerializeField] private Image fillImage;

    [Tooltip("血条背景图（留空自动找 PlayerHP_Background）")]
    [SerializeField] private Image backgroundImage;

    [Header("位置（运行时可调，可保存）")]
    [Tooltip("名牌相对玩家根节点的高度")]
    [SerializeField] private float heightOffset = 2.2f;

    [Tooltip("名牌沿摄像机右方的横向偏移（画面里往左/右挪）")]
    [SerializeField] private float lateralOffset = -1f;

    private const string PREF_HEIGHT = "Nameplate_Height";
    private const string PREF_LATERAL = "Nameplate_Lateral";

    private Text _nameText;
    private Image _fillImage;
    private bool _useScaleDrive; // Fill 无 Sprite 时的缩放驱动模式（仅 FillAmount 模式）
    private float _baseBarWidth = 180f; // 血条满宽（Width 模式基准）

    private void Awake()
    {
        // 手动拖拽优先；没拖的按节点名自动查找
        if (nameText == null)
        {
            var t = transform.Find("Panel_PlayerInfo/Text_PlayerName");
            if (t != null) nameText = t.GetComponent<Text>();
        }

        if (fillImage == null)
        {
            var t = transform.Find("Panel_PlayerInfo/PlayerHP_Fill");
            if (t != null) fillImage = t.GetComponent<Image>();
        }

        if (backgroundImage == null)
        {
            var t = transform.Find("Panel_PlayerInfo/PlayerHP_Background");
            if (t != null) backgroundImage = t.GetComponent<Image>();
        }

        if (fillImage != null)
        {
            if (barMode == HpBarMode.Width)
            {
                // Width 模式：记录血条满宽（改 sizeDelta.x 实现变化）。
                // 不动 Image 类型——Sliced 图保持九宫格不变形
                _baseBarWidth = fillImage.rectTransform.sizeDelta.x;
                if (_baseBarWidth <= 0.01f) _baseBarWidth = 180f;
            }
            else
            {
                // FillAmount 模式：需要 Sprite + Filled 类型
                if (fillImage.type != Image.Type.Filled)
                {
                    fillImage.type = Image.Type.Filled;
                    fillImage.fillMethod = Image.FillMethod.Horizontal;
                    fillImage.fillAmount = 1f;
                }

                // 无 Sprite 时 UGUI 强制走 Simple 渲染，fillAmount 无效 → 缩放驱动兜底
                if (fillImage.sprite == null)
                {
                    _useScaleDrive = true;

                    var rt = fillImage.rectTransform;
                    Vector2 pivot = rt.pivot;
                    rt.pivot = new Vector2(0f, pivot.y); // 缩放锚定左边缘，向右收缩
                    rt.anchoredPosition = new Vector2(
                        rt.anchoredPosition.x - rt.rect.width * (0.5f - pivot.x),
                        rt.anchoredPosition.y);
                }
            }
        }

        if (fillImage == null)
        {
            Debug.LogWarning($"[NameplateUI] ⚠️ [{name}] 血条填充图未找到（手动拖或确认节点名 PlayerHP_Fill）", this);
        }

        // 读取上次保存的布局参数（PlayerPrefs 里有就用保存的值）
        if (PlayerPrefs.HasKey(PREF_HEIGHT)) heightOffset = PlayerPrefs.GetFloat(PREF_HEIGHT);
        if (PlayerPrefs.HasKey(PREF_LATERAL)) lateralOffset = PlayerPrefs.GetFloat(PREF_LATERAL);
    }

    private void LateUpdate()
    {
        // Billboard：名牌始终面向摄像机（本游戏摄像机不旋转，成本可忽略）
        if (Camera.main != null)
        {
            transform.rotation = Camera.main.transform.rotation;
        }

        // 位置重锚：名牌挂在会旋转的玩家层级下，billboard 只修朝向不修位置——
        // 每帧重锚到「玩家根节点 + 高度/横向偏移」，位置与玩家朝向彻底解耦
        var root = transform.root;
        transform.position = root.position
                             + Vector3.up * heightOffset
                             + CameraRight() * lateralOffset;
    }

    private Vector3 CameraRight()
    {
        if (Camera.main == null) return Vector3.right;
        Vector3 r = Camera.main.transform.right;
        r.y = 0f;
        return r.sqrMagnitude > 0.001f ? r.normalized : Vector3.right;
    }

    /// <summary>保存当前布局参数（组件右键菜单，Play 模式中调完即存，重开仍在）</summary>
    [ContextMenu("保存名牌布局到本地")]
    public void SaveLayout()
    {
        PlayerPrefs.SetFloat(PREF_HEIGHT, heightOffset);
        PlayerPrefs.SetFloat(PREF_LATERAL, lateralOffset);
        PlayerPrefs.Save();
        Debug.Log($"[NameplateUI] 💾 布局已保存: height={heightOffset}, lateral={lateralOffset}");
    }

    /// <summary>设置玩家名字</summary>
    public void SetName(string name)
    {
        if (nameText != null) nameText.text = name;
    }

    /// <summary>
    /// 设置血量（按 maxHp 自动换算填充比例）
    /// </summary>
    public void SetHp(int current)
    {
        if (fillImage == null) return;
        float ratio = maxHp > 0 ? Mathf.Clamp01((float)current / maxHp) : 0f;

        if (barMode == HpBarMode.Width)
        {
            // 改 Fill 宽度：配合父级 Horizontal Layout，血条从右侧收缩
            var size = fillImage.rectTransform.sizeDelta;
            size.x = _baseBarWidth * ratio;
            fillImage.rectTransform.sizeDelta = size;
        }
        else if (_useScaleDrive)
        {
            // FillAmount 模式无 Sprite 的兜底：横向缩放
            var localScale = fillImage.rectTransform.localScale;
            localScale.x = Mathf.Max(0.0001f, ratio);
            fillImage.rectTransform.localScale = localScale;
        }
        else
        {
            fillImage.fillAmount = ratio;
        }
    }
}
