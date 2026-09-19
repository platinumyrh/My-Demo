using System.Collections.Generic;

namespace GameLockstep
{
    /// <summary>
    /// 逻辑世界：固定步长确定性模拟器（纯 C#，客户端/服务端共用同一份源码——阶段6 拷贝到服务端）
    ///
    /// Tick 内固定顺序（全端必须一致，改动顺序 = 改协议）：
    ///   1. 按 PlayerId 升序逐个移动积分（缺指令 = 空指令，站桩）
    ///   2. 圆形碰撞互推（跑两轮缓解多人堆叠残留，配对顺序按 id 升序）
    ///   3. 世界边界钳制
    /// 输入延迟/本地预测/回滚重演（阶段3）在调用方完成——本类只消费"当前帧指令"，可重入重演
    /// </summary>
    public sealed class LogicWorld
    {
        private readonly List<LogicPlayer> _players = new List<LogicPlayer>(); // 始终按 PlayerId 升序
        private readonly Dictionary<int, LogicPlayer> _byId = new Dictionary<int, LogicPlayer>();

        public long Frame { get; private set; }
        public DeterministicRNG Rng { get; }
        public IReadOnlyList<LogicPlayer> Players => _players;

        public LogicWorld(ulong rngSeed)
        {
            Rng = new DeterministicRNG(rngSeed);
        }

        public LogicPlayer AddPlayer(int playerId, FP x, FP z, int yaw, int maxHp)
        {
            if (_byId.TryGetValue(playerId, out var existed))
                return existed; // 重复加入幂等（重连/快照恢复场景）

            var p = new LogicPlayer(playerId, x, z, yaw, maxHp);
            _byId[playerId] = p;
            _players.Add(p);
            _players.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId)); // 遍历顺序 = 确定性顺序
            return p;
        }

        /// <summary>断线移除（阶段6 中继侧调用）</summary>
        public bool RemovePlayer(int playerId)
        {
            if (!_byId.Remove(playerId)) return false;
            _players.RemoveAll(p => p.PlayerId == playerId);
            return true;
        }

        public LogicPlayer GetPlayer(int playerId) =>
            _byId.TryGetValue(playerId, out var p) ? p : null;

        /// <summary>推进一个逻辑帧</summary>
        public void Tick(IReadOnlyDictionary<int, PlayerCommand> commands)
        {
            Frame++;

            // 1. 移动积分（缺指令按空指令处理，不卡帧）
            for (int i = 0; i < _players.Count; i++)
            {
                commands.TryGetValue(_players[i].PlayerId, out var cmd);
                _players[i].ApplyMovement(cmd);
            }

            // 2. 圆形碰撞互推
            ResolveCollisions();
            ResolveCollisions();

            // 3. 世界边界
            var limit = FP.FromRaw(LockstepConfig.WORLD_LIMIT);
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.X > limit) p.X = limit;
                if (p.X < -limit) p.X = -limit;
                if (p.Z > limit) p.Z = limit;
                if (p.Z < -limit) p.Z = -limit;
            }
        }

        /// <summary>
        /// 圆形碰撞：等半径互推各让一半重叠量（纯位置修正，无速度冲量）
        /// </summary>
        private void ResolveCollisions()
        {
            // (2R)² 预先算好——与 SqrDistance 同在 值×2^32 域，比较免开方
            long minDistRaw = 2 * LockstepConfig.PLAYER_RADIUS;
            long minSqrRaw = minDistRaw * minDistRaw;

            for (int i = 0; i < _players.Count; i++)
            {
                for (int j = i + 1; j < _players.Count; j++)
                {
                    var a = _players[i];
                    var b = _players[j];

                    long sqrRaw = FP.SqrDistance(a.X, a.Z, b.X, b.Z);
                    if (sqrRaw >= minSqrRaw) continue;

                    var dist = FP.Sqrt(sqrRaw); // Q16.16
                    if (dist.IsZero)
                    {
                        // 完全重合的确定性兜底：固定沿 ±X 各推一个半径，恰好分开到 2R
                        var r = FP.FromRaw(LockstepConfig.PLAYER_RADIUS);
                        a.X += r;
                        b.X -= r;
                        continue;
                    }

                    var overlap = FP.FromRaw(minDistRaw) - dist;
                    var push = overlap / FP.FromInt(2);
                    var ux = (a.X - b.X) / dist; // 单位分离向量
                    var uz = (a.Z - b.Z) / dist;
                    a.X += ux * push;
                    a.Z += uz * push;
                    b.X -= ux * push;
                    b.Z -= uz * push;
                }
            }
        }

        /// <summary>出生点：8 个均匀分布在半径 8m 圆上，按加入序号取。
        /// 全端一致性依赖"加入顺序由服务端广播保证"（阶段6 落实）</summary>
        public static void GetSpawnPoint(int index, out FP x, out FP z)
        {
            int angle = (index % 8) * (LockstepConfig.ANGLE_STEPS / 8);
            var dir = FPTrig.Dir(angle);
            var radius = FP.FromInt(8);
            x = dir.X * radius;
            z = dir.Z * radius;
        }

        /// <summary>状态哈希（阶段8 跨端漂移比对用）。纯整数混合，禁用运行时相关哈希（如字符串 GetHashCode）</summary>
        public long ComputeStateHash()
        {
            long h = unchecked((long)0x9E3779B97F4A7C15UL) ^ Frame;
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                h = Mix(h, p.PlayerId);
                h = Mix(h, p.X.Raw);
                h = Mix(h, p.Z.Raw);
                h = Mix(h, p.Y.Raw);
                h = Mix(h, p.Yaw);
                h = Mix(h, p.Hp);
                h = Mix(h, p.Ammo);
                h = Mix(h, p.IsDead ? 1 : 0);
            }
            return h;
        }

        private static long Mix(long h, long v)
        {
            h ^= v;
            h *= unchecked((long)0xFF51AFD7ED558CCDUL);
            h ^= h >> 33;
            return h;
        }
    }
}
