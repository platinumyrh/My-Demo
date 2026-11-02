using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

/// <summary>
/// 资源加载管理器 - 对外提供统一的资源加载API
/// 继承BaseManager，符合项目架构规范
/// 
/// 使用示例：
/// // 同步加载
/// GameObject prefab = ResLoad.GetInstance().LoadResource<GameObject>("Prefabs/Enemy");
/// 
/// // 异步加载
/// yield return ResLoad.GetInstance().LoadResourceAsync<GameObject>("UI/MainPanel", (obj) => { ... });
/// 
/// // 一键实例化
/// GameObject enemy = ResLoad.GetInstance().Instantiate("Prefabs/Enemy", transform);
/// </summary>
public class ResLoad : BaseManager<ResLoad>
{
    #region 私有字段

    // 已加载的资源实例缓存（可选，用于避免重复LoadAsset）
    private Dictionary<string, Object> resourceCache;

    // 是否启用资源缓存（默认关闭，按需开启）
    private bool enableCache;

    #endregion

    #region 构造函数

    /// <summary>
    /// 私有构造函数
    /// </summary>
    public ResLoad()
    {
        resourceCache = new Dictionary<string, Object>();
        enableCache = false; // 默认关闭缓存，由外部根据需要开启

        Debug.Log("[ResLoad] 初始化完成");
    }

    #endregion

    #region 配置方法

    /// <summary>
    /// 设置是否启用资源缓存
    /// 开启后，重复加载同一资源会从缓存返回（不重新调用LoadAsset）
    /// </summary>
    /// <param name="enable">是否启用</param>
    public void SetCacheEnabled(bool enable)
    {
        enableCache = enable;
        Debug.Log($"[ResLoad] 资源缓存: {(enable ? "已启用 ✅" : "已禁用 ❌")}");
    }

    /// <summary>
    /// 清空资源缓存（不会卸载AB包）
    /// </summary>
    public void ClearCache()
    {
        resourceCache.Clear();
        Debug.Log("[ResLoad] 资源缓存已清空");
    }

    #endregion

    #region 核心方法 - 同步加载

    /// <summary>
    /// 同步加载资源（核心方法）
    /// </summary>
    /// <typeparam name="T">资源类型（GameObject, Texture2D, AudioClip等）</typeparam>
    /// <param name="path">资源路径（格式："文件夹类型/资源名"，如 "Prefabs/Enemy"）</param>
    /// <returns>加载的资源对象，失败返回null</returns>
    public T LoadResource<T>(string path) where T : Object
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[ResLoad] 资源路径不能为空！");
            return null;
        }

        // 1. 检查缓存
        string cacheKey = $"{typeof(T).Name}_{path}";
        if (enableCache && resourceCache.ContainsKey(cacheKey))
        {
            return resourceCache[cacheKey] as T;
        }

        // 2. 解析路径 → AB包名 + 资源名
        string abName = ParseABName(path);
        string assetName = ParseAssetName(path);

        // 3. 加载AB包
        AssetBundle bundle = ABManager.GetInstance().LoadBundle(abName);
        if (bundle == null)
        {
            Debug.LogError($"[ResLoad] AB包加载失败: {abName} (资源路径: {path})");
            return null;
        }

        // 4. 从AB包中加载资源
        T asset = bundle.LoadAsset<T>(assetName);

        if (asset == null)
        {
            Debug.LogError($"[ResLoad] 从AB包中加载资源失败！\n" +
                          $"  AB包: {abName}\n" +
                          $"  资源名: {assetName}\n" +
                          $"  原始路径: {path}\n" +
                          $"  请检查：\n" +
                          $"    1. AB包中是否包含该资源\n" +
                          $"    2. 资源名称是否正确（注意大小写和扩展名）");
            return null;
        }

        // 5. 存入缓存
        if (enableCache)
        {
            resourceCache[cacheKey] = asset;
        }

        Debug.Log($"[ResLoad] 资源加载成功: {path} → [{typeof(T).Name}] {asset.name}");
        return asset;
    }

    /// <summary>
    /// 同步加载资源（不指定类型，返回Object）
    /// </summary>
    /// <param name="path">资源路径</param>
    /// <returns>资源对象</returns>
    public Object LoadResource(string path)
    {
        return LoadResource<Object>(path);
    }

    #endregion

    #region 核心方法 - 异步加载

    /// <summary>
    /// 异步加载资源（核心方法）
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="path">资源路径</param>
    /// <param name="onComplete">加载完成回调 (T asset)</param>
    /// <returns>协程（需要用StartCoroutine调用或yield return）</returns>
    public IEnumerator LoadResourceAsync<T>(string path, UnityAction<T> onComplete) where T : Object
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[ResLoad] 资源路径不能为空！");
            onComplete?.Invoke(null);
            yield break;
        }

        // 1. 检查缓存
        string cacheKey = $"{typeof(T).Name}_{path}";
        if (enableCache && resourceCache.ContainsKey(cacheKey))
        {
            onComplete?.Invoke(resourceCache[cacheKey] as T);
            yield break;
        }

        // 2. 解析路径
        string abName = ParseABName(path);
        string assetName = ParseAssetName(path);

        // 3. 异步加载AB包
        AssetBundle bundle = null;
        bool bundleLoaded = false;

        yield return MonoManager.GetInstance().StartCoroutine(ABManager.GetInstance().LoadBundleAsync(abName, (b) =>
        {
            bundle = b;
            bundleLoaded = (b != null);
        }));

        if (!bundleLoaded || bundle == null)
        {
            Debug.LogError($"[ResLoad] AB包异步加载失败: {abName}");
            onComplete?.Invoke(null);
            yield break;
        }

        // 4. 异步加载资源
        AssetBundleRequest request = bundle.LoadAssetAsync<T>(assetName);
        yield return request;

        if (request.asset == null)
        {
            Debug.LogError($"[ResLoad] 资源异步加载失败！AB包: {abName}, 资源名: {assetName}");
            onComplete?.Invoke(null);
            yield break;
        }

        T asset = request.asset as T;

        // 5. 存入缓存
        if (enableCache)
        {
            resourceCache[cacheKey] = asset;
        }

        Debug.Log($"[ResLoad] 资源异步加载成功: {path}");
        onComplete?.Invoke(asset);
    }

    /// <summary>
    /// 异步加载资源（不指定类型）
    /// </summary>
    /// <param name="path">资源路径</param>
    /// <param name="onComplete">回调</param>
    /// <returns>协程</returns>
    public IEnumerator LoadResourceAsync(string path, UnityAction<Object> onComplete)
    {
        return LoadResourceAsync<Object>(path, onComplete);
    }

    #endregion

    #region 便捷方法 - 实例化

    /// <summary>
    /// 一键实例化资源（同步版本）
    /// 自动处理：加载 → 实例化 → 返回GameObject
    /// </summary>
    /// <param name="path">资源路径（必须是Prefab）</param>
    /// <param name="parent">父物体（可选）</param>
    /// <param name="position">位置（可选）</param>
    /// <param name="rotation">旋转（可选）</param>
    /// <returns>实例化的GameObject</returns>
    public GameObject Instantiate(
        string path,
        Transform parent = null,
        Vector3? position = null,
        Quaternion? rotation = null)
    {
        GameObject prefab = LoadResource<GameObject>(path);
        
        if (prefab == null)
        {
            Debug.LogError($"[ResLoad] 实例化失败，无法加载Prefab: {path}");
            return null;
        }

        Vector3 pos = position ?? Vector3.zero;
        Quaternion rot = rotation ?? Quaternion.identity;

        GameObject instance = UnityEngine.Object.Instantiate(prefab, pos, rot, parent);
        instance.name = prefab.name; // 去掉"(Clone)"后缀

        Debug.Log($"[ResLoad] 实例化成功: {path} → {instance.name}");
        return instance;
    }

    /// <summary>
    /// 一键实例化资源（异步版本）
    /// </summary>
    /// <param name="path">资源路径</param>
    /// <param name="parent">父物体</param>
    /// <param name="onComplete">完成回调 (GameObject)</param>
    /// <param name="position">位置</param>
    /// <param name="rotation">旋转</param>
    /// <returns>协程</returns>
    public IEnumerator InstantiateAsync(
        string path,
        Transform parent = null,
        UnityAction<GameObject> onComplete = null,
        Vector3? position = null,
        Quaternion? rotation = null)
    {
        GameObject prefab = null;
        
        yield return MonoManager.GetInstance().StartCoroutine(LoadResourceAsync<GameObject>(path, (p) =>
        {
            prefab = p;
        }));

        if (prefab == null)
        {
            Debug.LogError($"[ResLoad] 异步实例化失败: {path}");
            onComplete?.Invoke(null);
            yield break;
        }

        Vector3 pos = position ?? Vector3.zero;
        Quaternion rot = rotation ?? Quaternion.identity;

        GameObject instance = UnityEngine.Object.Instantiate(prefab, pos, rot, parent);
        instance.name = prefab.name;

        Debug.Log($"[ResLoad] 异步实例化成功: {path} → {instance.name}");
        onComplete?.Invoke(instance);
    }

    #endregion

    #region 便捷方法 - 特定类型加载

    /// <summary>
    /// 加载Sprite（常用于UI图片）
    /// </summary>
    /// <param name="path">资源路径</param>
    /// <returns>Sprite对象</returns>
    public Sprite LoadSprite(string path)
    {
        return LoadResource<Sprite>(path);
    }

    /// <summary>
    /// 异步加载Sprite
    /// </summary>
    public IEnumerator LoadSpriteAsync(string path, UnityAction<Sprite> onComplete)
    {
        return LoadResourceAsync<Sprite>(path, onComplete);
    }

    /// <summary>
    /// 加载Texture2D（常用于原始贴图）
    /// </summary>
    public Texture2D LoadTexture(string path)
    {
        return LoadResource<Texture2D>(path);
    }

    /// <summary>
    /// 加载AudioClip（音频文件）
    /// </summary>
    public AudioClip LoadAudio(string path)
    {
        return LoadResource<AudioClip>(path);
    }

    /// <summary>
    /// 加载TextAsset（文本、JSON、XML等）
    /// </summary>
    public TextAsset LoadText(string path)
    {
        return LoadResource<TextAsset>(path);
    }

    /// <summary>
    /// 加载ScriptableObject（配置数据）
    /// </summary>
    public T LoadConfig<T>(string path) where T : ScriptableObject
    {
        return LoadResource<T>(path);
    }

    #endregion

    #region 卸载方法

    /// <summary>
    /// 卸载指定路径对应的AB包
    /// 注意：这会尝试卸载整个AB包，请确保没有其他地方在使用该包中的资源
    /// </summary>
    /// <param name="path">资源路径</param>
    public void Unload(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        string abName = ParseABName(path);
        ABManager.GetInstance().UnloadBundle(abName);

        // 同时清除缓存
        ClearPathFromCache(path);

        Debug.Log($"[ResLoad] 已请求卸载: {path} (AB包: {abName})");
    }

    /// <summary>
    /// 清除指定路径的缓存记录
    /// </summary>
    private void ClearPathFromCache(string path)
    {
        if (!enableCache) return;

        List<string> keysToRemove = new List<string>();
        foreach (var kvp in resourceCache)
        {
            if (kvp.Key.EndsWith(path))
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (string key in keysToRemove)
        {
            resourceCache.Remove(key);
        }
    }

    /// <summary>
    /// 卸载所有资源（慎用！通常在切换场景时调用）
    /// </summary>
    /// <param name="unloadAllObjects">是否同时销毁所有已实例化的对象</param>
    public void UnloadAll(bool unloadAllObjects = false)
    {
        // 清空缓存
        ClearCache();

        // 通知ABManager卸载所有AB包
        ABManager.GetInstance().UnloadAll(unloadAllObjects);

        Debug.Log("[ResLoad] 所有资源已卸载");
    }

    #endregion

    #region 内部方法 - 路径解析

    /// <summary>
    /// 从资源路径解析出AB包名称
    /// 规则：取第一级目录名（小写）
    /// 
    /// 重要：Unity打包的AB包默认没有扩展名！
    /// 示例：
    ///   "Prefabs/Enemy" → "prefabs"  （不是 "prefabs.ab"）
    ///   "UI/MainPanel" → "ui"
    ///   "Test/TestCube" → "test"
    /// </summary>
    /// <param name="resourcePath">资源路径</param>
    /// <returns>AB包名称（无扩展名）</returns>
    public string ParseABName(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath)) return string.Empty;

        // 处理Windows风格的路径分隔符
        resourcePath = resourcePath.Replace('\\', '/');

        // 取第一级目录
        int firstSlashIndex = resourcePath.IndexOf('/');
        if (firstSlashIndex <= 0)
        {
            // 没有目录分隔符，视为在根目录下
            Debug.LogWarning($"[ResLoad] 路径格式异常（缺少目录）: {resourcePath}，将使用默认AB包名 'root'");
            return "root";
        }

        string folderName = resourcePath.Substring(0, firstSlashIndex);
        
        // 只转小写，不添加 .ab 后缀（Unity默认打包没有扩展名）
        return folderName.ToLower();
    }

    /// <summary>
    /// 从资源路径解析出资源名称
    /// 规则：取最后一部分（去掉扩展名）
    /// 示例：
    ///   "Prefabs/Enemy" → "Enemy"
    ///   "UI/Panels/MainPanel" → "MainPanel"
    ///   "Audio/BGM/Battle" → "Battle"
    /// </summary>
    /// <param name="resourcePath">资源路径</param>
    /// <returns>资源名称</returns>
    public string ParseAssetName(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath)) return string.Empty;

        // 处理Windows风格的路径分隔符
        resourcePath = resourcePath.Replace('\\', '/');

        // 取最后一部分
        int lastSlashIndex = resourcePath.LastIndexOf('/');
        if (lastSlashIndex < 0)
        {
            return resourcePath;
        }

        string assetName = resourcePath.Substring(lastSlashIndex + 1);

        // 可选：去掉扩展名（Unity的LoadAsset有时需要带扩展名，有时不需要，这里保留原样）
        // 如果你的打包规则要求去掉扩展名，可以取消下面的注释：
        // int dotIndex = assetName.LastIndexOf('.');
        // if (dotIndex > 0)
        // {
        //     assetName = assetName.Substring(0, dotIndex);
        // }

        return assetName;
    }

    #endregion

    #region 调试方法

    /// <summary>
    /// 打印调试信息
    /// </summary>
    public void PrintDebugInfo()
    {
        string info = $"========== [ResLoad 调试信息] ==========\n";
        info += $"资源缓存状态: {(enableCache ? "已启用 ✅" : "已禁用 ❌")}\n";
        info += $"缓存资源数量: {resourceCache.Count}\n";
        info += $"\n{ABManager.GetInstance().GetMemoryInfo()}\n";
        info += $"=============================================";

        Debug.Log(info);
    }

    /// <summary>
    /// 验证资源路径是否能正确解析
    /// （开发工具方法，可在Editor中使用）
    /// </summary>
    /// <param name="testPath">测试路径</param>
    /// <returns>解析结果字符串</returns>
    public string TestParsePath(string testPath)
    {
        string abName = ParseABName(testPath);
        string assetName = ParseAssetName(testPath);

        return $"路径解析测试:\n" +
               $"  输入: {testPath}\n" +
               $"  AB包名: {abName}\n" +
               $"  资源名: {assetName}";
    }

    #endregion
}
