using System.Collections.Generic;
using GameLockstep;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 战斗逻辑自测（Tools/Lockstep/战斗逻辑自测）
/// 验证：开火节流与弹药、hitscan 命中/反向 miss/射程/最近目标优先、
/// 空仓自动换弹与手动换弹、击杀与穿透（死者不挡弹）、死亡射手停火、
/// 带战斗的随机指令双世界确定性
/// </summary>
public static class LockstepCombatTests
{
    [MenuItem("Tools/Lockstep/战斗逻辑自测")]
    public static void RunAll()
    {
        int pass = 0, fail = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) { pass++; Debug.Log($"✅ {name} {detail}"); }
            else { fail++; Debug.LogError($"❌ {name} {detail}"); }
        }

        var fire = new PlayerCommand { Buttons = PlayerCommand.BTN_FIRE };

        // ===== 开火节流：按住 FIRE 30 帧 → 每2帧一发共15发，弹药 30-15 =====
        var w1 = new LogicWorld(1);
        var a1 = w1.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var cmds = new Dictionary<int, PlayerCommand> { { 1, fire } };
        int firedCount = 0;
        for (int i = 0; i < 30; i++)
        {
            w1.Tick(cmds);
            foreach (var e in w1.TickEvents)
                if (e.Type == LogicEventType.Fired) firedCount++;
        }
        Check("开火节流 30帧15发", firedCount == 15 && a1.Ammo == 15,
              $"fired={firedCount} ammo={a1.Ammo}");

        // ===== 直射命中：A(0,0)朝+Z，B(0,5) → B掉10血，命中点在B圆心 =====
        var w2 = new LogicWorld(1);
        var a2 = w2.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var b2 = w2.AddPlayer(2, new FP(0), FP.FromInt(5), 0, 100);
        w2.Tick(new Dictionary<int, PlayerCommand> { { 1, fire } });
        Check("直射命中掉血", b2.Hp == 90 && a2.Ammo == 29, $"hp={b2.Hp}");
        LogicEvent hit2 = default; bool foundHit = false;
        foreach (var e in w2.TickEvents) if (e.Type == LogicEventType.Hit) { hit2 = e; foundHit = true; }
        Check("命中事件与落点", foundHit && hit2.TargetId == 2 && hit2.ShooterId == 1
              && Mathf.Abs(hit2.HitZ.ToFloat() - 5f) < 0.01f && Mathf.Abs(hit2.HitX.ToFloat()) < 0.01f,
              $"hit=({hit2.HitX.ToFloat():F2},{hit2.HitZ.ToFloat():F2})");

        // ===== 反向 miss：A 朝 -Z 开火（指令须带 AimAngle——转向先于开火结算）→ B 不掉血 =====
        var w3 = new LogicWorld(1);
        w3.AddPlayer(1, new FP(0), new FP(0), FPAngle.FromDegrees(180), 100);
        var b3 = w3.AddPlayer(2, new FP(0), FP.FromInt(5), 0, 100);
        var fireBack = new PlayerCommand { AimAngle = FPAngle.FromDegrees(180), Buttons = PlayerCommand.BTN_FIRE };
        w3.Tick(new Dictionary<int, PlayerCommand> { { 1, fireBack } });
        LogicEvent miss3 = default; bool foundMiss = false;
        foreach (var e in w3.TickEvents) if (e.Type == LogicEventType.Fired) { miss3 = e; foundMiss = true; }
        Check("反向 miss", b3.Hp == 100 && foundMiss && miss3.TargetId == 0
              && Mathf.Abs(miss3.HitZ.ToFloat() + 50f) < 0.01f, $"b.hp={b3.Hp}");

        // ===== 射程外 miss：B 在 60m，射程 50m =====
        var w4 = new LogicWorld(1);
        w4.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var b4 = w4.AddPlayer(2, new FP(0), FP.FromInt(60), 0, 100);
        w4.Tick(new Dictionary<int, PlayerCommand> { { 1, fire } });
        Check("射程外 miss", b4.Hp == 100);

        // ===== 最近目标优先：B1(0,5) B2(0,10) → B1 中弹 =====
        var w5 = new LogicWorld(1);
        w5.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var b5a = w5.AddPlayer(2, new FP(0), FP.FromInt(5), 0, 100);
        var b5b = w5.AddPlayer(3, new FP(0), FP.FromInt(10), 0, 100);
        w5.Tick(new Dictionary<int, PlayerCommand> { { 1, fire } });
        Check("最近目标优先", b5a.Hp == 90 && b5b.Hp == 100, $"b2={b5a.Hp} b3={b5b.Hp}");

        // ===== 击杀与穿透：10枪杀死B，死后不挡弹，同一射线打到其后的C =====
        var w6 = new LogicWorld(1);
        var a6 = w6.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        var b6 = w6.AddPlayer(2, new FP(0), FP.FromInt(5), 0, 100);
        var c6 = w6.AddPlayer(3, new FP(0), FP.FromInt(8), 0, 100);
        bool sawDied = false;
        for (int i = 0; i < 10; i++)
        {
            w6.Tick(cmds); // 连发（每2帧1发，10发需19帧——下面补齐）
            foreach (var e in w6.TickEvents) if (e.Type == LogicEventType.Died) sawDied = true;
        }
        // 10 帧 = 5 发，再打 10 帧补足 10 发
        for (int i = 0; i < 10; i++)
        {
            w6.Tick(cmds);
            foreach (var e in w6.TickEvents) if (e.Type == LogicEventType.Died) sawDied = true;
        }
        Check("10枪击杀", b6.Hp == 0 && b6.IsDead && sawDied, $"hp={b6.Hp} dead={b6.IsDead}");
        // B 已死：再开一枪应穿透打到 C
        w6.Tick(new Dictionary<int, PlayerCommand> { { 1, PlayerCommand.Empty } });
        w6.Tick(new Dictionary<int, PlayerCommand> { { 1, fire } });
        Check("死者不挡弹（穿透）", c6.Hp == 90 && b6.Hp == 0, $"c.hp={c6.Hp}");

        // ===== 死亡射手停火 =====
        var w7 = new LogicWorld(1);
        var a7 = w7.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        a7.IsDead = true;
        int deadFired = 0;
        for (int i = 0; i < 10; i++)
        {
            w7.Tick(cmds);
            foreach (var e in w7.TickEvents) if (e.Type == LogicEventType.Fired && e.ShooterId == 1) deadFired++;
        }
        Check("死亡射手停火", deadFired == 0 && a7.Ammo == 30);

        // ===== 空仓自动换弹：持续FIRE → 弹药打空后（冷却结束后）触发换弹，23帧后满仓 =====
        var w8 = new LogicWorld(1);
        var a8 = w8.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        int tick = 0;
        while (a8.Ammo > 0 && tick < 200) { w8.Tick(cmds); tick++; }
        int emptyAtTick = tick; // 第 emptyAtTick 帧弹药归零
        bool reloadingStarted = false;
        for (int i = 0; i < 3 && !reloadingStarted; i++) // 冷却2帧后才会再次尝试开火→触发自动换弹
        {
            w8.Tick(cmds); tick++;
            reloadingStarted = a8.ReloadTicksLeft > 0 && a8.Ammo == 0;
        }
        while (a8.ReloadTicksLeft > 0) { w8.Tick(cmds); tick++; }
        Check("空仓自动换弹并装满", reloadingStarted && a8.Ammo == LockstepConfig.MAGAZINE_SIZE,
              $"emptyAt={emptyAtTick} reloadedAt={tick}");

        // ===== 手动换弹：半仓按 RELOAD → 23帧后满仓，期间 FIRE 无效 =====
        var w9 = new LogicWorld(1);
        var a9 = w9.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        a9.Ammo = 10; // 模拟半仓
        var reload = new PlayerCommand { Buttons = PlayerCommand.BTN_RELOAD };
        w9.Tick(new Dictionary<int, PlayerCommand> { { 1, reload } });
        bool reloadStarted = a9.ReloadTicksLeft == LockstepConfig.RELOAD_TICKS;
        for (int i = 0; i < LockstepConfig.RELOAD_TICKS; i++) w9.Tick(new Dictionary<int, PlayerCommand> { { 1, PlayerCommand.Empty } });
        Check("手动换弹", reloadStarted && a9.Ammo == LockstepConfig.MAGAZINE_SIZE && a9.ReloadTicksLeft == 0);

        // ===== 带战斗的随机指令双世界确定性 + 战斗敏感性 =====
        var wa = CreateCombatWorld();
        var wb = CreateCombatWorld();
        var rngA = new DeterministicRNG(314);
        bool same = true; long diffAt = -1;
        for (int f = 0; f < 300; f++)
        {
            var c = RandomCombatCmds(rngA, wa, noFire: f == 100); // 基线：第100帧全员无火
            wa.Tick(c);
            wb.Tick(c);
            if (wa.ComputeStateHash() != wb.ComputeStateHash()) { same = false; diffAt = f; break; }
        }
        Check("战斗随机300帧双世界一致", same, diffAt >= 0 ? $"分歧于 frame={diffAt}" : "");

        // ===== 战斗敏感性：受控场景，唯一差异=第10帧2号开火（此时必然可开火：未开过火未换弹）=====
        LogicWorld SensWorld()
        {
            var w = new LogicWorld(7);
            w.AddPlayer(1, new FP(0), new FP(0), 0, 100);
            w.AddPlayer(2, FP.FromInt(3), new FP(0), FPAngle.FromDegrees(270), 100); // 朝 -X 瞄准1号
            return w;
        }
        var ws1 = SensWorld();
        var ws2 = SensWorld();
        var aimWest = new PlayerCommand { AimAngle = FPAngle.FromDegrees(270) };
        var aimWestFire = new PlayerCommand { AimAngle = FPAngle.FromDegrees(270), Buttons = PlayerCommand.BTN_FIRE };
        for (int f = 0; f < 50; f++)
        {
            ws1.Tick(new Dictionary<int, PlayerCommand> { { 1, PlayerCommand.Empty }, { 2, aimWest } });
            ws2.Tick(new Dictionary<int, PlayerCommand> { { 1, PlayerCommand.Empty }, { 2, f == 10 ? aimWestFire : aimWest } });
        }
        Check("战斗敏感性（单帧单发）", ws1.ComputeStateHash() != ws2.ComputeStateHash());

        Debug.Log($"========== 战斗逻辑自测: {pass} 通过, {fail} 失败 ==========");
        if (fail == 0) Debug.Log("🎉 阶段4战斗确定性可靠，可以进入阶段5（协议改造）");
    }

    /// <summary>三人近距站位（保证随机射击能真实命中/击杀，压力测试战斗路径）</summary>
    private static LogicWorld CreateCombatWorld()
    {
        var w = new LogicWorld(77);
        w.AddPlayer(1, new FP(0), new FP(0), 0, 100);
        w.AddPlayer(2, FP.FromInt(3), new FP(1), 0, 100);
        w.AddPlayer(3, new FP(-2), FP.FromInt(4), 0, 100);
        return w;
    }

    private static Dictionary<int, PlayerCommand> RandomCombatCmds(DeterministicRNG rng, LogicWorld w, bool noFire = false)
    {
        var cmds = new Dictionary<int, PlayerCommand>();
        foreach (var p in w.Players)
        {
            byte buttons = (byte)rng.NextInt(16);
            if (noFire) buttons &= unchecked((byte)~PlayerCommand.BTN_FIRE);
            cmds[p.PlayerId] = new PlayerCommand
            {
                MoveX = rng.NextInt(255) - 127,
                MoveZ = rng.NextInt(255) - 127,
                AimAngle = rng.NextAngle(),
                Buttons = buttons,
            };
        }
        return cmds;
    }
}
