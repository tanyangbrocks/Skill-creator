namespace SkillCreator.AbilitySystem.Data;

// 技能整構中的單一插槽，容納一個技能因子及其附掛的刻印
public class SpellSlot
{
    // 積木序列中引用此插槽的名稱（空 = 自動命名為 "slot_N"）
    public string Name { get; set; } = "";

    public TotemData? Totem { get; set; }
    public List<EngraveData> LocalEngravings { get; } = new();

    // W-6B：此技能因子注入的 MP 類型（null = 未指定，免費執行或待設定）
    public string? ManaTypeKey { get; set; }

    public bool IsEmpty => Totem is null;

    // 此技能因子鏈是否包含需要消耗 MP 的積木。
    // 初期保守判斷：有圖騰即視為可能消耗 MP；W-6E 可依積木型別精確化。
    public bool HasAnyMpBlocks => !IsEmpty;

    public int AbilityPointCost
    {
        get
        {
            if (Totem is null) return 0;
            return Totem.BaseAbilityPointCost
                + LocalEngravings.Sum(e => e.TotalAbilityPointCost);
        }
    }
}
