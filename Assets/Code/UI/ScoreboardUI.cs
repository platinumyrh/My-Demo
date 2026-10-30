using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 局内计分面板：只显示逻辑世界里的 Kills/Deaths，不在 UI 里累加。
/// 由 LockstepGameDriver 开赛时 Build、每逻辑帧 SetEntry（值没变不会重写 Text）。
/// 场景里现成的 Panel_PlayerScoreElement 当模板；多出来的测试元素会被隐藏。
/// </summary>
public class ScoreboardUI : MonoBehaviour
{
    [Tooltip("条目挂载点，留空就用本节点")]
    [SerializeField] private RectTransform entryRoot;

    [Tooltip("条目模板（可留空：自动取子物体里的 Panel_PlayerScoreElement）")]
    [SerializeField] private GameObject entryTemplate;

    private class Entry
    {
        public GameObject Root;
        public Text Name;
        public Text Score;
        public int LastKills = int.MinValue;
        public int LastDeaths = int.MinValue;
    }

    private readonly Dictionary<int, Entry> _entries = new();
    private readonly List<GameObject> _spawned = new();

    /// <summary>按座位表生成条目（座位 → 显示名），顺序与座位号一致</summary>
    public void Build(IReadOnlyDictionary<int, string> seatNames, int localSeat)
    {
        Clear();

        GameObject template = ResolveTemplate();
        if (template == null)
        {
            Debug.LogError("[ScoreboardUI] 找不到条目模板（Panel_PlayerScoreElement）");
            return;
        }

        RectTransform root = entryRoot != null ? entryRoot : transform as RectTransform;

        var seats = new List<int>(seatNames.Keys);
        seats.Sort();

        foreach (int seat in seats)
        {
            GameObject go = Instantiate(template, root);
            go.name = "ScoreEntry_" + seat;
            go.SetActive(true);
            _spawned.Add(go);

            var entry = new Entry
            {
                Root = go,
                Name = FindText(go.transform, "name"),
                Score = FindText(go.transform, "score"),
            };
            if (entry.Name != null) entry.Name.text = seatNames[seat];
            if (entry.Score != null) entry.Score.text = "0 / 0";

            _entries[seat] = entry;
        }
    }

    /// <summary>每逻辑帧推一次；值没变化直接返回，避免无意义的 UGUI 重建</summary>
    public void SetEntry(int seat, int kills, int deaths)
    {
        if (!_entries.TryGetValue(seat, out var entry)) return;
        if (entry.LastKills == kills && entry.LastDeaths == deaths) return;

        entry.LastKills = kills;
        entry.LastDeaths = deaths;
        if (entry.Score != null) entry.Score.text = kills + " / " + deaths;
    }

    /// <summary>对局中玩家退出：只隐藏该行，逻辑数据和 Kills/Deaths 都保留</summary>
    public void SetEntryVisible(int seat, bool visible)
    {
        if (!_entries.TryGetValue(seat, out var entry) || entry.Root == null) return;
        if (entry.Root.activeSelf != visible) entry.Root.SetActive(visible);
    }

    public void Clear()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null) Destroy(_spawned[i]);
        }
        _spawned.Clear();
        _entries.Clear();
    }

    /// <summary>
    /// 优先用 Inspector 指定的模板；否则拿第一个子物体当模板，
    /// 顺带把场景里多摆的测试元素全部隐藏
    /// </summary>
    private GameObject ResolveTemplate()
    {
        if (entryTemplate != null) return entryTemplate;

        GameObject template = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (!child.name.StartsWith("Panel_PlayerScoreElement")) continue;

            if (template == null) template = child;
            child.SetActive(false);
        }
        return template;
    }

    /// <summary>按名字关键字找 Text（文本节点叫 "Text _PlayerName" / "Text _PlayerScore"）</summary>
    private static Text FindText(Transform root, string keyword)
    {
        var texts = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            string n = texts[i].name.Replace(" ", "").ToLowerInvariant();
            if (n.Contains(keyword)) return texts[i];
        }
        return null;
    }
}
