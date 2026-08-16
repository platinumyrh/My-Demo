using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// AB包管理器 - 负责AssetBundle的加载、卸载、缓存和依赖管理
/// 继承BaseManager，符合项目架构规范
/// </summary>
public class ABManager : BaseManager<ABManager>
{
    #region 私有字段

    // 已加载的AB包缓存字典 key:AB包名 value:AssetBundle对象
    private Dictionary<string, AssetBundle> loadedBundles;

    // AB包引用计数字典 key:AB包名 value:引用次数
    private Dictionary<string, int> bundleRefCounts;

    // AB包依赖关系字典 key:AB包名 value:依赖的AB包名列表
    private Dictionary<string, List<string>> bundleDependencies;

    // AssetBundle主清单（用于查询依赖关系）
    private AssetBundleManifest manifest;

    // 主清单AB包是否已加载
    private bool isManifestLoaded;

    // AB包根路径（优先从热更新目录加载）
    private string basePath;

    // 正在异步加载中的AB包（防止重复加载）
    private HashSet<string> loadingBundles;

    #endregion

    #region 构造函数

    /// <summary>
    /// 私有构造函数，防止外部实例化
    /// </summary>
    public ABManager()
    {
        loadedBundles = new Dictionary<string, AssetBundle>();
        bundleRefCounts = new Dictionary<string, int>();
        bundleDependencies = new Dictionary<string, List<string>>();
        loadingBundles = new HashSet<string>();

        // 设置AB包基础路径为持久化目录
        basePath = Application.persistentDataPath + "/";

        isManifestLoaded = false;
        manifest = null;

        Debug.Log("[ABManager] 初始化完成");
    }

    #endregion

    #region 公开方法 - 初始化

    /// <summary>
    /// 初始化ABManager，加载主清单文件
    /// 必须在游戏启动时调用一次（通常在热更新完成后调用）
    /// </summary>
    /// <param name="onComplete">初始化完成回调</param>
    public IEnumerator Initialize(UnityAction onComplete = null)
    {
        Debug.Log("[ABManager] 开始初始化...");

        // 加载主清单文件
        yield return MonoManager.GetInstance().StartCoroutine(LoadManifest());

        if (isManifestLoaded)
        {
            Debug.Log($"[ABManager] 初始化成功，主清单已加载");
            onComplete?.Invoke();
        }
        else
        {
            Debug.LogWarning("[ABManager] 初始化完成，但未找到主清单文件（可能是首次运行或无依赖）");
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// 同步初始化（不使用协程的简化版本）
    /// </summary>
    public void InitializeSync()
    {
        Debug.Log("[ABManager] 同步初始化...");

        string manifestPath = GetFullPath(ABConfig.ManifestFileName);
        
        if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
        {
            try
            {
                AssetBundle manifestBundle = AssetBundle.LoadFromFile(manifestPath);
                manifest = manifestBundle.LoadAsset<AssetBundleManifest>(ABConfig.ManifestAssetName);
                
                if (manifest != null)
                {
                    isManifestLoaded = true;
                    Debug.Log("[ABManager] 主清单加载成功");
                }
                else
                {
                    Debug.LogWarning("[ABManager] 主清单资源为空");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABManager] 主清单加载失败: {e.Message}");
            }
        }
        else
        {
            Debug.Log("[ABManager] 未找到主清单文件，跳过依赖检查");
        }
    }

    #endregion

    #region 公开方法 - 加载AB包

    /// <summary>
    /// 同步加载单个AB包
    /// </summary>
    /// <param name="abName">AB包名称（如 "prefabs.ab"）</param>
    /// <returns>加载成功的AssetBundle对象，失败返回null</returns>
    public AssetBundle LoadBundle(string abName)
    {
        if (string.IsNullOrEmpty(abName))
        {
            Debug.LogError("[ABManager] AB包名称不能为空！");
            return null;
        }

        // 标准化AB包名（确保小写且带.ab后缀）
        abName = NormalizeABName(abName);

        // 1. 检查是否已经加载过
        if (loadedBundles.ContainsKey(abName))
        {
            AddReference(abName);
            Debug.Log($"[ABManager] AB包已缓存，增加引用: {abName} (引用数: {bundleRefCounts[abName]})");
            return loadedBundles[abName];
        }

        // 2. 检查是否正在异步加载中
        if (loadingBundles.Contains(abName))
        {
            Debug.LogWarning($"[ABManager] AB包正在异步加载中，请等待: {abName}");
            return null;
        }

        // 3. 加载依赖项
        LoadDependencies(abName);

        // 4. 获取完整路径并加载
        string fullPath = GetFullPath(abName);
        if (string.IsNullOrEmpty(fullPath))
        {
            Debug.LogError($"[ABManager] 找不到AB包: {abName}");
            return null;
        }

        try
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(fullPath);
            
            if (bundle != null)
            {
                // 缓存AB包
                loadedBundles.Add(abName, bundle);
                bundleRefCounts.Add(abName, 1);
                
                Debug.Log($"[ABManager] AB包加载成功: {abName} (路径: {fullPath})");
                return bundle;
            }
            else
            {
                Debug.LogError($"[ABManager] AB包加载失败（返回null）: {abName}");
                return null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ABManager] AB包加载异常: {abName}, 错误: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 异步加载单个AB包
    /// </summary>
    /// <param name="abName">AB包名称</param>
    /// <param name="onComplete">加载完成回调 (AssetBundle)</param>
    /// <returns>协程</returns>
    public IEnumerator LoadBundleAsync(string abName, UnityAction<AssetBundle> onComplete)
    {
        if (string.IsNullOrEmpty(abName))
        {
            Debug.LogError("[ABManager] AB包名称不能为空！");
            onComplete?.Invoke(null);
            yield break;
        }

        // 标准化AB包名
        abName = NormalizeABName(abName);

        // 1. 检查是否已经加载过
        if (loadedBundles.ContainsKey(abName))
        {
            AddReference(abName);
            Debug.Log($"[ABManager] AB包已缓存（异步）: {abName}");
            onComplete?.Invoke(loadedBundles[abName]);
            yield break;
        }

        // 2. 检查是否正在加载中（防止重复加载）
        if (loadingBundles.Contains(abName))
        {
            Debug.LogWarning($"[ABManager] AB包已在加载队列中: {abName}");
            yield return new WaitUntil(() => !loadingBundles.Contains(abName));
            onComplete?.Invoke(loadedBundles.ContainsKey(abName) ? loadedBundles[abName] : null);
            yield break;
        }

        // 3. 标记为正在加载
        loadingBundles.Add(abName);

        // 4. 先加载依赖项
        List<string> dependencies = GetDependencies(abName);
        if (dependencies != null && dependencies.Count > 0)
        {
            foreach (string depAbName in dependencies)
            {
                if (!IsBundleLoaded(depAbName))
                {
                    bool depLoaded = false;
                    yield return MonoManager.GetInstance().StartCoroutine(LoadBundleAsync(depAbName, (depBundle) =>
                    {
                        depLoaded = (depBundle != null);
                    }));

                    if (!depLoaded)
                    {
                        Debug.LogError($"[ABManager] 依赖包加载失败: {depAbName}");
                        loadingBundles.Remove(abName);
                        onComplete?.Invoke(null);
                        yield break;
                    }
                }
                else
                {
                    AddReference(depAbName);
                }
            }
        }

        // 5. 获取完整路径
        string fullPath = GetFullPath(abName);
        if (string.IsNullOrEmpty(fullPath))
        {
            Debug.LogError($"[ABManager] 找不到AB包: {abName}");
            loadingBundles.Remove(abName);
            onComplete?.Invoke(null);
            yield break;
        }

        // 6. 异步加载AB包
        AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(fullPath);
        yield return request;

        if (request.assetBundle != null)
        {
            AssetBundle bundle = request.assetBundle;

            // 缓存AB包
            loadedBundles.Add(abName, bundle);
            bundleRefCounts.Add(abName, 1);

            // 移除加载标记
            loadingBundles.Remove(abName);

            Debug.Log($"[ABManager] AB包异步加载成功: {abName}");
            onComplete?.Invoke(bundle);
        }
        else
        {
            Debug.LogError($"[ABManager] AB包异步加载失败: {abName}");
            loadingBundles.Remove(abName);
            onComplete?.Invoke(null);
        }
    }

    #endregion

    #region 公开方法 - 卸载AB包

    /// <summary>
    /// 卸载指定AB包（基于引用计数）
    /// 当引用计数降为0时真正卸载
    /// </summary>
    /// <param name="abName">AB包名称</param>
    /// <param name="unloadAllLoadedObjects">是否同时卸载所有已加载的资源对象</param>
    public void UnloadBundle(string abName, bool unloadAllLoadedObjects = false)
    {
        if (string.IsNullOrEmpty(abName)) return;

        abName = NormalizeABName(abName);

        if (!loadedBundles.ContainsKey(abName))
        {
            Debug.LogWarning($"[ABManager] 尝试卸载未加载的AB包: {abName}");
            return;
        }

        // 减少引用计数
        RemoveReference(abName);

        int currentRefCount = bundleRefCounts[abName];
        Debug.Log($"[ABManager] AB包引用减少: {abName} (当前引用数: {currentRefCount})");

        // 引用计数为0时真正卸载
        if (currentRefCount <= 0)
        {
            // 卸载依赖项
            UnloadDependencies(abName);

            // 真正卸载AB包
            loadedBundles[abName].Unload(unloadAllLoadedObjects);
            loadedBundles.Remove(abName);
            bundleRefCounts.Remove(abName);
            bundleDependencies.Remove(abName);

            Debug.Log($"[ABManager] AB包已完全卸载: {abName}");
        }
    }

    /// <summary>
    /// 强制卸载所有AB包（慎用！通常在切换场景时调用）
    /// </summary>
    /// <param name="unloadAllLoadedObjects">是否卸载所有资源对象</param>
    public void UnloadAll(bool unloadAllLoadedObjects = false)
    {
        Debug.Log($"[ABManager] 开始卸载所有AB包 (共{loadedBundles.Count}个)...");

        foreach (var kvp in loadedBundles)
        {
            kvp.Value.Unload(unloadAllLoadedObjects);
            Debug.Log($"[ABManager] 卸载AB包: {kvp.Key}");
        }

        loadedBundles.Clear();
        bundleRefCounts.Clear();
        bundleDependencies.Clear();

        Debug.Log("[ABManager] 所有AB包已卸载完成");
    }

    #endregion

    #region 公开方法 - 查询

    /// <summary>
    /// 检查指定AB包是否已加载
    /// </summary>
    /// <param name="abName">AB包名称</param>
    /// <returns>是否已加载</returns>
    public bool IsBundleLoaded(string abName)
    {
        if (string.IsNullOrEmpty(abName)) return false;
        return loadedBundles.ContainsKey(NormalizeABName(abName));
    }

    /// <summary>
    /// 获取已加载的AB包对象
    /// </summary>
    /// <param name="abName">AB包名称</param>
    /// <returns>AssetBundle对象，未加载则返回null</returns>
    public AssetBundle GetLoadedBundle(string abName)
    {
        if (string.IsNullOrEmpty(abName)) return null;
        
        abName = NormalizeABName(abName);
        return loadedBundles.ContainsKey(abName) ? loadedBundles[abName] : null;
    }

    /// <summary>
    /// 获取指定AB包的引用计数
    /// </summary>
    /// <param name="abName">AB包名称</param>
    /// <returns>引用计数，未加载返回-1</returns>
    public int GetBundleRefCount(string abName)
    {
        if (string.IsNullOrEmpty(abName)) return -1;
        
        abName = NormalizeABName(abName);
        return bundleRefCounts.ContainsKey(abName) ? bundleRefCounts[abName] : -1;
    }

    /// <summary>
    /// 获取所有已加载的AB包名称列表
    /// </summary>
    /// <returns>AB包名称列表</returns>
    public List<string> GetAllLoadedBundleNames()
    {
        return new List<string>(loadedBundles.Keys);
    }

    /// <summary>
    /// 获取当前内存占用信息（调试用）
    /// </summary>
    /// <returns>格式化的内存信息字符串</returns>
    public string GetMemoryInfo()
    {
        long totalSize = 0;
        foreach (var bundle in loadedBundles.Values)
        {
            // 注意：Unity没有直接获取AB包大小的方法，这里仅统计数量
        }
        
        return $"[ABManager 内存信息]\n" +
               $"已加载AB包数量: {loadedBundles.Count}\n" +
               $"正在加载中数量: {loadingBundles.Count}\n" +
               $"主清单状态: {(isManifestLoaded ? "已加载 ✅" : "未加载 ❌")}";
    }

    #endregion

    #region 内部方法 - 路径处理

    /// <summary>
    /// 获取AB包的完整文件路径
    /// 优先从persistentDataPath（热更新目录）查找，其次从StreamingAssets查找
    /// </summary>
    /// <param name="abName">AB包名称</param>
    /// <returns>完整路径，找不到返回null</returns>
    private string GetFullPath(string abName)
    {
        // 1. 优先从热更新目录（persistentDataPath）查找
        string hotUpdatePath = basePath + abName;
        if (File.Exists(hotUpdatePath))
        {
            return hotUpdatePath;
        }

        // 2. 其次从初始资源目录（StreamingAssets）查找
        string streamingPath = Application.streamingAssetsPath + "/" + abName;
        if (File.Exists(streamingPath))
        {
            return streamingPath;
        }

        // 3. 都找不到
        Debug.LogWarning($"[ABManager] 在以下位置均未找到AB包 '{abName}':\n" +
                        $"  - {hotUpdatePath}\n" +
                        $"  - {streamingPath}");
        return null;
    }

    /// <summary>
    /// 标准化AB包名称（统一小写）
    /// 
    /// 重要说明：
    /// Unity打包的AB包文件默认没有扩展名（如 "test" 而不是 "test.ab"）
    /// 所以这里不再强制添加 .ab 后缀，保持原始名称
    /// 
    /// 如果你的打包规则会给AB包添加 .ab 后缀，可以取消下面的注释
    /// </summary>
    private string NormalizeABName(string abName)
    {
        if (string.IsNullOrEmpty(abName)) return abName;

        abName = abName.ToLower().Trim();
        
        // 注意：Unity默认打包的AB包没有扩展名
        // 如果你的打包工具会添加 .ab 后缀，可以启用下面这行：
        // if (!abName.EndsWith(".ab")) { abName += ".ab"; }

        return abName;
    }

    #endregion

    #region 内部方法 - 依赖处理

    /// <summary>
    /// 加载指定AB包的所有依赖项（同步版本）
    /// </summary>
    private void LoadDependencies(string abName)
    {
        List<string> dependencies = GetDependencies(abName);
        
        if (dependencies == null || dependencies.Count == 0) return;

        // 记录依赖关系到字典
        bundleDependencies[abName] = dependencies;

        // 递归加载每个依赖项
        foreach (string depAbName in dependencies)
        {
            if (!IsBundleLoaded(depAbName))
            {
                LoadBundle(depAbName); // 递归加载
            }
            else
            {
                // 已经加载了，只增加引用计数
                AddReference(depAbName);
            }
        }
    }

    /// <summary>
    /// 获取指定AB包的所有依赖项列表
    /// </summary>
    private List<string> GetDependencies(string abName)
    {
        if (manifest == null || !isManifestLoaded) return null;

        try
        {
            string[] deps = manifest.GetAllDependencies(abName);
            if (deps == null || deps.Length == 0) return null;

            return new List<string>(deps);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ABManager] 获取依赖失败 ({abName}): {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 卸载指定AB包的依赖项
    /// </summary>
    private void UnloadDependencies(string abName)
    {
        if (!bundleDependencies.ContainsKey(abName)) return;

        List<string> dependencies = bundleDependencies[abName];
        foreach (string depAbName in dependencies)
        {
            UnloadBundle(depAbName);
        }
    }

    #endregion

    #region 内部方法 - 引用计数

    /// <summary>
    /// 增加AB包的引用计数
    /// </summary>
    private void AddReference(string abName)
    {
        if (bundleRefCounts.ContainsKey(abName))
        {
            bundleRefCounts[abName]++;
        }
        else
        {
            bundleRefCounts[abName] = 1;
        }
    }

    /// <summary>
    /// 减少AB包的引用计数
    /// </summary>
    private void RemoveReference(string abName)
    {
        if (bundleRefCounts.ContainsKey(abName))
        {
            bundleRefCounts[abName]--;
        }
    }

    #endregion

    #region 内部方法 - 清单加载

    /// <summary>
    /// 加载AssetBundle主清单文件（用于获取依赖关系）
    /// </summary>
    private IEnumerator LoadManifest()
    {
        string manifestBundlePath = GetFullPath(ABConfig.ManifestFileName);
        
        if (string.IsNullOrEmpty(manifestBundlePath))
        {
            Debug.LogWarning("[ABManager] 未找到主清单AB包，将不启用依赖检查");
            isManifestLoaded = false;
            yield break;
        }

        // 异步加载主清单AB包
        AssetBundleCreateRequest manifestRequest = AssetBundle.LoadFromFileAsync(manifestBundlePath);
        yield return manifestRequest;

        if (manifestRequest.assetBundle == null)
        {
            Debug.LogError("[ABManager] 主清单AB包加载失败！");
            yield break;
        }

        // 从主清单AB包中加载Manifest资源
        AssetBundleRequest manifestAssetRequest = manifestRequest.assetBundle.LoadAssetAsync<AssetBundleManifest>(ABConfig.ManifestAssetName);
        yield return manifestAssetRequest;

        if (manifestAssetRequest.asset == null)
        {
            Debug.LogError("[ABManager] 主清单资源加载失败！");
            manifestRequest.assetBundle.Unload(false);
            yield break;
        }

        // 保存manifest引用（不要卸载这个AB包！）
        manifest = manifestAssetRequest.asset as AssetBundleManifest;
        isManifestLoaded = true;

        Debug.Log("[ABManager] 主清单加载成功，依赖系统已启用");
    }

    #endregion
}
