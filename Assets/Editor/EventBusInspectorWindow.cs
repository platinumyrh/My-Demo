using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EventBus 静态扫描器
/// 扫描 Assets/Code 下所有 .cs 文件中的 EventBus Subscribe/Unsubscribe/Publish 调用点，
/// 生成「事件 → 发布者/订阅者」矩阵，用于回答：
/// 1. 谁发布了某事件（文件:行号，可点击跳转）
/// 2. 谁订阅了某事件（类名.方法名 + 位置）
/// 3. 哪些事件从未使用 / 只发没人收 / 只订没人发（死事件）
/// 4. 哪些事件订阅数和退订数不一致（疑似订阅泄漏，也可能是有意常驻）
///
/// 纯静态文本分析，不依赖 Play Mode；业务代码零侵入
/// </summary>
public class EventBusInspectorWindow : EditorWindow
{
    #region 数据结构

    private class CallSite
    {
        public string File;      // 相对路径（Assets/Code/...）
        public int Line;         // 1-based 行号
        public string ClassName; // 所属类名
        public string Detail;    // 订阅/退订=处理方法名；发布=payload类型或"-"
    }

    private class EventInfo
    {
        public string Name;
        public List<CallSite> Publishers = new List<CallSite>();
        public List<CallSite> Subscribers = new List<CallSite>();
        public List<CallSite> Unsubscribers = new List<CallSite>();

        public bool NeverUsed => Publishers.Count == 0 && Subscribers.Count == 0;
        public bool PublishNoSub => Publishers.Count > 0 && Subscribers.Count == 0;
        public bool SubNoPublish => Subscribers.Count > 0 && Publishers.Count == 0;
        public bool SuspectLeak => Subscribers.Count > 0 && Subscribers.Count != Unsubscribers.Count;
    }

    #endregion

    #region 窗口状态

    private const string CodeFolder = "Assets/Code";
    private const string EnumFile = "Assets/Code/Base/EventBus/GameEventType.cs";

    private readonly Dictionary<string, EventInfo> _events = new Dictionary<string, EventInfo>();
    private Vector2 _scroll;
    private string _filter = "";
    private bool _showNormal = true;
    private bool _showDead = true;
    private bool _showLeak = true;
    private bool _scanned = false;

    [MenuItem("Tools/Event Bus Inspector")]
    private static void Open()
    {
        var window = GetWindow<EventBusInspectorWindow>("EventBus");
        window.minSize = new Vector2(560f, 400f);
    }

    private void OnEnable()
    {
        if (!_scanned) Scan();
    }

    #endregion

    #region 扫描

    private void Scan()
    {
        _events.Clear();

        // 1. 从 GameEventType.cs 枚举全部事件名（保证"从未使用"的事件也出现在矩阵里）
        foreach (var name in ParseEnumMembers(EnumFile))
        {
            _events[name] = new EventInfo { Name = name };
        }

        // 2. 扫描代码目录
        string codeRoot = Path.GetFullPath(CodeFolder);
        if (!Directory.Exists(codeRoot))
        {
            Debug.LogError($"[EventBusInspector] 找不到代码目录: {CodeFolder}");
            return;
        }

        var subscribeRe = new Regex(@"\.Subscribe<\s*[\w.]+\s*>\s*\(\s*GameEventType\.(\w+)\s*,\s*(\w+)", RegexOptions.Singleline | RegexOptions.Compiled);
        var unsubscribeRe = new Regex(@"\.Unsubscribe<\s*[\w.]+\s*>\s*\(\s*GameEventType\.(\w+)\s*,\s*(\w+)", RegexOptions.Singleline | RegexOptions.Compiled);
        var publishRe = new Regex(@"\.Publish(?:<\s*[\w.]+\s*>)?\s*\(\s*GameEventType\.(\w+)", RegexOptions.Singleline | RegexOptions.Compiled);

        foreach (var fullPath in Directory.GetFiles(codeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(fullPath); }
            catch { continue; }

            string relPath = fullPath.Replace('\\', '/');
            int assetsIdx = relPath.IndexOf("Assets/", System.StringComparison.OrdinalIgnoreCase);
            relPath = assetsIdx >= 0 ? relPath.Substring(assetsIdx) : relPath;

            var classPositions = FindClassPositions(text);

            CollectCalls(text, subscribeRe, relPath, classPositions, detailFromMatch: m => m.Groups[2].Value,
                (eventName, site) => _events[eventName].Subscribers.Add(site));
            CollectCalls(text, unsubscribeRe, relPath, classPositions, detailFromMatch: m => m.Groups[2].Value,
                (eventName, site) => _events[eventName].Unsubscribers.Add(site));
            CollectCalls(text, publishRe, relPath, classPositions, detailFromMatch: m => "-",
                (eventName, site) => _events[eventName].Publishers.Add(site));
        }

        _scanned = true;
    }

    private void CollectCalls(
        string text,
        Regex regex,
        string relPath,
        List<(int index, string name)> classPositions,
        System.Func<Match, string> detailFromMatch,
        System.Action<string, CallSite> add)
    {
        foreach (Match m in regex.Matches(text))
        {
            string eventName = m.Groups[1].Value;
            if (!_events.ContainsKey(eventName))
            {
                _events[eventName] = new EventInfo { Name = eventName };
            }

            var site = new CallSite
            {
                File = relPath,
                Line = CountNewlinesBefore(text, m.Index) + 1,
                ClassName = ContainingClass(classPositions, m.Index, relPath),
                Detail = detailFromMatch(m)
            };

            add(eventName, site);
        }
    }

    #endregion

    #region 解析辅助

    /// <summary>
    /// 解析 GameEventType.cs 枚举的所有成员名
    /// </summary>
    private static IEnumerable<string> ParseEnumMembers(string relativePath)
    {
        string fullPath = Path.GetFullPath(relativePath);
        if (!File.Exists(fullPath)) yield break;

        string text = File.ReadAllText(fullPath);
        int start = text.IndexOf("enum GameEventType", System.StringComparison.Ordinal);
        if (start < 0) yield break;

        int open = text.IndexOf('{', start);
        int close = text.IndexOf('}', open);
        if (open < 0 || close < 0) yield break;

        string body = text.Substring(open + 1, close - open - 1);
        var memberRe = new Regex(@"^\s*(\w+)\s*,?\s*(?://.*)?$", RegexOptions.Multiline | RegexOptions.Compiled);

        foreach (Match m in memberRe.Matches(body))
        {
            string name = m.Groups[1].Value;
            // 排除 #region 等（\w 不会匹配 # 开头，双保险再过滤关键字）
            if (name == "region" || name == "endregion") continue;
            yield return name;
        }
    }

    /// <summary>
    /// 记录文件中所有 class 声明的位置（用于定位调用点所属类）
    /// </summary>
    private static List<(int index, string name)> FindClassPositions(string text)
    {
        var result = new List<(int index, string name)>();
        var re = new Regex(@"class\s+(\w+)", RegexOptions.Compiled);
        foreach (Match m in re.Matches(text))
        {
            result.Add((m.Index, m.Groups[1].Value));
        }
        return result;
    }

    private static string ContainingClass(List<(int index, string name)> classPositions, int position, string relPath)
    {
        string result = Path.GetFileNameWithoutExtension(relPath);
        foreach (var (index, name) in classPositions)
        {
            if (index <= position) result = name;
            else break;
        }
        return result;
    }

    private static int CountNewlinesBefore(string text, int index)
    {
        int count = 0;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n') count++;
        }
        return count;
    }

    #endregion

    #region 状态与颜色

    private enum Status { Normal, NeverUsed, PublishNoSub, SubNoPublish, SuspectLeak }

    private static Status GetStatus(EventInfo info)
    {
        if (info.NeverUsed) return Status.NeverUsed;
        if (info.PublishNoSub) return Status.PublishNoSub;
        if (info.SubNoPublish) return Status.SubNoPublish;
        if (info.SuspectLeak) return Status.SuspectLeak;
        return Status.Normal;
    }

    private static string GetStatusLabel(EventInfo info)
    {
        switch (GetStatus(info))
        {
            case Status.NeverUsed: return "从未使用";
            case Status.PublishNoSub: return "发了没人收";
            case Status.SubNoPublish: return "订了没人发";
            case Status.SuspectLeak: return "订阅/退订数不一致";
            default: return "";
        }
    }

    private static Color GetStatusColor(EventInfo info)
    {
        switch (GetStatus(info))
        {
            case Status.NeverUsed: return new Color(0.55f, 0.55f, 0.55f);
            case Status.PublishNoSub:
            case Status.SubNoPublish: return new Color(0.95f, 0.55f, 0.15f);
            case Status.SuspectLeak: return new Color(0.95f, 0.85f, 0.2f);
            default: return new Color(0.5f, 0.9f, 0.5f);
        }
    }

    #endregion

    #region UI

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("重新扫描", GUILayout.Width(90)))
        {
            Scan();
        }

        EditorGUILayout.LabelField("过滤:", GUILayout.Width(38));
        string newFilter = EditorGUILayout.TextField(_filter);
        if (newFilter != _filter) { _filter = newFilter; }

        _showNormal = GUILayout.Toggle(_showNormal, "正常", GUILayout.Width(56));
        _showLeak = GUILayout.Toggle(_showLeak, "疑似泄漏", GUILayout.Width(76));
        _showDead = GUILayout.Toggle(_showDead, "死事件", GUILayout.Width(64));
        EditorGUILayout.EndHorizontal();

        if (!_scanned)
        {
            EditorGUILayout.HelpBox("点击「重新扫描」开始分析", MessageType.Info);
            return;
        }

        // 统计摘要
        int normal = 0, dead = 0, leak = 0;
        foreach (var info in _events.Values)
        {
            var s = GetStatus(info);
            if (s == Status.Normal) normal++;
            else if (s == Status.SuspectLeak) leak++;
            else dead++;
        }

        EditorGUILayout.HelpBox(
            $"事件总数: {_events.Count}    正常: {normal}    死事件/孤儿: {dead}    疑似泄漏: {leak}\n" +
            "点击任意条目跳转到对应代码行", MessageType.None);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        foreach (var info in _events.Values.OrderBy(e => e.Name))
        {
            var status = GetStatus(info);
            if (status == Status.Normal && !_showNormal) continue;
            if (status == Status.SuspectLeak && !_showLeak) continue;
            if (status != Status.Normal && status != Status.SuspectLeak && !_showDead) continue;

            if (!string.IsNullOrEmpty(_filter) &&
                info.Name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            DrawEventRow(info);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawEventRow(EventInfo info)
    {
        var statusColor = GetStatusColor(info);
        var statusLabel = GetStatusLabel(info);

        string header = $"{info.Name}   发布 {info.Publishers.Count} | 订阅 {info.Subscribers.Count} | 退订 {info.Unsubscribers.Count}";
        if (!string.IsNullOrEmpty(statusLabel))
        {
            header += $"   [{statusLabel}]";
        }

        var headerStyle = new GUIStyle(EditorStyles.foldout) { normal = { textColor = statusColor }, fontStyle = FontStyle.Bold };

        bool expand = EditorGUILayout.Foldout(SessionState.GetBool($"ebi_{info.Name}", false), header, true, headerStyle);
        SessionState.SetBool($"ebi_{info.Name}", expand);

        if (!expand) return;

        EditorGUI.indentLevel += 2;

        DrawSiteGroup("发布者:", info.Publishers);
        DrawSiteGroup("订阅者:", info.Subscribers);
        DrawSiteGroup("退订位置:", info.Unsubscribers);

        EditorGUI.indentLevel -= 2;
        EditorGUILayout.Space(4);
    }

    private void DrawSiteGroup(string label, List<CallSite> sites)
    {
        if (sites.Count == 0) return;

        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
        EditorGUI.indentLevel++;

        foreach (var site in sites.GroupBy(s => $"{s.File}:{s.Line}:{s.Detail}").Select(g => g.First()))
        {
            string row = $"{site.ClassName}.{site.Detail}   ({site.File}:{site.Line})";
            var rect = EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(row, EditorStyles.linkLabel))
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(site.File);
                if (asset != null) AssetDatabase.OpenAsset(asset, site.Line);
                else Debug.LogWarning($"[EventBusInspector] 无法打开: {site.File}");
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUI.indentLevel--;
    }

    #endregion
}
