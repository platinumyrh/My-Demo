using UnityEngine;

/// <summary>
/// AB包系统配置类
/// 集中管理所有AB包相关的配置常量
/// </summary>
public static class ABConfig
{
    #region 主清单配置

    /// <summary>
    /// AssetBundle主清单文件名
    /// 重要：这个名称 = 你在Unity打包时设置的输出文件夹名称！
    /// 
    /// 示例：
    ///   如果你的AB包输出到 "ArtRes/AB/PC/" 文件夹
    ///   那么主清单文件名就是 "PC"（不带扩展名）
    ///   
    /// Unity会自动生成一个与输出文件夹同名的文件作为主清单
    /// 该文件包含所有AB包的依赖关系信息（Manifest）
    /// </summary>
    /// <remarks>
    /// 从截图中看到用户打包输出的文件夹名为 "PC"
    /// 所以这里应该设置为 "PC"
    /// 如果将来改了输出路径，记得同步修改这个常量
    /// </remarks>
    public const string ManifestFileName = "PC";

    /// <summary>
    /// 主清单中的资源名称（固定值，Unity规定，不要修改）
    /// 从AssetBundle中加载Manifest时使用此名称
    /// </summary>
    public const string ManifestAssetName = "AssetBundleManifest";

    #endregion

    #region 路径规则配置

    /// <summary>
    /// AB包命名规则：文件夹名小写 + ".ab" 后缀
    /// 示例：
    ///   Prefabs/  → prefabs.ab
    ///   UI/      → ui.ab
    ///   Audio/   → audio.ab
    /// </summary>
    public const string AB_NAME_FORMAT = "{foldername}.ab";

    /// <summary>
    /// AB包存放的根目录名称（在服务器和本地）
    /// </summary>
    public const string AB_ROOT_FOLDER = "AB";

    /// <summary>
    /// 各平台的AB包子目录
    /// </summary>
    public const string PC_FOLDER = "PC";
    public const string IOS_FOLDER = "IOS";
    public const string ANDROID_FOLDER = "ANDROID";

    #endregion

    #region 性能优化配置

    /// <summary>
    /// 是否默认启用资源缓存
    /// 开启后重复加载同一资源会从内存缓存返回（不重新LoadAsset）
    /// 建议频繁使用的小资源开启，大资源关闭以节省内存
    /// </summary>
    public const bool DEFAULT_CACHE_ENABLED = false;

    /// <summary>
    /// 异步加载的超时时间（秒）
    /// 超过此时间未完成会打印警告
    /// </summary>
    public const float ASYNC_LOAD_TIMEOUT = 30f;

    #endregion

    #region FTP服务器配置（与ABUpdateManager保持一致）

    /// <summary>
    /// FTP服务器地址（开发环境）
    /// </summary>
    public const string FTP_SERVER_IP = "ftp://127.0.0.1";

    /// <summary>
    /// FTP登录用户名
    /// </summary>
    public const string FTP_USERNAME = "ABTest";

    /// <summary>
    /// FTP登录密码
    /// </summary>
    public const string FTP_PASSWORD = "8208820";

    /// <summary>
    /// FTP服务器上的AB包根路径
    /// 格式: ftp://ip/AB/{Platform}/
    /// </summary>
    public static string GetFTPBasePath(string platform)
    {
        return $"{FTP_SERVER_IP}/{AB_ROOT_FOLDER}/{platform}/";
    }

    #endregion

    #region 文件路径工具方法

    /// <summary>
    /// 获取当前平台的文件夹名称
    /// </summary>
    public static string GetCurrentPlatformFolder()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsEditor:
                return PC_FOLDER;
            case RuntimePlatform.IPhonePlayer:
                return IOS_FOLDER;
            case RuntimePlatform.Android:
                return ANDROID_FOLDER;
            default:
                return PC_FOLDER; // 默认返回PC
        }
    }

    /// <summary>
    /// 获取完整的FTP下载路径
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <returns>完整URL</returns>
    public static string GetFTPDownloadPath(string fileName)
    {
        return $"{GetFTPBasePath(GetCurrentPlatformFolder())}{fileName}";
    }

    /// <summary>
    /// 获取本地的持久化数据路径 + AB包相对路径
    /// </summary>
    /// <param name="abName">AB包名</param>
    /// <returns>完整本地路径</returns>
    public static string GetLocalPersistentPath(string abName)
    {
        return Application.persistentDataPath + "/" + abName;
    }

    /// <summary>
    /// 获取StreamingAssets中的AB包路径
    /// </summary>
    /// <param name="abName">AB包名</param>
    /// <returns>完整本地路径</returns>
    public static string GetStreamingAssetsPath(string abName)
    {
        return Application.streamingAssetsPath + "/" + abName;
    }

    #endregion
}
