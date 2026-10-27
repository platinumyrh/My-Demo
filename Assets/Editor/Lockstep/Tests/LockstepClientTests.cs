using System.Collections.Generic;
using GameLockstep;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 客户端预测与回滚自测（Tools/Lockstep/客户端预测自测）
/// 用"封帧表 + 可调网络延迟"沙盒驱动 LockstepClient，与全知参照世界逐位比对：
/// 即时确认零回滚 / 预测命中零回滚 / 预测落空触发回滚且终态与参照一致 /
/// 乱序全量投递 / 重复投递幂等 / 迟到本地指令立即回滚生效 / 发送队列一次性 / 快照保真
/// </summary>
public static class LockstepClientTests
{
    [MenuItem("Tools/Lockstep/客户端预测自测")]
    public static void RunAll()
    {
        int pass = 0, fail = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) { pass++; Debug.Log($"✅ {name} {detail}"); }
            else { fail++; Debug.LogError($"❌ {name} {detail}"); }
        }

        const int frames = 40;

        // ===== 场景1：延迟0——封帧总在模拟前到达，纯确认路径，零回滚 =====
        RunScenario(frames, netDelay: 0, remoteIdle: false, out var c1, out var r1);
        Check("延迟0 即时确认零回滚", c1.RollbackCount == 0, $"rollbacks={c1.RollbackCount}");
        Check("延迟0 终态=参照", c1.World.ComputeStateHash() == r1.ComputeStateHash()
              && c1.ConfirmedFrame == frames && c1.SimFrame == frames);

        // ===== 场景2：延迟3 + 远端静止——预测(空)与真值(空)一致，零回滚 =====
        RunScenario(frames, netDelay: 3, remoteIdle: true, out var c2, out var r2);
        Check("预测命中零回滚", c2.RollbackCount == 0, $"rollbacks={c2.RollbackCount}");
        Check("预测命中 终态=参照", c2.World.ComputeStateHash() == r2.ComputeStateHash());

        // ===== 场景3（核心）：延迟3 + 远端乱动——hold-last 预测必落空，回滚校正后终态仍=参照 =====
        RunScenario(frames, netDelay: 3, remoteIdle: false, out var c3, out var r3);
        Check("预测落空触发回滚", c3.RollbackCount > 0, $"rollbacks={c3.RollbackCount}");
        Check("回滚校正 终态=参照", c3.World.ComputeStateHash() == r3.ComputeStateHash()
              && c3.ConfirmedFrame == frames && c3.SimFrame == frames);

        // ===== 场景4：乱序——全部封帧倒序投递再推进，确认帧序正确、终态=参照 =====
        var (world4, truth4) = BuildTruth(3, frames);
        var c4 = new LockstepClient(1, world4);
        for (long f = frames; f >= 1; f--)
            c4.OnAuthoritativeInputs(f, truth4[f]);
        c4.Advance(frames);
        Check("乱序全量投递 终态=参照", c4.ConfirmedFrame == frames && c4.SimFrame == frames
              && c4.World.ComputeStateHash() == ReferenceHash(truth4, frames)
              && c4.RollbackCount == 0);

        // ===== 场景5：重复投递幂等 =====
        var (world5, truth5) = BuildTruth(4, frames);
        var c5 = new LockstepClient(1, world5);
        c5.OnAuthoritativeInputs(1, truth5[1]);
        c5.OnAuthoritativeInputs(1, truth5[1]); // 重复
        for (long f = 2; f <= frames; f++) c5.OnAuthoritativeInputs(f, truth5[f]);
        c5.Advance(frames);
        Check("重复投递幂等 终态=参照", c5.ConfirmedFrame == frames
              && c5.World.ComputeStateHash() == ReferenceHash(truth5, frames));

        // ===== 场景6：迟到本地指令——生效帧已被预测过，提交后立即回滚生效 =====
        LogicWorld.GetSpawnPoint(0, out var sx, out var sz);
        var w6 = new LogicWorld(9);
        w6.AddPlayer(7, sx, sz, 0, 100);
        var c6 = new LockstepClient(7, w6);
        c6.Advance(5); // 5 帧空指令
        var move = new PlayerCommand { MoveZ = 127 };
        c6.SubmitLocalCommand(3, move); // 3 ≤ SimFrame=5 且 > ConfirmedFrame=0
        var rw6 = new LogicWorld(9);
        rw6.AddPlayer(7, sx, sz, 0, 100);
        var empty = new Dictionary<int, PlayerCommand> { { 7, PlayerCommand.Empty } };
        var moveDict = new Dictionary<int, PlayerCommand> { { 7, move } };
        rw6.Tick(empty); rw6.Tick(empty);
        rw6.Tick(moveDict); rw6.Tick(empty); rw6.Tick(empty); // 只提交过帧3一条指令，帧4/5应为空
        Check("迟到指令回滚生效", c6.RollbackCount == 1 && c6.SimFrame == 5
              && w6.ComputeStateHash() == rw6.ComputeStateHash(),
              $"rollbacks={c6.RollbackCount}");

        // ===== 场景7：发送队列——每条指令只被取走一次 =====
        var w7 = new LogicWorld(11);
        w7.AddPlayer(1, sx, sz, 0, 100);
        var c7 = new LockstepClient(1, w7);
        c7.SubmitLocalCommand(2, move);
        c7.SubmitLocalCommand(3, move);
        c7.SubmitLocalCommand(4, move);
        int drained = c7.DrainPendingSends().Count;
        int drainedAgain = c7.DrainPendingSends().Count;
        Check("发送队列一次性", drained == 3 && drainedAgain == 0, $"first={drained} second={drainedAgain}");

        // ===== 场景8：快照保真——推进→快照→再推进→恢复，哈希回到快照时刻 =====
        var w8 = new LogicWorld(13);
        w8.AddPlayer(1, sx, sz, 0, 100);
        var tickInputs = new Dictionary<int, PlayerCommand> { { 1, move } };
        for (int i = 0; i < 20; i++) w8.Tick(tickInputs);
        long hashAt20 = w8.ComputeStateHash();
        var snap = w8.CaptureSnapshot();
        for (int i = 0; i < 20; i++) w8.Tick(tickInputs);
        w8.RestoreSnapshot(snap);
        Check("快照还原保真", w8.ComputeStateHash() == hashAt20 && w8.Frame == 20);

        Debug.Log($"========== 客户端预测自测: {pass} 通过, {fail} 失败 ==========");
        if (fail == 0) Debug.Log("🎉 阶段3预测回滚可靠，可以进入阶段4（战斗确定性化）");
    }

    /// <summary>
    /// 场景沙盒：双玩家（1=本地，2=远端）。
    /// 墙钟 w 每拍：提交 w+INPUT_DELAY 帧的本地指令 → 投递已到达的封帧 → Advance(w)。
    /// 封帧 f（RTT=0，服务器在指令到齐即封）在墙钟 f-INPUT_DELAY+netDelay 送达客户端
    /// </summary>
    private static void RunScenario(int frames, int netDelay, bool remoteIdle,
        out LockstepClient client, out LogicWorld reference)
    {
        var (world, truth) = BuildTruth(remoteIdle ? 202 : 203, frames);
        client = new LockstepClient(1, world);

        // 本地指令真值：帧 2..frames+2（墙钟 w 提交 w+2）
        var rngL = new DeterministicRNG(101);
        var localTruth = new Dictionary<long, PlayerCommand>();
        for (long f = 2; f <= frames + 2; f++)
            localTruth[f] = RandomCmd(rngL);

        // 服务器封帧包含全员（回显本地指令）：帧1 空（本地指令尚未产生），帧2起用本地真值
        for (long f = 1; f <= frames; f++)
            truth[f][1] = f == 1 ? PlayerCommand.Empty : localTruth[f];

        var delivered = new HashSet<long>();
        for (long w = 0; w <= frames; w++)
        {
            client.SubmitLocalCommand(w + 2, localTruth[w + 2]);
            for (long f = 1; f <= frames; f++)
            {
                if (!delivered.Contains(f) && w >= f - LockstepConfig.INPUT_DELAY_FRAMES + netDelay)
                {
                    client.OnAuthoritativeInputs(f, truth[f]);
                    delivered.Add(f);
                }
            }
            client.Advance(w);
        }
        // 冲刷剩余封帧并推到 frames（模拟局末缓冲清空）
        for (long f = 1; f <= frames; f++)
            if (!delivered.Contains(f)) client.OnAuthoritativeInputs(f, truth[f]);
        client.Advance(frames);

        reference = BuildTruth(remoteIdle ? 202 : 203, frames).Item1;
        for (long f = 1; f <= frames; f++)
            reference.Tick(truth[f]);
    }

    /// <summary>构建双玩家世界 + 远端指令真值表（帧 1..frames）</summary>
    private static (LogicWorld world, Dictionary<long, Dictionary<int, PlayerCommand>> truth) BuildTruth(
        int remoteSeed, int frames)
    {
        LogicWorld NewWorld()
        {
            var w = new LogicWorld(55);
            LogicWorld.GetSpawnPoint(0, out var x0, out var z0);
            LogicWorld.GetSpawnPoint(1, out var x1, out var z1);
            w.AddPlayer(1, x0, z0, 0, 100);
            w.AddPlayer(2, x1, z1, 0, 100);
            return w;
        }

        var rngR = new DeterministicRNG((ulong)remoteSeed);
        var remoteIdle = remoteSeed == 202;
        var truth = new Dictionary<long, Dictionary<int, PlayerCommand>>();
        for (long f = 1; f <= frames; f++)
        {
            var remote = remoteIdle ? PlayerCommand.Empty : RandomCmd(rngR);
            truth[f] = new Dictionary<int, PlayerCommand> { { 2, remote } }; // 本地指令由场景填
        }
        return (NewWorld(), truth);
    }

    /// <summary>参照世界哈希：逐帧喂远端真值 + 本地空指令（本地指令在乱序/幂等场景不参与）</summary>
    private static long ReferenceHash(Dictionary<long, Dictionary<int, PlayerCommand>> truth, int frames)
    {
        var w = new LogicWorld(55);
        LogicWorld.GetSpawnPoint(0, out var x0, out var z0);
        LogicWorld.GetSpawnPoint(1, out var x1, out var z1);
        w.AddPlayer(1, x0, z0, 0, 100);
        w.AddPlayer(2, x1, z1, 0, 100);
        for (long f = 1; f <= frames; f++)
        {
            var inputs = new Dictionary<int, PlayerCommand> { { 1, PlayerCommand.Empty } };
            foreach (var kv in truth[f]) inputs[kv.Key] = kv.Value;
            w.Tick(inputs);
        }
        return w.ComputeStateHash();
    }

    private static PlayerCommand RandomCmd(DeterministicRNG rng)
    {
        return new PlayerCommand
        {
            MoveX = rng.NextInt(255) - 127,
            MoveZ = rng.NextInt(255) - 127,
            AimAngle = rng.NextAngle(),
            Buttons = (byte)rng.NextInt(16),
        };
    }
}
