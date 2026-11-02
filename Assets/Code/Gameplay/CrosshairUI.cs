using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 俯视角准星（挂 Main 场景 Canvas 下的 Crosshair 物体）
/// 职责单一：跟随鼠标 + 集中提供各反馈效果入口，自己不做任何战斗逻辑
/// 效果子物体结构（名字约定，可拖拽覆盖）：
///   Crosshair/
///   ├─ HitMarker    命中反馈（短暂显示后隐藏）
///   ├─ KillMarker   击杀反馈
///   └─ ReloadRing   换弹进度环（Image.type = Filled）
/// 后续加新效果 = 加一个子物体 + 一个 Show 方法
/// </summary>
public class CrosshairUI : MonoBehaviour
{
    public static CrosshairUI Instance { get; private set; }

    [Header("效果子物体（按名字自动查找，可手动拖拽覆盖）")]
    [Tooltip("命中反馈")]
    public GameObject hitMarker;
    [Tooltip("击杀反馈")]
    public GameObject killMarker;
    [Tooltip("换弹进度环（Image 需设为 Filled）")]
    public Image reloadRing;

    [Header("表现参数")]
    [Tooltip("命中/击杀反馈的显示时长（秒）")]
    public float feedbackDuration = 0.15f;

    private RectTransform _rect;
    private Canvas _canvas;
    private Coroutine _hitCo;
    private Coroutine _killCo;

    private void Awake()
    {
        Instance = this;
        _rect = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();

        // 按名字自动查找子效果物体
        var t = transform;
        if (hitMarker == null) { var c = t.Find("HitMarker"); hitMarker = c ? c.gameObject : null; }
        if (killMarker == null) { var c = t.Find("KillMarker"); killMarker = c ? c.gameObject : null; }
        if (reloadRing == null) { var c = t.Find("ReloadRing"); reloadRing = c ? c.GetComponent<Image>() : null; }

        SetActive(hitMarker, false);
        SetActive(killMarker, false);
        SetActive(reloadRing?.gameObject, false);
    }

    private void OnEnable()
    {
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        Cursor.visible = true;
    }

    private void Update()
    {
        if (_rect == null || _canvas == null) return;

        Vector2 mouse = GetMousePosition();

        // 屏幕坐标 → Canvas 本地坐标（Overlay 模式 camera 传 null）
        Camera uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rect.parent as RectTransform, mouse, uiCamera, out Vector2 localPoint))
        {
            _rect.anchoredPosition = localPoint;
        }
    }

    #region 反馈效果入口

    /// <summary>命中反馈</summary>
    public void ShowHit()
    {
        ShowTransient(hitMarker, ref _hitCo);
    }

    /// <summary>击杀反馈</summary>
    public void ShowKill()
    {
        ShowTransient(killMarker, ref _killCo);
    }

    /// <summary>换弹进度环显隐 + 进度（0..1）</summary>
    public void SetReloadProgress(float progress)
    {
        if (reloadRing == null) return;

        bool show = progress is >= 0f and < 1f;
        SetActive(reloadRing.gameObject, show);
        if (show) reloadRing.fillAmount = progress;
    }

    /// <summary>显示一个短暂效果并自动隐藏</summary>
    private void ShowTransient(GameObject marker, ref Coroutine cache)
    {
        if (marker == null) return;

        if (cache != null) StopCoroutine(cache);
        cache = StartCoroutine(HideAfter(marker, feedbackDuration));
    }

    private IEnumerator HideAfter(GameObject marker, float delay)
    {
        SetActive(marker, true);
        yield return new WaitForSeconds(delay);
        SetActive(marker, false);
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    #endregion

    private static Vector2 GetMousePosition()
    {
        if (Mouse.current != null) return Mouse.current.position.ReadValue();

#if ENABLE_LEGACY_INPUT_MANAGER
        return UnityEngine.Input.mousePosition;
#else
        return Vector2.zero;
#endif
    }
}
