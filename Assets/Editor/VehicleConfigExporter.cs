using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 载具配置导出（Tools/导出载具配置到服务端）
/// 把项目里所有 PlayerProfileSO 资产序列化成 JSON，写到服务端的 Data/vehicles.json，
/// 供服务端按载具初始化血量等属性（Unity 的 SO 服务端读不了，以此文件为桥）
/// </summary>
public static class VehicleConfigExporter
{
    /// <summary>服务端 Data 目录（客户端项目的同级目录下）</summary>
    private static string ServerDataDir =>
        Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Server_MyDemo", "Data");

    [MenuItem("Tools/导出载具配置到服务端")]
    public static void ExportAll()
    {
        var guids = AssetDatabase.FindAssets("t:PlayerProfileSO");
        var entries = new List<VehicleEntry>();

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var profile = AssetDatabase.LoadAssetAtPath<PlayerProfileSO>(path);
            if (profile == null) continue;

            entries.Add(new VehicleEntry
            {
                profile = profile.name,
                maxHp = profile.maxHp,
                walkSpeed = profile.walkSpeed,
                sprintSpeed = profile.sprintSpeed,
                weaponDamage = profile.weaponDamage,
                weaponFireRate = profile.weaponFireRate,
                weaponMagazineSize = profile.weaponMagazineSize,
                weaponReloadTime = profile.weaponReloadTime,
                weaponMaxRange = profile.weaponMaxRange
            });
        }

        if (entries.Count == 0)
        {
            Debug.LogWarning("[VehicleConfigExporter] ⚠️ 项目里没有找到 PlayerProfileSO 资产，未导出");
            return;
        }

        var dir = ServerDataDir;
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "vehicles.json");

        var list = new VehicleList { vehicles = entries };
        File.WriteAllText(file, JsonUtility.ToJson(list, true));

        Debug.Log($"[VehicleConfigExporter] ✅ 已导出 {entries.Count} 个载具配置 → {file}\n" +
                  string.Join("\n", entries.ConvertAll(e => $"  - {e.profile}: hp={e.maxHp}, dmg={e.weaponDamage}")));
    }

    [System.Serializable]
    private class VehicleEntry
    {
        public string profile;
        public int maxHp;
        public float walkSpeed;
        public float sprintSpeed;
        public int weaponDamage;
        public float weaponFireRate;
        public int weaponMagazineSize;
        public float weaponReloadTime;
        public float weaponMaxRange;
    }

    [System.Serializable]
    private class VehicleList
    {
        public List<VehicleEntry> vehicles = new List<VehicleEntry>();
    }
}
