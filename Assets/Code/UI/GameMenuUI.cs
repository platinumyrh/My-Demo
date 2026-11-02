using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 局内 Esc 菜单：开关面板 + 离开对局。
/// 不暂停时间——帧同步下本机停推进、服务端仍按墙钟封帧，
/// 错过的权威帧不会补发，恢复后 ConfirmFrame 会永久卡住。
/// </summary>
public class GameMenuUI : MonoBehaviour
{
    public static GameMenuUI Instance { get; private set; }

    [Tooltip("菜单面板（留空自动找子物体 Panel_GameMenu）")]
    [SerializeField] private GameObject panel;

    [Tooltip("恢复游戏按钮（留空按名字自动查找）")]
    [SerializeField] private Button restoreButton;

    [Tooltip("退出游戏按钮（留空按名字自动查找）")]
    [SerializeField] private Button exitButton;

    [Tooltip("退出后返回的场景")]
    [SerializeField] private string lobbySceneName = "Lobby";

    public bool IsOpen => panel != null && panel.activeSelf;

    private bool _leaving;

    /// <summary>场景里没有手动挂组件时，由 GameInitializer 调到 Canvas 上</summary>
    public static void Ensure()
    {
        if (Instance != null) return;

        var canvas = GameObject.Find("Canvas_ScreenSpace");
        if (canvas == null)
        {
            Debug.LogWarning("[GameMenuUI] ⚠️ 没找到 Canvas_ScreenSpace");
            return;
        }
        canvas.AddComponent<GameMenuUI>();
    }

    private void Awake()
    {
        Instance = this;

        if (panel == null)
        {
            var t = transform.Find("Panel_GameMenu");
            if (t != null) panel = t.gameObject;
        }
        if (panel == null)
        {
            Debug.LogWarning("[GameMenuUI] ⚠️ 没找到 Panel_GameMenu");
            return;
        }

        if (restoreButton == null)
            restoreButton = panel.transform.Find("Button_Restore")?.GetComponent<Button>();
        if (exitButton == null)
            exitButton = panel.transform.Find("Button_Exit")?.GetComponent<Button>();

        if (restoreButton != null) restoreButton.onClick.AddListener(Close);
        if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);

        panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (restoreButton != null) restoreButton.onClick.RemoveListener(Close);
        if (exitButton != null) exitButton.onClick.RemoveListener(OnExitClicked);
    }

    private void Update()
    {
        if (_leaving) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetOpen(!IsOpen);
    }

    private void SetOpen(bool open)
    {
        if (panel == null) return;
        panel.SetActive(open);

        Cursor.visible = open;
        if (CrosshairUI.Instance != null)
            CrosshairUI.Instance.gameObject.SetActive(!open);
    }

    private void Close() => SetOpen(false);

    private void OnExitClicked()
    {
        if (_leaving) return;
        _leaving = true;
        if (exitButton != null) exitButton.interactable = false;

        // 发出离房请求后直接回大厅：Main 场景没有房间 UI，
        // 常驻的 NetworkManager/RoomManager 会继续消化 S2CLeaveRoom。
        // 服务端收到后把该玩家移出房间，并把 lockstep 座位标为掉线（之后补空指令）。
        RoomManager.GetInstance()?.LeaveRoom();
        SceneManager.LoadScene(lobbySceneName);
    }
}
