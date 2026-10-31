using System.Collections.Generic;
using GameLockstep;

/// <summary>
/// 实时会话：LockstepClient（预测/确认/回滚）+ 网络桥。
/// 驱动只看到“提交本地指令 / 发包 / 推进”三个动作。
/// </summary>
public sealed class LiveLockstepSession : ILockstepSession
{
    private readonly LockstepClient _client;
    private readonly LockstepNetworkBridge _bridge;

    public LiveLockstepSession(LockstepClient client, int localSeat, bool offline, IReadOnlyList<int> seats)
    {
        _client = client;
        LocalSeat = localSeat;
        _bridge = new LockstepNetworkBridge(client, localSeat, offline, seats);
    }

    /// <summary>断线重连：world 已快进到 startFrame，从该帧接回实时预测</summary>
    public LiveLockstepSession(LogicWorld world, int localSeat, bool offline, IReadOnlyList<int> seats,
                               long startFrame, IReadOnlyDictionary<int, PlayerCommand> lastKnown)
    {
        LocalSeat = localSeat;
        _client = new LockstepClient(localSeat, world, startFrame, lastKnown);
        _bridge = new LockstepNetworkBridge(_client, localSeat, offline, seats);
    }

    public LogicWorld World => _client.World;
    public int LocalSeat { get; }
    public bool IsReplay => false;
    public long LatestServerFrame => _client.LatestAuthoritativeFrame;
    public int RollbackCount => _client.RollbackCount;

    public void SubmitLocal(long frame, PlayerCommand command) => _client.SubmitLocalCommand(frame, command);
    public void FlushSends(long wallFrame) => _bridge.FlushLocalSends(wallFrame);
    public void Advance(long wallFrame) => _client.Advance(wallFrame);
    public bool TryDequeueEvent(out LogicEvent ev) => _client.TryDequeueEvent(out ev);

    public void Dispose() => _bridge.Dispose();
}
