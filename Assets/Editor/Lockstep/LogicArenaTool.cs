using System;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 场景墙体 → LogicArena.Walls 代码生成（Tools/Lockstep/生成墙体 OBB 代码）
///
/// 为什么是"生成代码"而不是"运行时读场景"：几何要参与命中判定，必须所有端完全一致，
/// 逻辑层不能引用 UnityEngine，所以工具只负责算数，结果由人手粘贴进 LogicArena。
///
/// 扫描规则：
/// - 只认 BoxCollider：逻辑盒子是 OBB，和 BoxCollider 一一对应
/// - 只收录跨过 ShotHeight 的盒子：逻辑层把弹道压成 y=1 的 2D 线，
///   地面、矮台阶（打上方飞过去）自然被排除
/// - 只支持"绕 Y 转 + 缩放"：盒子在竖直方向必须是正的（x/z 欧拉角为 0），
///   倾斜的盒子在 2D 里没法表示，会给出警告
/// </summary>
public static class LogicArenaTool
{
    /// <summary>与 LockstepGameDriver.OnFiredEvent 里的弹道高度一致</summary>
    private const float ShotHeight = 1f;

    [MenuItem("Tools/Lockstep/生成墙体 OBB 代码")]
    public static void Generate()
    {
        var colliders = UnityEngine.Object.FindObjectsOfType<BoxCollider>(true);
        Array.Sort(colliders, (a, b) => string.CompareOrdinal(a.name, b.name));

        var sb = new StringBuilder();
        sb.AppendLine("        public static readonly Box2[] Walls =");
        sb.AppendLine("        {");

        int count = 0;
        foreach (var col in colliders)
        {
            Bounds b = col.bounds; // 世界空间 AABB，用来做高度过滤
            if (b.max.y < ShotHeight || b.min.y > ShotHeight) continue;

            Transform t = col.transform;
            Vector3 euler = t.eulerAngles;
            if (Mathf.Abs(Mathf.DeltaAngle(euler.x, 0f)) > 0.01f || Mathf.Abs(Mathf.DeltaAngle(euler.z, 0f)) > 0.01f)
                Debug.LogWarning($"[LogicArena] {col.name} 在竖直方向上有倾斜 (x={euler.x:F1}°, z={euler.z:F1}°)，" +
                                 "逻辑层只支持绕 Y 旋转的盒子，这个盒子按 yaw 近似收录", col);

            // 世界中心：BoxCollider.center 是本地坐标，要跟着 Transform 走
            Vector3 center = t.TransformPoint(col.center);
            // 世界尺寸：本地尺寸 × 世界缩放（取绝对值，负缩放就是镜像）
            Vector3 scale = t.lossyScale;
            float sizeX = Mathf.Abs(scale.x) * col.size.x;
            float sizeZ = Mathf.Abs(scale.z) * col.size.z;
            float yaw = euler.y;

            sb.AppendLine($"            // {col.name}: y {b.min.y:F2}~{b.max.y:F2}，yaw {yaw:F2}°");
            sb.AppendLine($"            Box2.FromCenterSize({center.x:F4}f, {center.z:F4}f, {sizeX:F4}f, {sizeZ:F4}f, {yaw:F2}f),");
            count++;
        }

        sb.AppendLine("        };");

        if (count == 0)
        {
            Debug.LogWarning($"[LogicArena] 当前场景没有跨过 y={ShotHeight} 的 BoxCollider，什么都没生成");
            return;
        }

        EditorGUIUtility.systemCopyBuffer = sb.ToString();
        Debug.Log($"[LogicArena] 生成 {count} 个墙体，已复制到剪贴板，" +
                  "替换 LogicArena.Walls 即可：\n" + sb);
    }
}
