using System;
using System.Collections.Generic;

namespace GameLockstep
{
    /// <summary>
    /// 帧同步客户端会话：本地预测 + 权威确认 + 回滚重演（纯 C#，不碰网络不碰 Unity——
    /// 网络接线是阶段5的事，本类只定义"喂指令/取待发/推进"三个接缝）
    ///
    /// 接缝约定：
    /// - 输入端：表现层把玩家操作量化成 PlayerCommand 提交，带生效帧号（= 当前帧 + INPUT_DELAY_FRAMES）
    /// - 发送端：网络层定期 DrainPendingSends 取走待发指令发往服务器（每条只取一次）
    /// - 接收端：网络层把服务器封帧的全员指令喂给 OnAuthoritativeInputs（乱序/重复均安全）
    /// - 驱动：游戏循环按 15Hz 调 Advance(targetFrame)——先吸收连续权威帧，再把世界预测推进到目标帧
    ///
    /// 预测假设：自己 = 本地指令；别人 = 延续其最后一条已确认指令（hold-last）。
    /// 权威输入与预测不符 → 回滚到分歧帧前一快照，权威优先重演到当前
    /// </summary>
    public sealed class LockstepClient
    {
        public LogicWorld World { get; }
        public int LocalPlayerId { get; }
        /// <summary>最后一个人人指令已确认的帧</summary>
        public long ConfirmedFrame { get; private set; }
        /// <summary>当前已模拟到的帧（含预测部分，≥ ConfirmedFrame）</summary>
        public long SimFrame { get; private set; }
        /// <summary>发生过的回滚重演次数（性能/调试观测用）</summary>
        public int RollbackCount { get; private set; }

        private readonly Dictionary<long, PlayerCommand> _localCommands = new Dictionary<long, PlayerCommand>();//帧号，输入指令；记录本地玩家的输入情况
        private readonly Dictionary<int, PlayerCommand> _lastKnown = new Dictionary<int, PlayerCommand>();//玩家id，输入指令；记录其他玩家最后一次确认的指令
        //权威缓冲区。网络层收到的服务器真值暂时扔在这里。因为网络是乱序的，必须等齐了才能按顺序吃（Advance 的 A 阶段）。
        private readonly Dictionary<long, Dictionary<int, PlayerCommand>> _authBuffer = new Dictionary<long, Dictionary<int, PlayerCommand>>();
        //
        private readonly Dictionary<long, Dictionary<int, PlayerCommand>> _usedInputs = new Dictionary<long, Dictionary<int, PlayerCommand>>();
        private readonly Dictionary<long, LogicWorldSnapshot> _snapshots = new Dictionary<long, LogicWorldSnapshot>();
        private readonly Queue<KeyValuePair<long, PlayerCommand>> _pendingSends = new Queue<KeyValuePair<long, PlayerCommand>>();

        public LockstepClient(int localPlayerId, LogicWorld world)
        {
            LocalPlayerId = localPlayerId;
            World = world;
            _snapshots[0] = world.CaptureSnapshot(); // 回滚锚点：初始状态
        }

        /// <summary>表现层提交本地指令（forFrame = 生效帧，一般 = 当前帧 + INPUT_DELAY_FRAMES）</summary>
        public void SubmitLocalCommand(long forFrame, in PlayerCommand cmd)
        {
            if (forFrame <= ConfirmedFrame) return; // 已烙进历史的帧不再接受（服务器同样会拒收）

            bool isNew = !_localCommands.TryGetValue(forFrame, out var old) || !SameCommand(old, cmd);
            if (!isNew) return;
            _localCommands[forFrame] = cmd;
            _pendingSends.Enqueue(new KeyValuePair<long, PlayerCommand>(forFrame, cmd));

            // 迟到指令（生效帧已被预测模拟过，如卡顿掉帧时）：立即回滚重演，让操作尽早生效
            if (forFrame <= SimFrame)
                RollbackAndReplay(forFrame);
        }

        /// <summary>网络层取走全部待发指令（每条只会被取走一次）</summary>
        public List<KeyValuePair<long, PlayerCommand>> DrainPendingSends()
        {
            var list = new List<KeyValuePair<long, PlayerCommand>>(_pendingSends.Count);
            while (_pendingSends.Count > 0) list.Add(_pendingSends.Dequeue());
            return list;
        }

        /// <summary>喂入服务器封帧的全员指令。已确认/重复的帧直接丢弃；乱序先缓冲</summary>
        public void OnAuthoritativeInputs(long frame, IReadOnlyDictionary<int, PlayerCommand> inputs)
        {
            if (frame <= ConfirmedFrame) return;
            _authBuffer[frame] = CopyInputs(inputs);
        }

        /// <summary>推进会话：A.吸收连续权威帧（预测有误则回滚重演） B.预测推进到 targetFrame</summary>
        public void Advance(long targetFrame)
        {
            // A. 确认：权威帧连续到达才逐帧烙进历史。全是真值，不受预测上限约束，
            //    但不超过 targetFrame——领先于渲染时钟的帧留在缓冲里，由 B 阶段以权威优先消费
            //    （若在此超前模拟，SimFrame 会越过渲染时钟，且缓冲移除后无法复核）
            while (ConfirmedFrame < targetFrame && _authBuffer.TryGetValue(ConfirmedFrame + 1, out var real))
            {
                long f = ConfirmedFrame + 1;

                bool wasPredicted = _usedInputs.TryGetValue(f, out var used);
                if (wasPredicted && !SameInputs(used, real))
                {
                    RollbackAndReplay(f); // _authBuffer[f] 仍在，重演会以权威优先
                }
                else if (!wasPredicted)
                {
                    SimulateFrame(f, real); // 落后于确认（未预测过），直接按真值模拟
                }
                // else: 预测与真值一致——直接确认，零成本

                _authBuffer.Remove(f);
                foreach (var kv in real)
                    _lastKnown[kv.Key] = kv.Value;
                ConfirmedFrame = f;
                PruneHistory();
            }

            // B. 预测：推进到目标帧。超前已确认帧 MAX_PREDICTION_FRAMES 为上限——
            //    断流时预测自然停在此处不空转
            long cap = ConfirmedFrame + LockstepConfig.MAX_PREDICTION_FRAMES;
            if (targetFrame > cap) targetFrame = cap;
            while (SimFrame < targetFrame)
                SimulateFrame(SimFrame + 1, BuildInputs(SimFrame + 1));
        }

        /// <summary>回滚到 frame-1 的快照，重演 frame..SimFrame（BuildInputs 权威优先）</summary>
        private void RollbackAndReplay(long frame)
        {
            long replayTo = SimFrame;
            RestoreClientSnapshot(frame - 1);
            RollbackCount++;
            for (long f = frame; f <= replayTo; f++)
                SimulateFrame(f, BuildInputs(f));
        }

        private void SimulateFrame(long f, Dictionary<int, PlayerCommand> inputs)
        {
            _usedInputs[f] = inputs;
            World.Tick(inputs);
            SimFrame = f;
            _snapshots[f] = World.CaptureSnapshot();
        }

        /// <summary>重建第 f 帧输入：权威缓冲优先；否则 自己=本地指令 / 别人=hold-last</summary>
        private Dictionary<int, PlayerCommand> BuildInputs(long f)
        {
            if (_authBuffer.TryGetValue(f, out var auth))
                return CopyInputs(auth);

            var inputs = new Dictionary<int, PlayerCommand>();
            foreach (var p in World.Players)
            {
                if (p.PlayerId == LocalPlayerId)
                    inputs[p.PlayerId] = _localCommands.TryGetValue(f, out var own) ? own : PlayerCommand.Empty;
                else
                    inputs[p.PlayerId] = _lastKnown.TryGetValue(p.PlayerId, out var last) ? last : PlayerCommand.Empty;
            }
            return inputs;
        }

        private void RestoreClientSnapshot(long frame)
        {
            if (!_snapshots.TryGetValue(frame, out var snap))
                throw new InvalidOperationException($"回滚锚点快照缺失: frame={frame}（已确认帧={ConfirmedFrame}）");
            World.RestoreSnapshot(snap);
            SimFrame = frame;
            RemoveKeysAtOrAbove(_usedInputs, frame);   // 作废该帧起的所有预测记录
            RemoveKeysAtOrAbove(_snapshots, frame);    // 由重演重新写入
        }

        /// <summary>确认后裁剪历史：逐帧输入与本地指令只留未确认部分，快照保留确认帧本身作回滚锚点</summary>
        private void PruneHistory()
        {
            RemoveKeysAtOrBelow(_usedInputs, ConfirmedFrame);
            RemoveKeysAtOrBelow(_localCommands, ConfirmedFrame);
            RemoveKeysBelow(_snapshots, ConfirmedFrame);
        }

        private static bool SameCommand(in PlayerCommand a, in PlayerCommand b)
        {
            return a.MoveX == b.MoveX && a.MoveZ == b.MoveZ && a.AimAngle == b.AimAngle && a.Buttons == b.Buttons;
        }

        /// <summary>两组全员指令是否一致（按当前世界玩家集合比较）</summary>
        private bool SameInputs(Dictionary<int, PlayerCommand> a, Dictionary<int, PlayerCommand> b)
        {
            foreach (var p in World.Players)
            {
                if (!a.TryGetValue(p.PlayerId, out var ca)) return false;
                if (!b.TryGetValue(p.PlayerId, out var cb)) return false;
                if (!SameCommand(ca, cb)) return false;
            }
            return true;
        }

        private static Dictionary<int, PlayerCommand> CopyInputs(IReadOnlyDictionary<int, PlayerCommand> src)
        {
            var copy = new Dictionary<int, PlayerCommand>(src.Count);
            foreach (var kv in src) copy[kv.Key] = kv.Value;
            return copy;
        }

        private static void RemoveKeysAtOrBelow<T>(Dictionary<long, T> dict, long threshold)
        {
            List<long> remove = null;
            foreach (var kv in dict)
                if (kv.Key <= threshold) (remove ??= new List<long>()).Add(kv.Key);
            if (remove != null)
                foreach (var k in remove) dict.Remove(k);
        }

        private static void RemoveKeysBelow<T>(Dictionary<long, T> dict, long threshold)
        {
            List<long> remove = null;
            foreach (var kv in dict)
                if (kv.Key < threshold) (remove ??= new List<long>()).Add(kv.Key);
            if (remove != null)
                foreach (var k in remove) dict.Remove(k);
        }

        private static void RemoveKeysAtOrAbove<T>(Dictionary<long, T> dict, long threshold)
        {
            List<long> remove = null;
            foreach (var kv in dict)
                if (kv.Key >= threshold) (remove ??= new List<long>()).Add(kv.Key);
            if (remove != null)
                foreach (var k in remove) dict.Remove(k);
        }
    }
}
