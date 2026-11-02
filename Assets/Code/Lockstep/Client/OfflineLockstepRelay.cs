using System;
using System.Collections.Generic;

namespace GameLockstep
{
    /// <summary>
    /// 单机模式下的“本地服务器”：只做服务端封帧这一件事。
    /// 本地指令按生效帧缓冲，墙钟走到该帧时封帧回喂给 LockstepClient，
    /// 让没有网络时也能走完“预测 → 权威确认”的同一条链路。
    /// </summary>
    public sealed class OfflineLockstepRelay
    {
        private readonly int[] _seats;
        private readonly Dictionary<long, Dictionary<int, PlayerCommand>> _buffered = new();
        private long _lastSealed;

        public OfflineLockstepRelay(IReadOnlyList<int> seats)
        {
            _seats = new int[seats.Count];
            for (int i = 0; i < seats.Count; i++) _seats[i] = seats[i];
        }

        /// <summary>接收本地指令（等价于服务器收到的 C2S_LockstepCommand）</summary>
        public void Submit(IReadOnlyList<KeyValuePair<long, PlayerCommand>> pending, int localSeat)
        {
            foreach (var kv in pending)
            {
                if (!_buffered.TryGetValue(kv.Key, out var frame))
                {
                    frame = new Dictionary<int, PlayerCommand>();
                    _buffered[kv.Key] = frame;
                }
                frame[localSeat] = kv.Value;
            }
        }

        /// <summary>封掉 frame 及之前未封的帧并回喂（缺席座位补空指令，与服务器超时补齐一致）</summary>
        public void SealUpTo(long frame, Action<long, IReadOnlyDictionary<int, PlayerCommand>> onSealed)
        {
            while (_lastSealed < frame)
            {
                long f = ++_lastSealed;
                var inputs = new Dictionary<int, PlayerCommand>(_seats.Length);
                _buffered.TryGetValue(f, out var buffered);
                foreach (int seat in _seats)
                    inputs[seat] = buffered != null && buffered.TryGetValue(seat, out var cmd)
                        ? cmd
                        : PlayerCommand.Empty;
                _buffered.Remove(f);
                onSealed(f, inputs);
            }
        }
    }
}
