using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

/// <summary>
/// AB包更新管理器 - 负责从远端服务器下载和更新AB包
/// 继承BaseManager，符合项目架构规范
/// </summary>
public class ABUpdateManager : BaseManager<ABUpdateManager>
{
    #region 私有字段

    private Dictionary<string, ABInfo> remoteABInfo;
    private Dictionary<string, ABInfo> localABInfo;
    private List<string> downloadList;
    private UnityAction<string> onProgressCallback;
    private bool isUpdating;

    #endregion

    #region 构造函数

    public ABUpdateManager()
    {
        remoteABInfo = new Dictionary<string, ABInfo>();
        localABInfo = new Dictionary<string, ABInfo>();
        downloadList = new List<string>();
        isUpdating = false;
        Debug.Log("[ABUpdateManager] 初始化完成");
    }

    #endregion

    #region 公开方法 - 主流程

    /// <summary>
    /// 检查并执行资源更新（协程版本）
    /// </summary>
    public IEnumerator CheckUpdateCoroutine(UnityAction<string> onProgress = null)
    {
        if (isUpdating)
        {
            Debug.LogWarning("[ABUpdateManager] 正在更新中，请勿重复调用");
            yield break;
        }

        isUpdating = true;
        onProgressCallback = onProgress;
        ClearData();

        // 第1步：下载对比文件
        ReportProgress("开始检查资源更新...");
        
        bool compareFileDownloaded = false;
        yield return DownloadABCompareFileCoroutine((result) =>
        {
            compareFileDownloaded = result;
        });

        if (!compareFileDownloaded)
        {
            ReportProgress("❌ 对比文件下载失败！");
            isUpdating = false;
            yield break;
        }

        ReportProgress("✅ 对比文件下载完成");

        // 第2步：解析远端信息
        ReportProgress("解析远端资源信息...");
        
        string tempFilePath = Application.persistentDataPath + "/ABCompareInfo_TMP.txt";
        if (File.Exists(tempFilePath))
        {
            string remoteInfo = File.ReadAllText(tempFilePath);
            ParseCompareFileInfo(remoteInfo, remoteABInfo);
        }
        else
        {
            ReportProgress("❌ 临时对比文件不存在！");
            isUpdating = false;
            yield break;
        }

        ReportProgress($"✅ 远端资源解析完成（共{remoteABInfo.Count}个包）");

        // 第3步：加载本地对比文件
        ReportProgress("加载本地资源信息...");
        yield return GetLocalABCompareFileInfoCoroutine();
        ReportProgress("✅ 本地资源解析完成");

        // 第4步：对比差异
        ReportProgress("对比资源差异...");
        CompareAndGenerateDownloadList();

        if (downloadList.Count == 0)
        {
            ReportProgress("✅ 资源已是最新，无需更新");
            isUpdating = false;
            yield break;
        }

        ReportProgress($"发现 {downloadList.Count} 个需要更新的包");

        // 第5步：下载AB包
        ReportProgress("开始下载AB包...");
        
        bool downloadSuccess = false;
        yield return DownloadABPackCoroutine((success) =>
        {
            downloadSuccess = success;
        });

        if (downloadSuccess)
        {
            string finalPath = Application.persistentDataPath + "/ABCompareInfo.txt";
            File.Copy(tempFilePath, finalPath, true);
            ReportProgress("✅ 所有资源更新完成！");
        }
        else
        {
            ReportProgress("❌ 部分资源下载失败！");
        }

        isUpdating = false;
    }

    /// <summary>
    /// 检查并执行资源更新（回调版本）
    /// </summary>
    public void CheckUpdate(UnityAction<bool> onComplete, UnityAction<string> onProgress = null)
    {
        MonoManager.GetInstance().StartCoroutine(CheckUpdateCoroutineWrapper(onComplete, onProgress));
    }

    private IEnumerator CheckUpdateCoroutineWrapper(UnityAction<bool> onComplete, UnityAction<string> onProgress)
    {
        yield return CheckUpdateCoroutine(onProgress);
        onComplete?.Invoke(downloadList.Count == 0 || !isUpdating);
    }

    #endregion

    #region 协程方法 - 下载对比文件

    public IEnumerator DownloadABCompareFileCoroutine(UnityAction<bool> onComplete = null)
    {
        string savePath = Application.persistentDataPath + "/ABCompareInfo_TMP.txt";
        int maxRetryCount = 5;
        bool isSuccess = false;

        for (int i = 0; i < maxRetryCount && !isSuccess; i++)
        {
            ReportProgress($"下载对比文件... ({i + 1}/{maxRetryCount})");

            bool downloading = true;
            bool currentResult = false;

            Thread thread = new Thread(() =>
            {
                try
                {
                    // 对比文件名固定为 ABCompareInfo.txt（用于版本比对）
                    // 注意：这不是主清单文件(PC.manifest)，而是资源对比文件
                    currentResult = DownloadFile("ABCompareInfo.txt", savePath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ABUpdateManager] 下载线程异常: {e.Message}");
                    currentResult = false;
                }
                finally
                {
                    downloading = false;
                }
            });

            thread.Start();

            while (downloading)
            {
                yield return null;
            }

            isSuccess = currentResult;
        }

        ReportProgress(isSuccess ? "✅ 对比文件下载成功" : "❌ 对比文件下载失败");
        onComplete?.Invoke(isSuccess);
    }

    #endregion

    #region 协程方法 - 加载本地对比文件

    private IEnumerator GetLocalABCompareFileInfoCoroutine()
    {
        string filePath = null;

        if (File.Exists(Application.persistentDataPath + "/ABCompareInfo.txt"))
        {
            filePath = Application.persistentDataPath + "/ABCompareInfo.txt";
        }
        else if (File.Exists(Application.streamingAssetsPath + "/ABCompareInfo.txt"))
        {
            filePath = Application.streamingAssetsPath + "/ABCompareInfo.txt";
        }
        else
        {
            ReportProgress("首次运行，将下载全部资源");
            yield break;
        }

        UnityWebRequest req = UnityWebRequest.Get(filePath);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            ParseCompareFileInfo(req.downloadHandler.text, localABInfo);
        }
        else
        {
            Debug.LogWarning($"[ABUpdateManager] 本地对比文件读取失败: {req.error}");
        }
    }

    #endregion

    #region 协程方法 - 批量下载AB包

    public IEnumerator DownloadABPackCoroutine(UnityAction<bool> onComplete = null)
    {
        if (downloadList == null || downloadList.Count == 0)
        {
            onComplete?.Invoke(true);
            yield break;
        }

        string basePath = Application.persistentDataPath + "/";
        int maxRetryRounds = 5;
        int totalToDownload = downloadList.Count;
        int downloadedCount = 0;

        Debug.Log($"[ABUpdateManager] 开始下载 {totalToDownload} 个AB包");

        while (downloadList.Count > 0 && maxRetryRounds > 0)
        {
            List<string> thisRoundSuccess = new List<string>();

            for (int i = 0; i < downloadList.Count; i++)
            {
                string fileName = downloadList[i];
                string savePath = basePath + fileName;

                ReportProgress($"下载 [{downloadedCount + 1}/{totalToDownload}] {fileName}");

                bool downloading = true;
                bool fileSuccess = false;

                Thread thread = new Thread(() =>
                {
                    try
                    {
                        fileSuccess = DownloadFile(fileName, savePath);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ABUpdateManager] 下载线程异常 [{fileName}]: {e.Message}");
                        fileSuccess = false;
                    }
                    finally
                    {
                        downloading = false;
                    }
                });

                thread.Start();

                while (downloading)
                {
                    yield return null;
                }

                if (fileSuccess)
                {
                    downloadedCount++;
                    thisRoundSuccess.Add(fileName);
                    Debug.Log($"[ABUpdateManager] ✅ 下载成功 ({downloadedCount}/{totalToDownload}): {fileName}");
                }
                else
                {
                    Debug.LogWarning($"[ABUpdateManager] ❌ 下载失败: {fileName}");
                }
            }

            foreach (string successName in thisRoundSuccess)
            {
                downloadList.Remove(successName);
            }

            maxRetryRounds--;
        }

        bool allSuccess = (downloadList.Count == 0);

        if (allSuccess)
        {
            ReportProgress($"✅ 全部 {totalToDownload} 个AB包下载完成");
        }
        else
        {
            ReportProgress($"⚠️ 下载完成，但 {downloadList.Count} 个包失败");
            Debug.LogError($"[ABUpdateManager] 失败的包: {string.Join(", ", downloadList.ToArray())}");
        }

        onComplete?.Invoke(allSuccess);
    }

    #endregion

    #region 核心方法 - FTP下载

    public bool DownloadFile(string fileName, string localPath)
    {
        try
        {
            string ftpUrl = ABConfig.GetFTPDownloadPath(fileName);
            FtpWebRequest req = WebRequest.Create(new Uri(ftpUrl)) as FtpWebRequest;

            req.Credentials = new NetworkCredential(ABConfig.FTP_USERNAME, ABConfig.FTP_PASSWORD);
            req.Proxy = null;
            req.KeepAlive = false;
            req.Method = WebRequestMethods.Ftp.DownloadFile;
            req.UseBinary = true;
            req.Timeout = 30000;

            using (FtpWebResponse res = req.GetResponse() as FtpWebResponse)
            using (Stream downloadStream = res.GetResponseStream())
            using (FileStream file = File.Create(localPath))
            {
                byte[] buffer = new byte[2048];
                int bytesRead = downloadStream.Read(buffer, 0, buffer.Length);

                while (bytesRead > 0)
                {
                    file.Write(buffer, 0, bytesRead);
                    bytesRead = downloadStream.Read(buffer, 0, buffer.Length);
                }
            }

            return true;
        }
        catch (WebException webEx)
        {
            Debug.LogError($"[ABUpdateManager] FTP错误 [{fileName}]: {webEx.Message}");
            return false;
        }
        catch (IOException ioEx)
        {
            Debug.LogError($"[ABUpdateManager] IO错误 [{fileName}]: {ioEx.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ABUpdateManager] 异常 [{fileName}]: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region 内部方法 - 数据处理

    private void ParseCompareFileInfo(string info, Dictionary<string, ABInfo> targetDict)
    {
        targetDict.Clear();
        if (string.IsNullOrEmpty(info)) return;

        string[] lines = info.Split('\n');
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(' ');
            if (parts.Length >= 3)
            {
                ABInfo abInfo = new ABInfo(parts[0], parts[1], parts[2]);
                targetDict[abInfo.name] = abInfo;
            }
        }
    }

    private void CompareAndGenerateDownloadList()
    {
        downloadList.Clear();
        List<string> deleteList = new List<string>();

        foreach (var pair in remoteABInfo)
        {
            string abName = pair.Key;
            if (!localABInfo.ContainsKey(abName))
            {
                downloadList.Add(abName);
            }
            else if (pair.Value.md5 != localABInfo[abName].md5)
            {
                downloadList.Add(abName);
            }
        }

        foreach (var pair in localABInfo)
        {
            if (!remoteABInfo.ContainsKey(pair.Key))
            {
                deleteList.Add(pair.Key);
            }
        }

        foreach (string deleteName in deleteList)
        {
            string abPath = Application.persistentDataPath + "/" + deleteName;
            if (File.Exists(abPath))
            {
                File.Delete(abPath);
                ReportProgress($"删除过期资源: {deleteName}");
            }
            localABInfo.Remove(deleteName);
        }
    }

    private void ClearData()
    {
        remoteABInfo?.Clear();
        localABInfo?.Clear();
        downloadList?.Clear();
    }

    private void ReportProgress(string message)
    {
        Debug.Log($"[ABUpdateManager] {message}");
        onProgressCallback?.Invoke(message);
    }

    #endregion

    #region 公开方法 - 查询

    public bool IsUpdating => isUpdating;
    public int PendingDownloadCount => downloadList?.Count ?? 0;
    public int RemoteABCount => remoteABInfo?.Count ?? 0;

    #endregion
}

/// <summary>
/// AB包信息数据类
/// </summary>
public class ABInfo
{
    public string name;
    public long size;
    public string md5;

    public ABInfo(string name, string size, string md5)
    {
        this.name = name;
        long parsedSize;
        this.size = long.TryParse(size, out parsedSize) ? parsedSize : 0;
        this.md5 = md5 ?? string.Empty;
    }

    public override string ToString()
    {
        return $"ABInfo[{name}] Size:{size} MD5:{(md5.Length > 8 ? md5.Substring(0, 8) : md5)}...";
    }
}
