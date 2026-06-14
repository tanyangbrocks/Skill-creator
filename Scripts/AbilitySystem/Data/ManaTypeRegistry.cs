namespace SkillCreator.AbilitySystem.Data;

using System.Collections.Frozen;

/// <summary>
/// 所有 MP 類型的靜態登記表。
/// W-6A：18 種基礎類型（三大根源各 6）。
/// W-13：複合類型（53 種）直接呼叫 Register 追加，無需改動此類。
/// </summary>
public static class ManaTypeRegistry
{
    private static readonly Dictionary<string, ManaType> _all = new();

    public static IReadOnlyDictionary<string, ManaType> All => _all;

    static ManaTypeRegistry()
    {
        // ── 修煉六道 ──────────────────────────────────────────────
        Register(new(1,  "wu_dao",     "武道",  "修煉", false, 1));
        Register(new(2,  "xian_dao",   "仙道",  "修煉", false, 2));
        Register(new(3,  "fa_dao",     "法道",  "修煉", false, 3));
        Register(new(4,  "yi_dao",     "意道",  "修煉", false, 4));
        Register(new(5,  "hun_dao",    "魂道",  "修煉", false, 5));
        Register(new(6,  "gui_dao",    "詭道",  "修煉", false, 6));

        // ── 支配六法 ──────────────────────────────────────────────
        Register(new(7,  "mo_fa",      "魔法",  "支配", false, 7));
        Register(new(8,  "yao_li",     "妖力",  "支配", false, 8));
        Register(new(9,  "ao_shu",     "奧術",  "支配", false, 9));
        Register(new(10, "shen_sheng", "神聖力", "支配", false, 10));
        Register(new(11, "yuan_neng",  "源能",  "支配", false, 11));
        Register(new(12, "xing_neng",  "星能",  "支配", false, 12));

        // ── 世界六意 ──────────────────────────────────────────────
        Register(new(13, "ji_neng",    "技能",  "世界", false, 13));
        Register(new(14, "zhi_ye",     "職業",  "世界", false, 14));
        Register(new(15, "chao_neng",  "超能",  "世界", false, 15));
        Register(new(16, "shen_li",    "神力",  "世界", false, 16));
        Register(new(17, "gai_nian",   "概念",  "世界", false, 17));
        Register(new(18, "xun_neng",   "尋能",  "世界", false, 18));
    }

    public static ManaType? Get(string key) =>
        _all.TryGetValue(key, out var t) ? t : null;

    /// <summary>依 SortOrder 排序，供 HUD 顯示。</summary>
    public static IEnumerable<ManaType> GetSortedForHud() =>
        _all.Values.OrderBy(t => t.SortOrder);

    /// <summary>W-13 追加複合類型時使用。</summary>
    public static void Register(ManaType type) => _all[type.Key] = type;

    public static bool AreSameRoot(string keyA, string keyB) =>
        _all.TryGetValue(keyA, out var a) &&
        _all.TryGetValue(keyB, out var b) &&
        a.RootGroup == b.RootGroup;
}
