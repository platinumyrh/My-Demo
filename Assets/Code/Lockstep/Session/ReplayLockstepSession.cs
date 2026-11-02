using System.Collections.Generic;
using GameLockstep;

/// <summary>
/// 回放会话：没有网络、没有预测、没有快照，按录像里的权威指令逐帧重演。
/// Advance 被墙钟按 15Hz 调一次，就往前走一帧。
/// </summary>
public sealed class ReplayLockstepSession : ILockstepSession
{
    private readonly ReplayData _data;
    private readonly Queue<LogicEvent> _events = new Queue<LogicEvent>();
    private int _next;

    public ReplayLockstepSession(ReplayData data, LogicWorld world, int localSeat)
    {
        _data = data;
        World = world;
        LocalSeat = localSeat;
    }

    public LogicWorld World { get; }
    public int LocalSeat { get; }
    public bool IsReplay => true;
    public long LatestServerFrame => 0; // 回放没有服务端，驱动走本地累加器
    public int RollbackCount => 0;      // 回放全是权威帧，没有回滚

    public bool IsFinished => _next >= _data.Frames.Count;

    public void SubmitLocal(long frame, PlayerCommand command) { }
    public void FlushSends(long wallFrame) { }

    public void Advance(long wallFrame)
    {
        if (_next >= _data.Frames.Count) return;
        World.Tick(_data.GetInputs(_next));
        _next++;
        // 回放没有预测/回滚，每帧事件只生成一次，直接排队
        foreach (var ev in World.TickEvents) _events.Enqueue(ev);
    }

    public bool TryDequeueEvent(out LogicEvent ev)
    {
        if (_events.Count == 0)
        {
            ev = default;
            return false;
        }
        ev = _events.Dequeue();
        return true;
    }

    public void Dispose() { }
}
