using System.Collections.Generic;
using GameLockstep;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 逻辑世界自测（Tools/Lockstep/逻辑世界自测）
/// 验证：双世界同指令逐帧哈希一致（确定性）、指令敏感性、直线/冲刺速度、斜向归一、
/// 转向限速不超调、跳跃落地、圆形互推对称、完全重合兜底、死亡锁定、边界钳制、
/// 静态墙体阻挡/滑动/推出
/// </summary>
public static class LockstepWorldTests
{
    [MenuItem("Tools/Lockstep/逻辑世界自测")]
    public static void RunAll()
    {
        int pass = 0, fail = 0;
        var radius = FP.FromRaw(LockstepConfig.PLAYER_RADIUS);
        float radiusFloat = radius.ToFloat();
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) { pass++; Debug.Log($"✅ {name} {detail}"); }
            else { fail++; Debug.LogError($"❌ {name} {detail}"); }
        }

        // ===== 确定性：两个独立世界喂同一条指令流，300 帧逐帧比对哈希 =====
        const int frames = 300;
        var w1 = CreateSampleWorld();
        var w2 = CreateSampleWorld();
        var cmdRng = new DeterministicRNG(777);
        bool allSame = true;
        long firstDiff = -1;
        for (int f = 0; f < frames; f++)
        {
            var cmds = NextCommands(cmdRng, w1);
            w1.Tick(cmds);
            w2.Tick(cmds);
            if (w1.ComputeStateHash() != w2.ComputeStateHash())
            {
                allSame = false;
                firstDiff = f;
                break;
            }
        }
        Check("双世界300帧哈希一致", allSame, firstDiff >= 0 ? $"首次分歧 frame={firstDiff}" : "");

        // ===== 指令敏感性：同种子指令流跑到第 50 帧，改一条指令 → 哈希分歧 =====
        var w3 = CreateSampleWorld();
        var w4 = CreateSampleWorld();
        var cmdRng3 = new DeterministicRNG(777);
        var cmdRng4 = new DeterministicRNG(777);
        for (int f = 0; f <= 50; f++)
        {
            var c3 = NextCommands(cmdRng3, w3);
            var c4 = NextCommands(cmdRng4, w4);
            if (f == 50)
                c3[1000] = new PlayerCommand { MoveX = 126, MoveZ = 0, AimAngle = 0, Buttons = 0 };
            w3.Tick(c3);
            w4.Tick(c4);
        }
        Check("指令敏感性：单条改动即分歧", w3.ComputeStateHash() != w4.ComputeStateHash());

        // ===== 直线速度：MoveZ=127 走路 60 帧 → 5m/s × 4s = 20m（从 z=-30 出发，避开 z≈7 的墙）=====
        const float WallFreeZ = -30f;
        var wLine = new LogicWorld(1);
        var pLine = wLine.AddPlayer(1, new FP(0), FP.FromInt((long)WallFreeZ), 0, 100);
        var fwd = new Dictionary<int, PlayerCommand> { { 1, new PlayerCommand { MoveZ = 127 } } };
        for (int i = 0; i < 60; i++) wLine.Tick(fwd);
        Check("走路直线 60帧≈20m", Mathf.Abs(pLine.Z.ToFloat() - (WallFreeZ + 20f)) < 0.01f && Mathf.Abs(pLine.X.ToFloat()) < 0.001f,
              $"z={pLine.Z.ToFloat():F4} x={pLine.X.ToFloat():F4}");

        // ===== 斜向归一：(90,90) 与 (0,127) 等速 =====
        var wDiag = new LogicWorld(1);
        var pDiag = wDiag.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var diag = new Dictionary<int, PlayerCommand> { { 1, new PlayerCommand { MoveX = 90, MoveZ = 90 } } };
        for (int i = 0; i < 15; i++) wDiag.Tick(diag);
        // 纯 Z 的 15 帧参照
        var wRef = new LogicWorld(1);
        var pRef = wRef.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        for (int i = 0; i < 15; i++) wRef.Tick(fwd);
        float dDiag = Mathf.Sqrt(pDiag.X.ToFloat() * pDiag.X.ToFloat() + pDiag.Z.ToFloat() * pDiag.Z.ToFloat());
        Check("斜向归一 (90,90)==(0,127)速度", Mathf.Abs(dDiag - pRef.Z.ToFloat()) < 0.002f,
              $"diag={dDiag:F4} straight={pRef.Z.ToFloat():F4}");

        // ===== 冲刺：15 帧 × 8m/s = 8m（同样从无墙区出发）=====
        var wSpr = new LogicWorld(1);
        var pSpr = wSpr.AddPlayer(1, new FP(0), FP.FromInt((long)WallFreeZ), 0, 100);
        var spr = new Dictionary<int, PlayerCommand> { { 1, new PlayerCommand { MoveZ = 127, Buttons = PlayerCommand.BTN_SPRINT } } };
        for (int i = 0; i < 15; i++) wSpr.Tick(spr);
        Check("冲刺 15帧≈8m", Mathf.Abs(pSpr.Z.ToFloat() - (WallFreeZ + 8f)) < 0.01f, $"z={pSpr.Z.ToFloat():F4}");

        // ===== 转向限速：从 0° 转 90°，每帧 TURN_SPEED，贴合不超调 =====
        var wTurn = new LogicWorld(1);
        var pTurn = wTurn.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var turn = new Dictionary<int, PlayerCommand> { { 1, new PlayerCommand { AimAngle = FPAngle.FromDegrees(90) } } };
        wTurn.Tick(turn);
        Check("转向限速 1帧=TURN_SPEED", pTurn.Yaw == LockstepConfig.TURN_SPEED_PER_TICK, $"yaw={pTurn.Yaw}");
        for (int i = 1; i < 10; i++) wTurn.Tick(turn);
        Check("转向贴合 90°不超调", pTurn.Yaw == FPAngle.FromDegrees(90), $"yaw={pTurn.Yaw} expect={FPAngle.FromDegrees(90)}");

        // ===== 圆形互推：0.4m 间距 → 分开到 2R，中点不动 =====
        var wCol = new LogicWorld(1);
        var pColA = wCol.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var pColB = wCol.AddPlayer(2, FP.FromInt(2) / FP.FromInt(5), new FP(0), 0, 100);
        wCol.Tick(new Dictionary<int, PlayerCommand>());
        float colDist = FP.Sqrt(FP.SqrDistance(pColA.X, pColA.Z, pColB.X, pColB.Z)).ToFloat();
        float midX = (pColA.X.ToFloat() + pColB.X.ToFloat()) / 2f;
        Check("互推分开到 2R", Mathf.Abs(colDist - 2f * radiusFloat) < 0.001f, $"dist={colDist:F4} expect={2f * radiusFloat:F4}");
        Check("互推中点守恒", Mathf.Abs(midX - 0.2f) < 0.001f, $"mid={midX:F4}");

        // ===== 完全重合兜底：同点出生 → 沿 ±X 各推一个半径 =====
        var wZero = new LogicWorld(1);
        var pZeroA = wZero.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var pZeroB = wZero.AddPlayer(2, new FP(0), new FP(0), 0, 100);
        wZero.Tick(new Dictionary<int, PlayerCommand>());
        Check("重合兜底 ±R", pZeroA.X.ToFloat() > radiusFloat - 0.01f && pZeroB.X.ToFloat() < -(radiusFloat - 0.01f),
              $"a.x={pZeroA.X.ToFloat():F4} b.x={pZeroB.X.ToFloat():F4}");

        // ===== 死亡锁定 =====
        var wDead = new LogicWorld(1);
        var pDead = wDead.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        pDead.IsDead = true;
        var anyCmd = new Dictionary<int, PlayerCommand> { { 1, new PlayerCommand { MoveX = 127, AimAngle = FPAngle.FromDegrees(179), Buttons = PlayerCommand.BTN_JUMP } } };
        wDead.Tick(anyCmd);
        Check("死亡不响应指令", pDead.X.Raw == 0 && pDead.Z.Raw == 0 && pDead.Yaw == 0 && pDead.Y.Raw == 0);

        // ===== 边界钳制：贴边冲刺 100 帧 → 钳在 ±500m =====
        var wBound = new LogicWorld(1);
        var pBound = wBound.AddPlayer(1, FP.FromInt(499), new FP(0), 0, 100);
        var outCmd = new Dictionary<int, PlayerCommand> { { 1, new PlayerCommand { MoveX = 127, Buttons = PlayerCommand.BTN_SPRINT } } };
        for (int i = 0; i < 100; i++) wBound.Tick(outCmd);
        Check("边界钳制 500m", pBound.X.Raw == LockstepConfig.WORLD_LIMIT, $"x={pBound.X.ToFloat():F2}");

        // ===== 静态墙体（OBB）：斜盒射线/推出、场景墙走路不穿透、卡进墙里被推出 =====
        if (LogicArena.Walls.Length == 0)
        {
            Debug.LogWarning("LogicArena.Walls 为空，跳过墙体自测");
        }
        else
        {
            // 45° 斜盒（10×1，中心 (0,10)）：原点朝 +Z 射线应命中 t=9.2929，法线随朝向转成 (-0.707,-0.707)
            var box = LogicArena.Box2.FromCenterSize(0f, 10f, 10f, 1f, 45f);
            var tHit = LogicArena.RaycastBox(box, new FP(0), new FP(0), FPTrig.Dir(0), FP.FromInt(100),
                                             out var hitNX, out var hitNZ);
            Check("斜盒射线命中距离", Mathf.Abs(tHit.ToFloat() - 9.2929f) < 0.002f, $"t={tHit.ToFloat():F4}");
            Check("斜盒命中法线随朝向旋转",
                  Mathf.Abs(hitNX.ToFloat() + 0.7071f) < 0.002f && Mathf.Abs(hitNZ.ToFloat() + 0.7071f) < 0.002f,
                  $"n=({hitNX.ToFloat():F3},{hitNZ.ToFloat():F3})");

            // 圆心压在盒心 → 一帧推到最近一面之外，局部 |localZ| = 半厚 + 半径
            var pushX = box.CenterX;
            var pushZ = box.CenterZ;
            LogicArena.ResolveCircle(box, ref pushX, ref pushZ, radius);
            box.ToLocal(pushX, pushZ, out var pushLX, out var pushLZ);
            Check("斜盒圆心推出最近面",
                  !box.Contains(pushX, pushZ) &&
                  Mathf.Abs(Mathf.Abs(pushLZ.ToFloat()) - (box.HalfZ + radius).ToFloat()) < 0.002f,
                  $"local=({pushLX.ToFloat():F3},{pushLZ.ToFloat():F3})");

            // 场景墙：随机走 300 帧，任何一帧都不能钻进墙里（撞墙停住、贴墙滑动都算通过）
            var wWall = new LogicWorld(1);
            var pWall = wWall.AddPlayer(1, new FP(0), new FP(0), 0, 100);
            var wallRng = new DeterministicRNG(99);
            bool insideWall = false;
            for (int i = 0; i < 300 && !insideWall; i++)
            {
                var walkCmd = new Dictionary<int, PlayerCommand>
                {
                    { 1, new PlayerCommand
                          {
                              MoveX = (sbyte)(wallRng.NextInt(255) - 127),
                              MoveZ = (sbyte)(wallRng.NextInt(255) - 127),
                          } },
                };
                wWall.Tick(walkCmd);
                for (int w = 0; w < LogicArena.Walls.Length; w++)
                    if (LogicArena.Walls[w].Contains(pWall.X, pWall.Z)) { insideWall = true; break; }
            }
            Check("场景墙随机走 300 帧不穿透", !insideWall,
                  $"pos=({pWall.X.ToFloat():F2},{pWall.Z.ToFloat():F2})");

            // 出生在墙心 → 一帧推出到最近的长边之外（圆心离墙面正好一个半径）
            var wall = LogicArena.Walls[0];
            var wInside = new LogicWorld(1);
            var pInside = wInside.AddPlayer(1, wall.CenterX, wall.CenterZ, 0, 100);
            wInside.Tick(new Dictionary<int, PlayerCommand>());
            wall.ToLocal(pInside.X, pInside.Z, out var insideLX, out var insideLZ);
            float clearance = Mathf.Abs(insideLZ.ToFloat()) - wall.HalfZ.ToFloat();
            Check("卡进墙里被推到最近面",
                  !wall.Contains(pInside.X, pInside.Z) &&
                  Mathf.Abs(insideLX.ToFloat()) < wall.HalfX.ToFloat() && // 推的是长边，不是端头
                  Mathf.Abs(clearance - radius.ToFloat()) < 0.002f,
                  $"local=({insideLX.ToFloat():F2},{insideLZ.ToFloat():F2}) clearance={clearance:F3}");
        }

        // ===== 跳跃与落地：单帧起跳 → 上升 → 60帧内回到地面且不再下坠 =====
        var wJump = new LogicWorld(1);
        var pJump = wJump.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        wJump.Tick(new Dictionary<int, PlayerCommand> { { 1, new PlayerCommand { Buttons = PlayerCommand.BTN_JUMP } } });
        bool rose = pJump.Y.ToFloat() > 0.1f;
        float yAfterJump = pJump.Y.ToFloat(); // 立刻取，详情日志用
        var idle = new Dictionary<int, PlayerCommand> { { 1, new PlayerCommand() } };
        bool neverNegative = true;
        for (int i = 0; i < 60; i++)
        {
            wJump.Tick(idle);
            if (pJump.Y.Raw < 0) neverNegative = false;
        }
        Check("跳跃上升", rose, $"y1帧后={yAfterJump:F3}");
        Check("落地归零且无穿地", pJump.Y.Raw == 0 && neverNegative && pJump.IsGrounded);

        Debug.Log($"========== 逻辑世界自测: {pass} 通过, {fail} 失败 ==========");
        if (fail == 0) Debug.Log("🎉 阶段2逻辑世界可靠，可以进入阶段3（输入指令化与本地预测）");
    }

    private static LogicWorld CreateSampleWorld()
    {
        var w = new LogicWorld(42);
        for (int i = 0; i < 4; i++)
        {
            LogicWorld.GetSpawnPoint(i, out var x, out var z);
            w.AddPlayer(1000 + i, x, z, FPAngle.FromDegrees(i * 90), LockstepConfig.MAX_HP);
        }
        return w;
    }

    /// <summary>用 RNG 生成一帧全员指令（按 id 升序消费——两世界同种子得到同指令流）</summary>
    private static Dictionary<int, PlayerCommand> NextCommands(DeterministicRNG rng, LogicWorld w)
    {
        var cmds = new Dictionary<int, PlayerCommand>();
        foreach (var p in w.Players)
        {
            cmds[p.PlayerId] = new PlayerCommand
            {
                MoveX = rng.NextInt(255) - 127,
                MoveZ = rng.NextInt(255) - 127,
                AimAngle = rng.NextAngle(),
                Buttons = (byte)rng.NextInt(16),
            };
        }
        return cmds;
    }
}
