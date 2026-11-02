using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameLockstep;

/// <summary>
/// 回放文件（服务端录像）读取端。
/// 格式与服务端 ReplayRecorder 一致：一行一条记录，文本、Base64 名字，末行 end。
/// 服务端边打边写，读取时遇到不完整行直接停止，不当作损坏。
/// </summary>
public sealed class ReplayData
{
    public struct SeatInfo
    {
        public int Seat;
        public string PlayerId;
        public string PlayerName;
    }

    public ulong Seed { get; private set; }
    public int TickRate { get; private set; }
    public List<SeatInfo> Seats { get; } = new();
    public List<PlayerCommand[]> Frames { get; } = new();

    public string FilePath { get; private set; } = "";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockstepDemo", "Replays");

    /// <summary>最新一份录像（按修改时间）；没有则返回 null</summary>
    public static string FindLatestFile()
    {
        if (!Directory.Exists(DefaultDirectory)) return null;
        var files = Directory.GetFiles(DefaultDirectory, "*.rpl");
        if (files.Length == 0) return null;

        string latest = files[0];
        for (int i = 1; i < files.Length; i++)
            if (File.GetLastWriteTimeUtc(files[i]) > File.GetLastWriteTimeUtc(latest))
                latest = files[i];
        return latest;
    }

    public static ReplayData Load(string path)
    {
        var data = new ReplayData { FilePath = path };

        using var reader = new StreamReader(path, Encoding.UTF8);
        string magic = reader.ReadLine();
        if (magic != "LOCKSTEP_REPLAY 1")
            throw new InvalidDataException("不是回放文件（文件头不匹配）");

        data.Seed = ReadHeaderValue(reader, "seed",
            (string s, NumberStyles style, IFormatProvider p, out ulong v) => ulong.TryParse(s, style, p, out v));
        data.TickRate = ReadHeaderValue(reader, "tick",
            (string s, NumberStyles style, IFormatProvider p, out int v) => int.TryParse(s, style, p, out v));
        int seatCount = ReadHeaderValue(reader, "seats",
            (string s, NumberStyles style, IFormatProvider p, out int v) => int.TryParse(s, style, p, out v));
        if (seatCount <= 0) throw new InvalidDataException("座位数为 0");

        for (int i = 0; i < seatCount; i++)
        {
            var parts = reader.ReadLine()?.Split(' ');
            if (parts == null || parts.Length < 4 || parts[0] != "seat") break;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seat)) break;
            data.Seats.Add(new SeatInfo
            {
                Seat = seat,
                PlayerId = FromB64(parts[2]),
                PlayerName = FromB64(parts[3]),
            });
        }
        if (data.Seats.Count != seatCount) throw new InvalidDataException("座位表不完整");
        data.Seats.Sort((a, b) => a.Seat.CompareTo(b.Seat));

        ParseFrames(reader, data);
        if (data.Frames.Count == 0) throw new InvalidDataException("录像里没有帧");
        return data;
    }

    private static void ParseFrames(StreamReader reader, ReplayData data)
    {
        int seats = data.Seats.Count;
        int tokensPerFrame = 2 + seats * 5; // "f" + 帧号 + 每座位 5 个字段
        long lastFrame = 0;

        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var parts = line.Split(' ');
            if (parts[0] == "end") break;
            if (parts[0] != "f" || parts.Length != tokensPerFrame) break; // 半行/损坏：就此停止

            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long frame)) break;
            if (frame < lastFrame + 1) continue; // 重复/旧帧
            if (frame > lastFrame + 1) break;     // 缺帧：不能凭空补，停在这里

            var cmds = new PlayerCommand[seats];
            for (int i = 0; i < seats; i++)
            {
                int b = 2 + i * 5;
                if (!int.TryParse(parts[b], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seat) ||
                    !int.TryParse(parts[b + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mx) ||
                    !int.TryParse(parts[b + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mz) ||
                    !int.TryParse(parts[b + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int aim) ||
                    !byte.TryParse(parts[b + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte buttons))
                    return; // 半行：丢弃这一帧及之后

                if (seat < 0 || seat >= seats) return;
                cmds[seat] = new PlayerCommand { MoveX = mx, MoveZ = mz, AimAngle = aim, Buttons = buttons };
            }

            data.Frames.Add(cmds);
            lastFrame = frame;
        }
    }

    /// <summary>按录像里的种子/座位重建初始逻辑世界（与实时开局完全同一条构造路径）</summary>
    public LogicWorld CreateWorld()
    {
        var world = new LogicWorld(Seed);
        for (int i = 0; i < Seats.Count; i++)
        {
            int seat = Seats[i].Seat;
            LogicWorld.GetSpawnPoint(seat, out var x, out var z);
            world.AddPlayer(seat, x, z, 0, LockstepConfig.MAX_HP);
        }
        return world;
    }

    public Dictionary<int, PlayerCommand> GetInputs(int index)
    {
        var frame = Frames[index];
        var inputs = new Dictionary<int, PlayerCommand>(frame.Length);
        for (int seat = 0; seat < frame.Length; seat++)
            inputs[seat] = frame[seat];
        return inputs;
    }

    #region 解析辅助

    private delegate bool TryParseDelegate<T>(string s, NumberStyles style, IFormatProvider provider, out T value);

    private static T ReadHeaderValue<T>(StreamReader reader, string key, TryParseDelegate<T> parse)
    {
        var parts = reader.ReadLine()?.Split(' ');
        if (parts == null || parts.Length < 2 || parts[0] != key)
            throw new InvalidDataException($"缺少文件头字段 {key}");
        if (!parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"文件头字段 {key} 不是数字");
        return value;
    }

    private static string FromB64(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));

    #endregion
}
