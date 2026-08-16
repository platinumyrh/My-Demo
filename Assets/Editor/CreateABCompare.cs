using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public  class CreateABCompare
{
   // [MenuItem("AB包工具/创建对比文件")]
    public static void CreateABCompareFile() 
    {
        //Debug.Log("创建AB包对比文件");

        //获取AB包所在文件夹的信息
        DirectoryInfo directoryInfo = new DirectoryInfo(Application.dataPath+"/ArtRes/AB/PC/");
        //获取所有文件信息
        FileInfo[] fileInfos = directoryInfo.GetFiles();

        //用于存储AB包对比信息的字符串
        string abCompareInfo = "";


        foreach (FileInfo fileInfo in fileInfos)
        {
           // Debug.Log(fileInfo.FullName);
           if(fileInfo.Extension == "")
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
        File.WriteAllText(Application.dataPath + "/ArtRes/AB/PC/ABCompareInfo.txt", abCompareInfo);
        AssetDatabase.Refresh();
    }

    public static string GetMD5(string filePath)
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
}
