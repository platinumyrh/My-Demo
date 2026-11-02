using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 回放专用切人面板：只显示当前视角名字、转发上/下一人。
/// 座位切换逻辑在 LockstepGameDriver，UI 不碰逻辑世界。
/// </summary>
public class ReplayViewSwitchUI : MonoBehaviour
{
    [SerializeField] private Text nameText;
    [SerializeField] private Button prevButton;
    [SerializeField] private Button nextButton;

    private LockstepGameDriver _driver;
    private bool _bound;

    private void Awake()
    {
        if (nameText == null) nameText = FindChild<Text>("Text_CurPlayerName");
        if (prevButton == null) prevButton = FindChild<Button>("Button_PrePlayer");
        if (nextButton == null) nextButton = FindChild<Button>("Button_NextPlayer");
    }

    private T FindChild<T>(string childName) where T : Component
    {
        var child = transform.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    /// <summary>回放开始时由驱动调用：绑按钮 + 刷新名字</summary>
    public void Bind(LockstepGameDriver driver)
    {
        _driver = driver;

        if (!_bound)
        {
            if (prevButton != null) prevButton.onClick.AddListener(OnPrev);
            if (nextButton != null) nextButton.onClick.AddListener(OnNext);
            _bound = true;
        }

        Refresh();
    }

    /// <summary>视角变化后由驱动调用</summary>
    public void Refresh()
    {
        if (nameText != null && _driver != null)
            nameText.text = _driver.ViewedPlayerName;
    }

    private void OnPrev() => _driver?.StepViewedSeat(-1);
    private void OnNext() => _driver?.StepViewedSeat(1);

    private void OnDestroy()
    {
        if (prevButton != null) prevButton.onClick.RemoveListener(OnPrev);
        if (nextButton != null) nextButton.onClick.RemoveListener(OnNext);
    }
}
