using System;
using System.Collections.Generic;

namespace GameLockstep
{
    /// <summary>
    /// lockstep 网络事件枢纽：NetworkHandler（解析层）→ 会话驱动层（阶段7 接入）。
    /// 高频路径不走 EventBus——与 FrameSyncManager 同一纪律。
    /// 事件在 Unity 主线程触发（NetworkManager 的分发线程模型决定）
    /// </summary>
    public static class LockstepNetEvents
    {
        /// <summary>收到封帧全员指令（一帧一条，已转换为 座位→指令）</summary>
        public static event Action<long, IReadOnlyDictionary<int, PlayerCommand>> FrameInputsReceived;

        /// <summary>GameStart 携带的 lockstep 初始化（种子/帧率/座位表）。
        /// seed==0 且座位表为空 = 旧服务器未实现新链路，驱动层据此回退</summary>
        public static event Action<ulong, int, IReadOnlyList<KeyValuePair<string, int>>> GameStartReceived;

        internal static void RaiseFrameInputs(long frame, IReadOnlyDictionary<int, PlayerCommand> inputs) =>
            FrameInputsReceived?.Invoke(frame, inputs);

        internal static void RaiseGameStart(ulong seed, int tickRate, IReadOnlyList<KeyValuePair<string, int>> seats) =>
            GameStartReceived?.Invoke(seed, tickRate, seats);
    }
}
