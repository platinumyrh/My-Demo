using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameLockstep;
using UnityEngine;

/// <summary>
/// 客户端录像：订阅 LockstepClient 的权威帧确认，按与服务端 ReplayRecorder 相同的 .rpl 格式边打边写。
///
/// 录的是“本端确认过的权威指令流”——预测帧不录，回滚重演也不会重复确认，所以正常情况下
/// 内容和服务端那份一致。区别是完整性只到本端收到的最后一帧：丢包/掉线缺帧时，
/// ReplayData 会停在缺口，回放只能看到缺口之前。
///
/// 只负责写文件，读取仍由 ReplayData 负责；回放会话不创建本类。
/// </summary>
public sealed class ClientReplayRecorder : IDisposable
{
    private const string Magic = "LOCKSTEP_REPLAY 1";

    private readonly LockstepClient _client;
    private readonly StreamWriter _writer;
    private readonly int _seatCount;
    private bool _disposed;

    public string FilePath { get; }

    private ClientReplayRecorder(string filePath, StreamWriter writer, int seatCount, LockstepClient client)
    {
        FilePath = filePath;
        _writer = writer;
        _seatCount = seatCount;
        _client = client;
    }

    /// <summary>
    /// 开始录制：先写文件头 + 座位表，再挂到客户端“权威帧确认”上。
    /// 创建失败只告警返回 null，不影响对局
    /// </summary>
    public static ClientReplayRecorder Start(ulong seed, int tickRate,
                                             IReadOnlyList<KeyValuePair<string, int>> seats,
                                             LockstepClient client)
    {
        if (client == null || seats == null || seats.Count == 0) return null;

        string path = Path.Combine(ReplayData.DefaultDirectory,
            $"client_{DateTime.Now:yyyyMMdd_HHmmss_fff}.rpl");

        StreamWriter writer = null;
        try
        {
            Directory.CreateDirectory(ReplayData.DefaultDirectory);
            writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };

            writer.WriteLine(Magic);
            writer.WriteLine("seed " + seed.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("tick " + tickRate.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("seats " + seats.Count.ToString(CultureInfo.InvariantCulture));

            var sorted = new List<KeyValuePair<string, int>>(seats);
            sorted.Sort((a, b) => a.Value.CompareTo(b.Value));
            var pm = PlayerManager.GetInstance();
            foreach (var s in sorted)
            {
                string name = pm != null ? pm.ResolveDisplayName(s.Key, s.Key) : s.Key;
                writer.WriteLine($"seat {s.Value} {B64(s.Key)} {B64(name)}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Replay] ⚠️ 客户端录像创建失败（对局继续）: {ex.Message}");
            writer?.Dispose();
            return null;
        }

        var recorder = new ClientReplayRecorder(path, writer, seats.Count, client);
        client.FrameConfirmed += recorder.OnFrameConfirmed;
        Debug.Log($"[Replay] 💾 客户端开始录制: {path}");
        return recorder;
    }

    /// <summary>权威帧确认回调：按服务端格式写一行（帧号 + 每个座位 5 个字段）</summary>
    private void OnFrameConfirmed(long frame, IReadOnlyDictionary<int, PlayerCommand> inputs)
    {
        if (_disposed) return;

        try
        {
            var sb = new StringBuilder(16 + _seatCount * 24);
            sb.Append('f').Append(' ').Append(frame.ToString(CultureInfo.InvariantCulture));
            for (int seat = 0; seat < _seatCount; seat++)
            {
                var cmd = inputs != null && inputs.TryGetValue(seat, out var c) ? c : PlayerCommand.Empty;
                sb.Append(' ').Append(seat.ToString(CultureInfo.InvariantCulture))
                  .Append(' ').Append(cmd.MoveX.ToString(CultureInfo.InvariantCulture))
                  .Append(' ').Append(cmd.MoveZ.ToString(CultureInfo.InvariantCulture))
                  .Append(' ').Append(cmd.AimAngle.ToString(CultureInfo.InvariantCulture))
                  .Append(' ').Append(cmd.Buttons.ToString(CultureInfo.InvariantCulture));
            }
            _writer.WriteLine(sb.ToString());
        }
        catch (Exception ex)
        {
            // 写盘失败就停录，别让 IO 问题影响帧循环
            Debug.LogWarning($"[Replay] ⚠️ 客户端录像写入失败，停止录制: {ex.Message}");
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _client.FrameConfirmed -= OnFrameConfirmed;
        try
        {
            _writer.WriteLine("end");
            _writer.Dispose();
        }
        catch
        {
            // 收尾失败不影响退出
        }
    }

    private static string B64(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
}
