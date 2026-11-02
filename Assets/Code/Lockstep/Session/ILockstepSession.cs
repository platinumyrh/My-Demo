using System;
using GameLockstep;

/// <summary>
/// 驱动看到的“会话”：一个逻辑世界 + 每帧怎么推进。
/// 联机/单机由 LiveLockstepSession 提供（预测/回滚/网络），回放由 ReplayLockstepSession 提供（按日志重演）。
/// </summary>
public interface ILockstepSession : IDisposable
{
    LogicWorld World { get; }
    int LocalSeat { get; }
    bool IsReplay { get; }

    /// <summary>服务端已封出的最大帧号（0 = 还没收到权威帧）。联机的帧号时钟只跟它对齐，不看本机时钟</summary>
    long LatestServerFrame { get; }

    /// <summary>回滚重演累计次数。驱动每逻辑帧比对一次，用来识别"这一帧位置被纠错改过"</summary>
    int RollbackCount { get; }

    void SubmitLocal(long frame, PlayerCommand command);
    void FlushSends(long wallFrame);
    void Advance(long wallFrame);

    /// <summary>
    /// 取出一条待表现的事件（开火/命中/死亡）。
    /// 会话内已按 (帧,类型,射手) 去重排队——回滚重演会重新生成同一帧的事件，
    /// 不缓冲的话只有 Advance 最后一帧的事件能活下来，重演区间里的特效就丢了
    /// </summary>
    bool TryDequeueEvent(out LogicEvent ev);
}
