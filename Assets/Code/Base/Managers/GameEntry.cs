using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏入口管理器 - 统一管理游戏启动流程
/// 
/// 职责：
/// 1. 协调资源更新（ABUpdateManager）
/// 2. 初始化资源加载系统（ABManager + ResLoad）
/// 3. 加载启动场景/界面
/// 4. 管理全局生命周期
/// 
/// 使用方法：
/// 1. 在第一个场景中创建空物体，挂载此脚本
/// 2. 或者在代码中直接调用 GameEntry.GetInstance().InitGame()
/// </summary>
public class GameEntry : MonoBehaviour
{
    #region 单例实现

    private static GameEntry instance;

    public static GameEntry GetInstance()
    {
        return instance;
    }

    #endregion

    #region Inspector 配置

    [Header("启动配置")]
    [Tooltip("是否在Start时自动初始化")]
    [SerializeField] private bool autoInitOnStart = true;

    [Tooltip("初始化完成后自动加载的场景名（留空则不加载）")]
    [SerializeField] private string startSceneName = "";

    [Tooltip("初始化完成后自动实例化的UI路径（留空则不加载）")]
    [SerializeField] private string bootUIPath = "";

    [Tooltip("UI的父物体（通常是Canvas）")]
    [SerializeField] private Transform canvasTransform;

    [Header("调试选项")]
    [Tooltip("是否跳过资源检查（开发阶段可开启加速启动）")]
    [SerializeField] private bool skipUpdateCheck = false;

    [Tooltip("是否启用资源缓存")]
    [SerializeField] private bool enableResourceCache = false;

    #endregion

    #region 事件回调（Inspector可配置）

    [Header("事件回调")]
    [Tooltip("游戏初始化开始时触发")]
    public UnityAction onInitStart;

    [Tooltip("资源更新完成时触发")]
    public UnityAction onUpdateComplete;

    [Tooltip("资源系统就绪时触发")]
    public UnityAction onSystemReady;

    [Tooltip("游戏完全启动完成时触发")]
    public UnityAction onGameReady;

    #endregion

    #region 私有字段

    private bool isInitialized = false;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        // 单例设置
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 如果没有手动设置Canvas，尝试自动查找
        if (canvasTransform == null)
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                canvasTransform = canvas.transform;
            }
        }
    }

    private void Start()
    {
        if (autoInitOnStart)
        {
            InitGame();
        }
    }

    #endregion

    #region 公开方法 - 主入口

    /// <summary>
    /// 初始化游戏（完整流程）
    /// 流程：检查更新 → 初始化资源系统 → 加载启动内容
    /// </summary>
    public void InitGame()
    {
        if (isInitialized)
        {
            Debug.LogWarning("[GameEntry] 已经初始化过，重复调用被忽略");
            return;
        }

        Debug.Log("========================================");
        Debug.Log("=== 🎮 GameEntry: 游戏初始化开始 ===");
        Debug.Log("========================================");

        onInitStart?.Invoke();

        StartCoroutine(InitGameCoroutine());
    }

    /// <summary>
    /// 完整的初始化协程流程
    /// </summary>
    private IEnumerator InitGameCoroutine()
    {
        // ===== 第1步：资源更新检查 =====
        yield return Step_CheckResourceUpdate();

        // ===== 第2步：初始化资源系统 =====
        yield return Step_InitResourceSystem();

        // ===== 第3步：标记为已初始化 =====
        isInitialized = true;
        onSystemReady?.Invoke();

        Debug.Log("✅ [GameEntry] 资源系统就绪");

        // ===== 第4步：加载启动内容 =====
        yield return Step_LoadStartContent();

        // ===== 完成！=====
        onGameReady?.Invoke();

        Debug.Log("========================================");
        Debug.Log("=== 🎉 GameEntry: 游戏启动完成！===");
        Debug.Log("========================================");
    }

    #endregion

    #region 流程步骤 - 可单独调用或重写

    /// <summary>
    /// 步骤1：检查并执行资源更新
    /// </summary>
    private IEnumerator Step_CheckResourceUpdate()
    {
        Debug.Log("[GameEntry] [1/4] 检查资源更新...");

        if (skipUpdateCheck)
        {
            Debug.Log("[GameEntry] ⏭️ 跳过更新检查（开发模式）");
            yield break;
        }

        bool updateSuccess = false;

        // 使用协程版本的更新检查
        yield return ABUpdateManager.GetInstance().CheckUpdateCoroutine((progress) =>
        {
            // 这里可以显示进度到UI
            // updateText.text = progress;
            Debug.Log($"[GameEntry] 📥 {progress}");
        });

        // 简单判断：如果下载列表为空或者没有异常，视为成功
        updateSuccess = (ABUpdateManager.GetInstance().PendingDownloadCount == 0);

        onUpdateComplete?.Invoke();

        if (!updateSuccess && !skipUpdateCheck)
        {
            Debug.LogWarning("[GameEntry] ⚠️ 资源更新可能未完全成功，但继续启动...");
            // 可以选择在这里弹出重试对话框，或者强制退出
            // yield return ShowRetryDialog();
        }
    }

    /// <summary>
    /// 步骤2：初始化资源加载系统
    /// </summary>
    private IEnumerator Step_InitResourceSystem()
    {
        Debug.Log("[GameEntry] [2/4] 初始化资源系统...");

        // 初始化ABManager（加载主清单，启用依赖系统）
        yield return ABManager.GetInstance().Initialize(() =>
        {
            Debug.Log("[GameEntry] ✅ ABManager初始化完成");
        });

        // 配置ResLoad
        ResLoad.GetInstance().SetCacheEnabled(enableResourceCache);

        Debug.Log($"[GameEntry] ✅ ResLoad配置完成 (缓存:{enableResourceCache})");
    }

    /// <summary>
    /// 步骤3：加载启动场景和UI
    /// </summary>
    private IEnumerator Step_LoadStartContent()
    {
        Debug.Log("[GameEntry] [3-4/4] 加载启动内容...");

        // 加载启动场景（如果配置了的话）
        if (!string.IsNullOrEmpty(startSceneName))
        {
            Debug.Log($"[GameEntry] 加载启动场景: {startSceneName}");
            
            AsyncOperation sceneOp = SceneManager.LoadSceneAsync(startSceneName);
            
            if (sceneOp != null)
            {
                // 等待场景加载完成
                while (!sceneOp.isDone)
                {
                    // 可以显示加载进度
                    float progress = Mathf.Clamp01(sceneOp.progress / 0.9f);
                    // loadingBar.value = progress;
                    
                    yield return null;
                }
                
                Debug.Log($"[GameEntry] ✅ 场景加载完成: {startSceneName}");
            }
        }

        // 加载启动UI（如果配置了的话）
        if (!string.IsNullOrEmpty(bootUIPath) && canvasTransform != null)
        {
            Debug.Log($"[GameEntry] 加载启动UI: {bootUIPath}");

            GameObject bootUI = null;
            
            yield return ResLoad.GetInstance().InstantiateAsync(
                bootUIPath,
                canvasTransform,
                (result) =>
                {
                    bootUI = result;
                }
            );

            if (bootUI != null)
            {
                Debug.Log($"[GameEntry] ✅ 启动UI加载完成: {bootUI.name}");
            }
            else
            {
                Debug.LogError($"[GameEntry] ❌ 启动UI加载失败: {bootUIPath}");
            }
        }
    }

    #endregion

    #region 公开方法 - 快捷操作

    /// <summary>
    /// 重新初始化（清除所有状态后重新开始）
    /// </summary>
    public void Reinit()
    {
        isInitialized = false;
        
        // 卸载所有资源
        ResLoad.GetInstance().UnloadAll();
        
        // 重新初始化
        InitGame();
    }

    /// <summary>
    /// 强制完成初始化（跳过未完成的步骤）
    /// 慎用！只在特殊情况下使用
    /// </summary>
    public void ForceComplete()
    {
        if (!isInitialized)
        {
            isInitialized = true;
            StopAllCoroutines();
            
            Debug.LogWarning("[GameEntry] ⚠️ 强制完成初始化（可能存在未完成的步骤）");
            
            onSystemReady?.Invoke();
            onGameReady?.Invoke();
        }
    }

    /// <summary>
    /// 检查是否已完成初始化
    /// </summary>
    public bool IsInitialized => isInitialized;

    #endregion

    #region 调试方法

    /// <summary>
    /// 打印系统状态信息
    /// </summary>
    [ContextMenu("Debug/Print System Info")]
    public void PrintSystemInfo()
    {
        string info = $"========== GameEntry 系统状态 ==========\n";
        info += $"初始化状态: {(isInitialized ? "✅ 已完成" : "❌ 未完成")}\n";
        info += $"跳过更新: {skipUpdateCheck}\n";
        info += $"资源缓存: {enableResourceCache}\n";
        info += $"启动场景: {(string.IsNullOrEmpty(startSceneName) ? "未设置" : startSceneName)}\n";
        info += $"启动UI: {(string.IsNullOrEmpty(bootUIPath) ? "未设置" : bootUIPath)}\n\n";
        info += ABManager.GetInstance().GetMemoryInfo();
        info += "\n=============================================";

        Debug.Log(info);
    }

    /// <summary>
    /// 测试资源加载（开发调试用）
    /// </summary>
    [ContextMenu("Debug/Test Load Resource")]
    public void TestLoadResource()
    {
        if (!isInitialized)
        {
            Debug.LogError("[GameEntry] 系统尚未初始化！");
            return;
        }

        // 测试加载一个资源（根据你的实际路径修改）
        string testPath = "Test/TestCube"; // 改成你的测试资源路径
        
        Debug.Log($"[GameEntry] 测试加载资源: {testPath}");
        
        GameObject testObj = ResLoad.GetInstance().LoadResource<GameObject>(testPath);
        
        if (testObj != null)
        {
            Instantiate(testObj);
            Debug.Log($"[GameEntry] ✅ 测试成功！已实例化: {testObj.name}");
        }
        else
        {
            Debug.LogError($"[GameEntry] ❌ 测试失败！无法加载: {testPath}");
        }
    }

    #endregion
}
