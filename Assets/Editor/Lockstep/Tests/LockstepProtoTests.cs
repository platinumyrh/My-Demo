using System.Collections.Generic;
using GameLockstep;
using GameNetwork;
using Google.Protobuf;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 协议转换自测（Tools/Lockstep/协议转换自测）
/// 验证：PlayerCommand ↔ protobuf 往返（含极值）、批量打包、C2S/S2C 字节级序列化往返、
/// 全链路回环（本地指令→上线→服务器封帧→回线→LockstepClient→与直连参照世界哈希一致）
/// </summary>
public static class LockstepProtoTests
{
    [MenuItem("Tools/Lockstep/协议转换自测")]
    public static void RunAll()
    {
        int pass = 0, fail = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) { pass++; Debug.Log($"✅ {name} {detail}"); }
            else { fail++; Debug.LogError($"❌ {name} {detail}"); }
        }

        // ===== 单条指令往返（含负值/极值）=====
        var src = new PlayerCommand { MoveX = -127, MoveZ = 127, AimAngle = 65535, Buttons = 0x0F };
        var data = new LockstepCommandData { Frame = 123456 };
        data.MoveX = src.MoveX; data.MoveZ = src.MoveZ; data.AimAngle = src.AimAngle; data.Buttons = src.Buttons;
        var back = LockstepProtoConvert.ToCommand(data);
        Check("单条指令往返", back.MoveX == -127 && back.MoveZ == 127 && back.AimAngle == 65535 && back.Buttons == 0x0F);

        // ===== 批量打包 + 逐条还原 =====
        var pending = new List<KeyValuePair<long, PlayerCommand>>
        {
            new KeyValuePair<long, PlayerCommand>(101, new PlayerCommand { MoveX = 10, MoveZ = -20, AimAngle = 300, Buttons = 1 }),
            new KeyValuePair<long, PlayerCommand>(102, new PlayerCommand { MoveX = -1, MoveZ = 0, AimAngle = 65535, Buttons = 2 }),
            new KeyValuePair<long, PlayerCommand>(103, new PlayerCommand { MoveX = 0, MoveZ = 127, AimAngle = 0, Buttons = 8 }),
        };
        var req = LockstepProtoConvert.ToCommandReq(pending);
        bool batchOk = req.Commands.Count == 3;
        for (int i = 0; i < req.Commands.Count && batchOk; i++)
        {
            var c = LockstepProtoConvert.ToCommand(req.Commands[i]);
            batchOk &= req.Commands[i].Frame == pending[i].Key
                       && c.MoveX == pending[i].Value.MoveX && c.MoveZ == pending[i].Value.MoveZ
                       && c.AimAngle == pending[i].Value.AimAngle && c.Buttons == pending[i].Value.Buttons;
        }
        Check("批量打包还原", batchOk);

        // ===== C2S 字节级序列化往返 =====
        byte[] c2sBytes = req.ToByteArray();
        var req2 = LockstepCommandReq.Parser.ParseFrom(c2sBytes);
        Check("C2S 字节往返", req2.Commands.Count == 3 && req2.Commands[1].Frame == 102
              && req2.Commands[2].AimAngle == 0 && req2.Commands[0].Buttons == 1);

        // ===== S2C 字节级序列化往返 =====
        var framesSrc = new Dictionary<long, Dictionary<int, PlayerCommand>>
        {
            { 201, new Dictionary<int, PlayerCommand> {
                { 0, new PlayerCommand { MoveX = 5, MoveZ = 5, AimAngle = 100, Buttons = 1 } },
                { 3, new PlayerCommand { MoveX = -5, MoveZ = 0, AimAngle = 65400, Buttons = 9 } } } },
            { 202, new Dictionary<int, PlayerCommand> {
                { 0, new PlayerCommand() },
                { 3, new PlayerCommand { MoveZ = 127, AimAngle = 32000 } } } },
        };
        var notify = LockstepProtoConvert.ToInputsNotify(framesSrc);
        byte[] s2cBytes = notify.ToByteArray();
        var notify2 = LockstepInputsNotify.Parser.ParseFrom(s2cBytes);
        bool s2cOk = notify2.Frames.Count == 2;
        foreach (var f in notify2.Frames)
        {
            var inputs = LockstepProtoConvert.ToInputs(f);
            if (!framesSrc.TryGetValue(f.Frame, out var expect)) { s2cOk = false; break; }
            foreach (var kv in expect)
            {
                if (!inputs.TryGetValue(kv.Key, out var got)
                    || got.MoveX != kv.Value.MoveX || got.MoveZ != kv.Value.MoveZ
                    || got.AimAngle != kv.Value.AimAngle || got.Buttons != kv.Value.Buttons)
                { s2cOk = false; break; }
            }
        }
        Check("S2C 字节往返", s2cOk);

        // ===== 全链路回环：LockstepClient 发送队列 → 上线 → 服务器封帧 → 回线 → 喂回 =====
        LogicWorld NewWorld()
        {
            var w = new LogicWorld(31);
            LogicWorld.GetSpawnPoint(4, out var x, out var z);
            w.AddPlayer(4, x, z, 0, 100);
            return w;
        }
        var client = new LockstepClient(4, NewWorld());
        var reference = NewWorld();

        var rng = new DeterministicRNG(88);
        // 服务器中继模型（阶段6 落地同一规则）：顺序封帧——
        // 指令齐了就封；帧号到期（≤墙钟）仍缺的补空指令，绝不卡帧
        var serverPending = new Dictionary<long, PlayerCommand>();
        var serverSealed = new Dictionary<long, Dictionary<int, PlayerCommand>>();
        long lastSealed = 0;

        for (long w = 0; w <= 20; w++)
        {
            // 1) 表现层提交未来帧指令
            var cmd = new PlayerCommand
            {
                MoveX = rng.NextInt(255) - 127,
                MoveZ = rng.NextInt(255) - 127,
                AimAngle = rng.NextAngle(),
                Buttons = (byte)rng.NextInt(16),
            };
            client.SubmitLocalCommand(w + 2, cmd);

            // 2) 网络层取待发指令 → 打包 → 字节上线 → 对端解析（服务器收到）
            var c2s = LockstepCommandReq.Parser.ParseFrom(
                LockstepProtoConvert.ToCommandReq(client.DrainPendingSends()).ToByteArray());
            foreach (var d in c2s.Commands)
                serverPending[d.Frame] = LockstepProtoConvert.ToCommand(d);

            // 3) 服务器顺序封帧（齐了就封 / 到期补空）
            var newlySealed = new Dictionary<long, Dictionary<int, PlayerCommand>>();
            while (true)
            {
                long f = lastSealed + 1;
                bool have = serverPending.TryGetValue(f, out var pc);
                if (!have && f > w) break; // 未齐且未到期
                serverSealed[f] = new Dictionary<int, PlayerCommand> { { 4, have ? pc : PlayerCommand.Empty } };
                newlySealed[f] = serverSealed[f];
                lastSealed = f;
            }

            // 4) 封帧广播 → 字节回线 → 客户端网络层解析喂回
            if (newlySealed.Count > 0)
            {
                var s2c = LockstepInputsNotify.Parser.ParseFrom(
                    LockstepProtoConvert.ToInputsNotify(newlySealed).ToByteArray());
                foreach (var f in s2c.Frames)
                    client.OnAuthoritativeInputs(f.Frame, LockstepProtoConvert.ToInputs(f));
            }

            // 5) 推进客户端，并把参照世界按封帧表推进到同一帧
            client.Advance(w);

            while (reference.Frame < w)
            {
                long f = reference.Frame + 1;
                var rc = serverSealed.TryGetValue(f, out var sealedInputs)
                    ? sealedInputs
                    : new Dictionary<int, PlayerCommand> { { 4, PlayerCommand.Empty } };
                reference.Tick(rc);
            }
        }

        Check("全链路回环 终态=直连参照", client.World.ComputeStateHash() == reference.ComputeStateHash()
              && client.ConfirmedFrame == 20 && client.SimFrame == 20 && client.RollbackCount == 0,
              $"rollbacks={client.RollbackCount}");

        Debug.Log($"========== 协议转换自测: {pass} 通过, {fail} 失败 ==========");
        if (fail == 0) Debug.Log("🎉 阶段5协议链路可靠，可以进入阶段6（服务端中继化）");
    }
}
