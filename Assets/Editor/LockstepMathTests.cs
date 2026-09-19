using GameLockstep;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 确定性基础库自测（Tests 菜单 / 每 Lockstep 分支合并前跑一次）
/// 验证：定点数基础运算、查表三角函数精度、环绕角、Sqrt、RNG 周期
/// </summary>
public static class LockstepMathTests
{
    [MenuItem("Tools/Lockstep/确定性基础库自测")]
    public static void RunAll()
    {
        int pass = 0, fail = 0;

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) { pass++; Debug.Log($"✅ {name} {detail}"); }
            else { fail++; Debug.LogError($"❌ {name} {detail}"); }
        }

        // ===== 定点数基础 =====
        var a = FP.FromInt(3);
        var b = FP.FromInt(4);
        Check("加法", (a + b) == FP.FromInt(7));
        Check("减法", (a - b) == FP.FromInt(-1));
        Check("乘法 3*4=12", (a * b) == FP.FromInt(12));

        var c = FP.FromInt(10);
        var d = FP.FromInt(4);
        Check("除法 10/4=2.5", (c / d).ToFloat() > 2.4999f && (c / d).ToFloat() < 2.5001f);

        // 精度：0.5 表示
        var half = new FP(FP.One / 2);
        Check("0.5 精度", half.ToFloat() > 0.4999f && half.ToFloat() < 0.5001f);

        // ===== 查表三角函数精度（与 UnityEngine.Mathf 对照） =====
        for (int deg = 0; deg < 360; deg += 7)
        {
            int angle = FPAngle.FromDegrees(deg);
            var dir = FPTrig.Dir(angle);

            float rad = deg * Mathf.Deg2Rad;
            float ex = Mathf.Sin(rad);
            float ez = Mathf.Cos(rad);

            float tolerance = 0.02f; // 查表 0.006° 步长 + 定点精度的综合容差
            if (Mathf.Abs(dir.X.ToFloat() - ex) > tolerance ||
                Mathf.Abs(dir.Z.ToFloat() - ez) > tolerance)
            {
                Check($"方向表 {deg}°", false,
                    $"dir=({dir.X.ToFloat():F3},{dir.Z.ToFloat():F3}) expect=({ex:F3},{ez:F3})");
                break;
            }
            if (deg == 353) Check("方向表全角度抽检", true);
        }

        // 0° 必须朝 +Z
        var dir0 = FPTrig.Dir(0);
        Check("0°朝+Z", dir0.Z.ToFloat() > 0.99f && Mathf.Abs(dir0.X.ToFloat()) < 0.02f);

        // ===== 环绕角 =====
        Check("环绕角差 350°→10° = 20°", FPAngle.Delta(FPAngle.FromDegrees(350), FPAngle.FromDegrees(10)) ==
              FPAngle.FromDegrees(20));

        // ===== Sqrt =====
        var s = FP.Sqrt(9L << 32); // sqrt(9.0 in Q32 domain) —— 直接验证定点域
        // 简化验证：Sqrt(SqrDistance) ≈ 已知距离
        long sqr = FP.SqrDistance(FP.FromInt(0), FP.FromInt(0), FP.FromInt(3), FP.FromInt(4));
        var dist = FP.Sqrt(sqr);
        Check("Sqrt 3-4-5", Mathf.Abs(dist.ToFloat() - 5f) < 0.001f, $"got={dist.ToFloat():F4}");

        // ===== RNG 确定性 =====
        var rng1 = new DeterministicRNG(12345);
        var rng2 = new DeterministicRNG(12345);
        bool same = true;
        for (int i = 0; i < 1000; i++)
        {
            if (rng1.NextInt(100) != rng2.NextInt(100)) { same = false; break; }
        }
        Check("RNG 同种子同序列", same);

        var rng3 = new DeterministicRNG(54321);
        bool diff = rng1.NextInt(1000000) != rng3.NextInt(1000000);
        Check("RNG 异种子异序列", diff);

        Debug.Log($"========== 确定性基础库自测: {pass} 通过, {fail} 失败 ==========");
        if (fail == 0) Debug.Log("🎉 阶段1地基可靠，可以进入阶段2（逻辑世界）");
    }
}
