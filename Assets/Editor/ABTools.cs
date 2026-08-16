using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class ABTools : EditorWindow
{
    private int nowSelectIndex = 0;
    private string[] targetStrings = new string[] { "PC", "IOS", "ANDROID" };

    private string serverIP = "ftp://127.0.0.1";

    [MenuItem("AB包工具/打开工具窗口")]
    private static void OpenWindow()
    {
        //获取一个窗口对象
        ABTools window = EditorWindow.GetWindowWithRect(typeof(ABTools), new Rect(0, 0, 535, 200)) as ABTools;
        window.Show();
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 150, 20), "选择平台");
        //页签显示
  
        nowSelectIndex = GUI.Toolbar(new Rect(10, 30, 280, 20), nowSelectIndex, targetStrings);
        //资源服务器IP地址
        GUI.Label(new Rect(10, 60, 150, 20), "资源服务器IP地址");
        serverIP = GUI.TextField(new Rect(10, 80, 280, 20), serverIP);



        //创建对比文件按钮
        if(GUI.Button(new Rect(10,110,100,40),"创建对比文件"))
        {
            CreateABCompareFile();
        }
        //保存选中资源到StreamingAssets按钮
        if (GUI.Button(new Rect(115, 110, 250, 40), "保存选中资源到StreamingAssets"))
        {
            MoveABtoStreamingAssets();
        }
        //上传资源到服务器按钮
        if (GUI.Button(new Rect(370, 110, 150, 40), "上传资源到服务器"))
        {
            _ = UploadABToServer();
        }
    }

    public  void CreateABCompareFile()
    {
        //Debug.Log("创建AB包对比文件");

        //获取AB包所在文件夹的信息
        DirectoryInfo directoryInfo = new DirectoryInfo(Application.dataPath + "/ArtRes/AB/" + targetStrings[nowSelectIndex]);
        //获取所有文件信息
        FileInfo[] fileInfos = directoryInfo.GetFiles();

        //用于存储AB包对比信息的字符串
        string abCompareInfo = "";


        foreach (FileInfo fileInfo in fileInfos)
        {
            // Debug.Log(fileInfo.FullName);
            if (fileInfo.Extension == "")
            {
                //拼接一个ab包的信息
                abCompareInfo += fileInfo.Name + " " + fileInfo.Length + " " + GetMD5(fileInfo.FullName);
                //用一个分割符号分割每个ab包的信息
                abCompareInfo += "\n";
            }

        }
        //去掉最后一个分割符号
        abCompareInfo = abCompareInfo.Substring(0, abCompareInfo.Length - 1);

        Debug.Log(abCompareInfo);

        //存储AB包对比信息到文件中
        File.WriteAllText(Application.dataPath + "/ArtRes/AB/" + targetStrings[nowSelectIndex] +"/ABCompareInfo.txt", abCompareInfo);
        AssetDatabase.Refresh();
    }

    public  string GetMD5(string filePath)
    {
        //将文件以流的方式打开
        using (FileStream file = new FileStream(filePath, FileMode.Open))
        {
            //声明一个MD5对象 用于生成MD5码
            MD5 md5 = new MD5CryptoServiceProvider();
            //利用api得到文件的MD5码
            byte[] mdInfo = md5.ComputeHash(file);

            StringBuilder sb = new StringBuilder();

            //关闭文件流
            file.Close();

            //将得到的MD5码转为16进制字符串
            for (int i = 0; i < mdInfo.Length; i++)
            {
                sb.Append(mdInfo[i].ToString("x2"));
            }

            return sb.ToString();
        }
    }


    private  void MoveABtoStreamingAssets()
    {
        //通过编辑器获取选中的资源
       UnityEngine.Object[] selectedAssets = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.DeepAssets);

        if (selectedAssets.Length == 0) return;

        //用于拼接本地默认ab包资源信息
        string abCompareInfo = "";

        foreach (var asset in selectedAssets)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string fileName = assetPath.Substring(assetPath.LastIndexOf('/'));
            // Debug.Log(fileName);

            AssetDatabase.CopyAsset(assetPath, "Assets/StreamingAssets" + fileName);

            FileInfo fileInfo = new FileInfo(Application.dataPath + "/StreamingAssets" + fileName);

            if (fileInfo.Extension == "")
            {
                abCompareInfo += fileInfo.Name + " " + fileInfo.Length + " " + CreateABCompare.GetMD5(fileInfo.FullName);
                abCompareInfo += "\n";
            }



        }
        abCompareInfo = abCompareInfo.Substring(0, abCompareInfo.Length - 1);

        File.WriteAllText(Application.dataPath + "/StreamingAssets/ABCompareInfo.txt", abCompareInfo);


    }

    private  async Task UploadABToServer()
    {
        //Debug.Log("上传AB包到服务器");


        //获取AB包所在文件夹的信息
        DirectoryInfo directoryInfo = new DirectoryInfo(Application.dataPath + "/ArtRes/AB/" + targetStrings[nowSelectIndex] +"/");
        //获取所有文件信息
        FileInfo[] fileInfos = directoryInfo.GetFiles();

        foreach (FileInfo fileInfo in fileInfos)
        {
            // Debug.Log(fileInfo.FullName);
            if (fileInfo.Extension == "" || fileInfo.Extension == ".txt")//如果文件为ab包或对比文件
            {
                //上传文件到服务器
                await FtpUpload(fileInfo.FullName, fileInfo.Name);
            }

        }
    }

    private async  Task FtpUpload(string filePath, string fileName)
    {
        try
        {
            // 将耗时的网络 IO 和文件读取放到后台线程 (ThreadPool) 中执行，避免阻塞 Unity 主线程
            await Task.Run(() =>
            {
                // 1. 创建 FTP 请求
                FtpWebRequest req = WebRequest.Create(new Uri(serverIP + "/AB/" + targetStrings[nowSelectIndex] +"/" + fileName)) as FtpWebRequest;

                // 2. 设置通信凭证与属性
                NetworkCredential n = new NetworkCredential("ABTest", "8208820");
                req.Credentials = n;
                req.Proxy = null;
                req.KeepAlive = false;
                req.Method = WebRequestMethods.Ftp.UploadFile;
                req.UseBinary = true;

                // 3. 上传文件（使用双 using 自动释放流资源）
                using (FileStream file = File.OpenRead(filePath))
                using (Stream uploadStream = req.GetRequestStream())
                {
                    byte[] bytes = new byte[2048];
                    int contentLength = file.Read(bytes, 0, bytes.Length);

                    while (contentLength != 0)
                    {
                        // 写入到上传流中
                        uploadStream.Write(bytes, 0, contentLength);
                        // 写完再读
                        contentLength = file.Read(bytes, 0, bytes.Length);
                    }
                }
            });

            // 回到 Unity 主线程打印日志
            Debug.Log($"[上传成功] 文件: {fileName}");
        }
        catch (WebException webEx)
        {
            // 捕获 FTP 网络相关异常（如 550 路径不存在、账号密码错误、连接超时等）
            Debug.LogError($"[FTP网络异常] 文件: {fileName}，原因: {webEx.Message}");
        }
        catch (Exception ex)
        {
            // 捕获其他异常（如文件被占用、IO错误等）
            Debug.LogError($"[上传失败] 文件: {fileName}，原因: {ex.Message}");
        }
    }
}
