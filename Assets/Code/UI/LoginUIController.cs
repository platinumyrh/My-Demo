using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using GameNetwork;        // 引用 Proto 消息命名空间（LoginReq, LoginResp 等）
// 🔑 注意：GameEventBus 是类名（不是命名空间），GameEventType 和 GameEventData 在全局命名空间
// 所以不需要 using GameEventBus; 直接写类名即可

/// <summary>
/// 登录界面控制器
/// 🔑 重构：通过 EventBus 订阅登录事件，不再直接依赖 NetworkManager 的委托
/// 职责：只负责 UI 交互 + 场景跳转，不涉及任何游戏对象
/// </summary>
public class LoginUIController : MonoBehaviour
{
    #region UI组件引用（在Inspector中拖拽赋值）

    [Header("输入框")]
    [Tooltip("账号/用户名输入框")]
    public InputField Input_Account;      // 账号输入框

    [Tooltip("密码输入框")]
    public InputField Input_Password;     // 密码输入框

    [Header("按钮")]
    [Tooltip("登录按钮")]
    public Button Btn_Log_in;                 // 登录按钮

    [Tooltip("游客登录按钮（预留，暂未实现）")]
    public Button Btn_Log_in_as_Tourist;      // 游客登录按钮

    [Header("提示信息")]
    [Tooltip("提示文本（显示错误信息、成功提示等）")]
    public Text Text_Message;      // 提示信息文本

    [Header("配置")]
    [Tooltip("登录成功后跳转的场景名称")]
    public string nextSceneName = "GameMenu";     // 登录成功后跳转的场景名

    [Tooltip("连接服务器的IP地址")]
    public string serverIP = "127.0.0.1";

    [Tooltip("服务器端口")]
    public int serverPort = 7777;

    #endregion

    #region 私有字段

    private bool _isProcessing = false;       // 是否正在处理登录请求（防止重复点击）

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        // 初始化UI组件引用（如果Inspector没拖拽，尝试自动查找）
        InitializeUIReferences();

        // 绑定按钮事件
        if (Btn_Log_in != null)
        {
            Btn_Log_in.onClick.AddListener(OnLoginButtonClick);
        }

        if (Btn_Log_in_as_Tourist != null)
        {
            Btn_Log_in_as_Tourist.onClick.AddListener(OnTouristLoginClick);
        }

        // 绑定回车键快捷登录
        if (Input_Password != null)
        {
            Input_Password.onEndEdit.AddListener((text) =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    OnLoginButtonClick();
                }
            });
        }

        // 🔑🔑🔑 关键改动：通过 EventBus 订阅登录事件（而非直接监听 NetworkManager 委托）
        var eventBus = GameEventBus.GetInstance();
        
        // 订阅登录成功事件
        eventBus.Subscribe<LoginSuccessEventData>(
            GameEventType.OnLoginSuccess,
            HandleLoginSuccessViaEventBus
        );
        
        // 订阅登录失败事件
        eventBus.Subscribe<LoginFailedEventData>(
            GameEventType.OnLoginFailed,
            HandleLoginFailedViaEventBus
        );

        // 显示初始提示
        ShowMessage("请输入账号和密码", Color.gray);

        Debug.Log("[LoginUI] ✅ 登录界面初始化完成");
    }

    private void OnDestroy()
    {
        // 取消所有事件监听，防止内存泄漏
        if (Btn_Log_in != null)
        {
            Btn_Log_in.onClick.RemoveListener(OnLoginButtonClick);
        }

        if (Btn_Log_in_as_Tourist != null)
        {
            Btn_Log_in_as_Tourist.onClick.RemoveListener(OnTouristLoginClick);
        }

        if (Input_Password != null)
        {
            Input_Password.onEndEdit.RemoveAllListeners();
        }

        // 🔑 取消 EventBus 订阅（防止内存泄漏）
        var eventBus = GameEventBus.GetInstance();
        
        eventBus.Unsubscribe<LoginSuccessEventData>(
            GameEventType.OnLoginSuccess,
            HandleLoginSuccessViaEventBus
        );
        
        eventBus.Unsubscribe<LoginFailedEventData>(
            GameEventType.OnLoginFailed,
            HandleLoginFailedViaEventBus
        );
        
        // （兼容性保留）取消旧的委托订阅
        // NetworkManager.Instance.OnLoginSuccess -= HandleLoginSuccess;
        // NetworkManager.Instance.OnLoginFailed -= HandleLoginFailed;

        Debug.Log("[LoginUI] 登录界面已销毁");
    }

    #endregion

    #region 公开方法 - UI交互

    /// <summary>
    /// 点击"登录"按钮的处理逻辑
    /// </summary>
    public void OnLoginButtonClick()
    {
        // 防止重复点击
        if (_isProcessing)
        {
            Debug.LogWarning("[LoginUI] 正在处理中，请勿重复点击");
            return;
        }

        // 获取输入内容
        string username = Input_Account != null ? Input_Account.text.Trim() : "";
        string password = Input_Password != null ? Input_Password.text : "";

        // 输入验证
        if (string.IsNullOrEmpty(username))
        {
            ShowMessage("⚠️ 请输入账号", Color.yellow);
            Input_Account?.Select();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowMessage("⚠️ 请输入密码", Color.yellow);
            Input_Password?.Select();
            return;
        }

        // 开始登录流程
        StartCoroutine(LoginCoroutine(username, password));
    }

    /// <summary>
    /// 点击"游客登录"按钮（预留接口，暂未实现）
    /// </summary>
    public void OnTouristLoginClick()
    {
        ShowMessage("🚧 游客登录功能开发中...", Color.yellow);
        Debug.Log("[LoginUI] 游客登录功能暂未实现");

        // TODO: 未来实现游客模式（自动生成临时ID）
        // NetworkManager.Instance.LoginAsTourist();
    }

    #endregion

    #region 私有方法 - 核心逻辑

    /// <summary>
    /// 初始化UI组件引用（自动查找子对象）
    /// </summary>
    private void InitializeUIReferences()
    {
        // 如果在Inspector中没有手动拖拽，尝试自动查找
        if (Input_Account == null)
        {
            Input_Account = transform.Find("PanelLogin/Input_Account")?.GetComponent<InputField>();
        }

        if (Input_Password == null)
        {
            Input_Password = transform.Find("PanelLogin/Input_Password")?.GetComponent<InputField>();
        }

        if (Btn_Log_in == null)
        {
            Btn_Log_in = transform.Find("PanelLogin/Btn_Log in")?.GetComponent<Button>();
        }

        if (Btn_Log_in_as_Tourist == null)
        {
            Btn_Log_in_as_Tourist = transform.Find("PanelLogin/Btn_Log in as Tourist")?.GetComponent<Button>();
        }

        if (Text_Message == null)
        {
            // 查找最后一个Text对象作为提示文本
            Transform panelLogin = transform.Find("PanelLogin");
            if (panelLogin != null)
            {
                Text_Message = panelLogin.Find("Text (Legacy)")?.GetComponent<Text>();
                if (Text_Message == null)
                {
                    // 尝试查找任何TextMeshProUGUI组件
                    Text_Message = panelLogin.GetComponentInChildren<Text>();
                }
            }
        }

        // 验证关键组件
        if (Input_Account == null || Input_Password == null || Btn_Log_in == null)
        {
            Debug.LogError("[LoginUI] ❌ 关键UI组件缺失！请在Inspector中手动拖拽赋值");
        }
    }

    /// <summary>
    /// 登录协程（异步处理连接+登录）
    /// </summary>
    private System.Collections.IEnumerator LoginCoroutine(string username, string password)
    {
        _isProcessing = true;
        SetInteractionEnabled(false);  // 禁用所有交互
        ShowMessage("正在连接服务器...", Color.white);

        // 步骤1：确保已连接服务器
        if (!NetworkManager.Instance.IsConnected)
        {
            bool connected = NetworkManager.Instance.Connect();

            if (!connected)
            {
                ShowMessage("❌ 无法连接到服务器", Color.red);
                SetInteractionEnabled(true);
                _isProcessing = false;
                yield break;
            }

            // 等待一小会儿确保UDP连接建立
            yield return new WaitForSeconds(0.3f);
        }

        // 步骤2：发送登录请求
        ShowMessage("正在验证账号...", Color.cyan);
        NetworkManager.Instance.Login(username, password);

        // 注意：不在这里等待结果，而是通过事件回调处理（HandleLoginSuccess / HandleLoginFailed）
        // 这样可以避免协程阻塞，同时保持代码清晰
    }

    /// <summary>
    /// 🔑 处理登录成功（通过 EventBus 接收的事件）
    /// 只负责 UI 显示和场景跳转，不涉及游戏对象创建
    /// </summary>
    private void HandleLoginSuccessViaEventBus(LoginSuccessEventData eventData)
    {
        Debug.Log($"[LoginUI] 🎉 收到 OnLoginSuccess 事件: {eventData}");
        
        ShowMessage($"✅ 登录成功！欢迎回来，{eventData.Username}", Color.green);

        // 延迟跳转场景（让用户看到成功提示）
        StartCoroutine(DelayedLoadScene());
    }
    
    /// <summary>
    /// （兼容性保留）旧的委托方式处理登录成功
    /// 后续可移除此方法
    /// </summary>
    private void HandleLoginSuccess(string userId, string nickname)
    {
        // 转调用新的 EventBus 方式
        Debug.Log("[LoginUI] ⚠️ 使用旧版 HandleLoginSuccess 委托（建议迁移到 EventBus）");
        ShowMessage($"✅ 登录成功！欢迎回来，{nickname}", Color.green);
        StartCoroutine(DelayedLoadScene());
    }

    /// <summary>
    /// 延迟加载下一个场景
    /// </summary>
    private IEnumerator DelayedLoadScene()
    {
        yield return new WaitForSeconds(1.0f);  // 等待1秒让用户看到成功提示

        if (!string.IsNullOrEmpty(nextSceneName))
        {
            Debug.Log($"[LoginUI] 🚀 正在跳转到场景: {nextSceneName}");
            UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            Debug.LogWarning("[LoginUI] 未设置目标场景名，无法跳转");
            SetInteractionEnabled(true);
            _isProcessing = false;
        }
    }

    /// <summary>
    /// 🔑 处理登录失败（通过 EventBus 接收的事件）
    /// </summary>
    private void HandleLoginFailedViaEventBus(LoginFailedEventData eventData)
    {
        Debug.Log($"[LoginUI] ❌ 收到 OnLoginFailed 事件: {eventData}");
        
        ShowMessage($"❌ {eventData.ErrorMessage}", Color.red);
        SetInteractionEnabled(true);
        _isProcessing = false;
    }
    
    /// <summary>
    /// （兼容性保留）旧的委托方式处理登录失败
    /// 后续可移除此方法
    /// </summary>
    private void HandleLoginFailed(string errorMessage)
    {
        Debug.Log("[LoginUI] ⚠️ 使用旧版 HandleLoginFailed 委托（建议迁移到 EventBus）");
        ShowMessage($"❌ {errorMessage}", Color.red);
        SetInteractionEnabled(true);
        _isProcessing = false;
    }

    #endregion

    #region 私有方法 - UI辅助

    /// <summary>
    /// 显示提示消息
    /// </summary>
    /// <param name="text">消息文本</param>
    /// <param name="color">文字颜色</param>
    private void ShowMessage(string text, Color color)
    {
        if (Text_Message != null)
        {
            Text_Message.text = text;
            Text_Message.color = color;
        }
        else
        {
            // 如果没有提示文本组件，用Debug输出
            Debug.Log($"[LoginUI] 💬 {text}");
        }
    }

    /// <summary>
    /// 设置所有交互元素是否可用
    /// </summary>
    /// <param name="enabled">是否启用</param>
    private void SetInteractionEnabled(bool enabled)
    {
        if (Btn_Log_in != null)
        {
            Btn_Log_in.interactable = enabled;
        }

        if (Btn_Log_in_as_Tourist != null)
        {
            Btn_Log_in_as_Tourist.interactable = enabled;
        }

        if (Input_Account != null)
        {
            Input_Account.enabled = enabled;
        }

        if (Input_Password != null)
        {
            Input_Password.enabled = enabled;
        }
    }

    #endregion
}
