namespace SkillCreator.AbilitySystem;

// ─────────────────────────────────────────────────────────────────────────────
//  ActionBus  —  Phase 4 第三層：行動結算層
//
//  行動攔截匯流排，職責：
//    1. 讓過濾器（Filters）登記到管線
//    2. Dispatch 把動作送進管線，依優先序逐一過濾
//    3. 任一過濾器回傳 null → 動作取消；回傳不同型別 → 替代
//
//  設計準則（類似 EventBus，但有回傳值）：
//    • 過濾器預設 oneShot = true（觸發一次後自動移除），適合「下一次防護」語意
//    • 使用 tag 可以批次移除同一法陣登記的所有過濾器
//    • Main._Ready 呼叫 ClearAll() 確保跨局不殘留
// ─────────────────────────────────────────────────────────────────────────────
public static class ActionBus
{
    private sealed class FilterEntry
    {
        public required Func<GameAction, GameAction?> Filter { get; init; }
        public int     Priority { get; init; }
        public bool    OneShot  { get; init; }
        public string? Tag      { get; init; }
    }

    private static readonly List<FilterEntry> _filters = new();

    // ── 登記 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 登記一個動作過濾器。
    /// </summary>
    /// <param name="filter">過濾函式：回傳 null 取消動作；回傳 action 允許（可修改）</param>
    /// <param name="priority">優先序（高者先執行）</param>
    /// <param name="oneShot">true = 觸發一次後自動移除（預設）</param>
    /// <param name="tag">批次移除標記（可選）</param>
    public static void Register(
        Func<GameAction, GameAction?> filter,
        int priority = 0,
        bool oneShot = true,
        string? tag = null)
    {
        _filters.Add(new FilterEntry
        {
            Filter   = filter,
            Priority = priority,
            OneShot  = oneShot,
            Tag      = tag,
        });
        // 確保高優先序的過濾器排在前面（穩定排序，相同優先序維持登記順序）
        _filters.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <summary>移除所有符合 tag 的過濾器（用於主動清除某個法陣的所有防護）</summary>
    public static void UnregisterByTag(string tag)
        => _filters.RemoveAll(e => e.Tag == tag);

    /// <summary>清除所有過濾器（Main._Ready 呼叫）</summary>
    public static void ClearAll() => _filters.Clear();

    // ── 分派 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 將動作送入攔截管線。
    /// 回傳最終動作（可能與輸入不同）；null 表示動作已被取消。
    /// </summary>
    public static GameAction? Dispatch(GameAction action)
    {
        if (_filters.Count == 0) return action; // 快速路徑

        GameAction? current = action;
        var toRemove = new List<FilterEntry>();

        // 注意：過濾器 lambda 不得在執行期間呼叫 Register/UnregisterByTag，否則需恢復 .ToList() 防護
        // 已知限制：ActionBus._filters 不在 SnapshotManager 的快照範圍內；
        //           Rollback 後 oneShot 過濾器（DamageShield/DeathGuard）不會自動還原。
        for (int fi = 0; fi < _filters.Count; fi++)
        {
            var entry = _filters[fi];
            if (current == null) break; // 已取消，後續過濾器不再執行

            current = entry.Filter(current);

            if (entry.OneShot)
                toRemove.Add(entry);
        }

        foreach (var e in toRemove)
            _filters.Remove(e);

        return current;
    }

    // ── 查詢 ──────────────────────────────────────────────────────────

    /// <summary>目前登記中的過濾器數量（供 debug 使用）</summary>
    public static int Count => _filters.Count;
}
