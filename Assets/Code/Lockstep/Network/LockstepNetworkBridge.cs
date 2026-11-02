using System;
using System.Collections.Generic;
using GameLockstep;
using GameNetwork;

/// <summary>
/// 网络接缝：把 LockstepClient 的“喂权威帧 / 取待发包”接到 UDP 链路。
/// 单机模式改用 OfflineLockstepRelay 本地封帧，走完全相同的确认链路。
/// 驱动只调 FlushLocalSends，不认识消息号和 NetworkManager。
/// </summary>
public sealed class LockstepNetworkBridge : IDisposable
{
    private readonly LockstepClient _client;
    private readonly OfflineLockstepRelay _offlineRelay; // null = 联机
    private readonly int _localSeat;

    public LockstepNetworkBridge(LockstepClient client, int localSeat, bool offline, IReadOnlyList<int> seats)
    {
        _client = client;
        _localSeat = localSeat;

        if (offline) _offlineRelay = new OfflineLockstepRelay(seats);

        LockstepNetEvents.FrameInputsReceived += OnFrameInputs;
    }

    private void OnFrameInputs(long frame, IReadOnlyDictionary<int, PlayerCommand> inputs)
        => _client.OnAuthoritativeInputs(frame, inputs);

    /// <summary>每个逻辑帧：取走本地待发指令 → 联机发包 / 单机本地封帧回喂</summary>
    public void FlushLocalSends(long wallFrame)
    {
        var pending = _client.DrainPendingSends();

        if (_offlineRelay != null)
        {
            _offlineRelay.Submit(pending, _localSeat);
            _offlineRelay.SealUpTo(wallFrame, _client.OnAuthoritativeInputs);
        }
        else if (pending.Count > 0 && NetworkManager.Instance != null && NetworkManager.Instance.IsConnected)
        {
            NetworkManager.Instance.Send(MsgID.C2SLockstepCommand, LockstepProtoConvert.ToCommandReq(pending));
        }
    }

    public void Dispose() => LockstepNetEvents.FrameInputsReceived -= OnFrameInputs;
}
