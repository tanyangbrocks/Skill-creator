namespace SkillCreator.AbilitySystem.Data;

// 玩家的技能欄位（最多 MaxSlots 個法陣槽位）
public class SpellLoadout
{
    public const int MaxSlots = 10;

    private readonly SpellArray?[] _slots = new SpellArray?[MaxSlots];

    public int ActiveIndex { get; set; } = 0;

    public SpellArray? ActiveSpell =>
        ActiveIndex >= 0 && ActiveIndex < MaxSlots ? _slots[ActiveIndex] : null;

    public SpellArray? GetSlot(int i) =>
        i >= 0 && i < MaxSlots ? _slots[i] : null;

    public void SetSlot(int i, SpellArray? spell)
    {
        if (i >= 0 && i < MaxSlots) _slots[i] = spell;
    }

    public string SlotLabel(int i)
    {
        var s = GetSlot(i);
        return s is null ? "（空）" : string.IsNullOrEmpty(s.Name) ? "未命名" : s.Name;
    }
}
