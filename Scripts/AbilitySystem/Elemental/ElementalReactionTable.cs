namespace SkillCreator.AbilitySystem.Elemental;

using SkillCreator.AbilitySystem.Data;

/// <summary>
/// 元素碰撞反應查詢表（雙元素組合，A+B 與 B+A 等價）。
/// 22 條基礎反應全數定義；目前含元素狀態效果的有 5 條，其餘留 W-3b 填入 CA 效果。
/// </summary>
public static class ElementalReactionTable
{
    public record ReactionDef(
        string Name,
        Func<ElementalStatusEffect>? MakeStatusEffect = null
        // W-3b 補入：Func<CaEffect>? MakeCaEffect
    );

    private static readonly Dictionary<(ElementType, ElementType), ReactionDef> _table = new()
    {
        // ── 水系反應 ──────────────────────────────────────────────────────
        [K(ElementType.Water, ElementType.Metal)]    = new("鏽化",
            () => new RustEffect          { RemainingDuration = RustEffect.DefaultDuration }),
        [K(ElementType.Water, ElementType.Wood)]     = new("蔓生",
            () => new GrowthSlowEffect    { RemainingDuration = GrowthSlowEffect.DefaultDuration }),
        [K(ElementType.Water, ElementType.Earth)]    = new("流沙",
            () => new QuicksandSlowEffect { RemainingDuration = QuicksandSlowEffect.DefaultDuration }),
        [K(ElementType.Water, ElementType.Thunder)]  = new("感電",
            () => new ElectrocutionEffect { RemainingDuration = ElectrocutionEffect.DefaultDuration }),
        [K(ElementType.Water, ElementType.Ice)]      = new("結凍",
            () => new FrozenEffect        { RemainingDuration = FrozenEffect.DefaultDuration }),
        [K(ElementType.Water, ElementType.Wind)]     = new("濃霧",  null),  // 無元素狀態效果

        // ── 火系反應（W-3b CA 效果待填）─────────────────────────────────
        [K(ElementType.Fire, ElementType.Metal)]     = new("熔爐",  null),
        [K(ElementType.Fire, ElementType.Wood)]      = new("燃燒",  null),
        [K(ElementType.Fire, ElementType.Water)]     = new("沸騰",  null),
        [K(ElementType.Fire, ElementType.Ice)]       = new("融化",  null),
        [K(ElementType.Fire, ElementType.Wind)]      = new("擴散",  null),
        [K(ElementType.Fire, ElementType.Thunder)]   = new("雷爆",  null),

        // ── 土系反應 ──────────────────────────────────────────────────────
        [K(ElementType.Earth, ElementType.Metal)]    = new("強磁",  null),
        [K(ElementType.Earth, ElementType.Wood)]     = new("紮根",  null),
        [K(ElementType.Earth, ElementType.Wind)]     = new("沙塵",  null),

        // ── 雷系反應 ──────────────────────────────────────────────────────
        [K(ElementType.Thunder, ElementType.Metal)]  = new("超載",  null),
        [K(ElementType.Ice,     ElementType.Thunder)]= new("超導",  null),

        // ── 光 / 暗 / 毒系反應 ───────────────────────────────────────────
        [K(ElementType.Light, ElementType.Metal)]    = new("幻象",  null),
        [K(ElementType.Light, ElementType.Thunder)]  = new("閃耀",  null),
        [K(ElementType.Dark,  ElementType.Light)]    = new("調和",  null),
        [K(ElementType.Poison,ElementType.Wood)]     = new("腐化",  null),
        [K(ElementType.Poison,ElementType.Dark)]     = new("凋零",  null),
    };

    /// <summary>查詢兩元素的反應定義；A+B 與 B+A 結果相同。未定義則回傳 null。</summary>
    public static ReactionDef? Lookup(ElementType a, ElementType b)
        => _table.TryGetValue(K(a, b), out var def) ? def : null;

    // 正規化為 (小, 大) 確保對稱
    private static (ElementType, ElementType) K(ElementType a, ElementType b)
        => a <= b ? (a, b) : (b, a);
}
