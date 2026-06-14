namespace SkillCreator.AbilitySystem.Data;

// 玩家的技能欄位（主動最多 MaxSlots 個，被動最多 MaxPassiveSlots 個）
public class SpellLoadout
{
    public const int MaxSlots        = 10;
    public const int MaxPassiveSlots = 20;

    private readonly SpellArray?[]    _slots         = new SpellArray?[MaxSlots];
    private readonly List<SpellArray> _passiveSpells = new();

    public int ActiveIndex { get; set; } = 0;

    // ── 主動技能 ──────────────────────────────────────────────────

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

    // ── 被動技能 ──────────────────────────────────────────────────

    public IReadOnlyList<SpellArray> PassiveSpells => _passiveSpells;

    public int PassiveCount => _passiveSpells.Count;

    /// <summary>新增被動技能；若已達上限回傳 false。</summary>
    public bool AddPassive(SpellArray spell)
    {
        if (_passiveSpells.Count >= MaxPassiveSlots) return false;
        _passiveSpells.Add(spell);
        return true;
    }

    /// <summary>移除被動技能；回傳是否成功。</summary>
    public bool RemovePassive(SpellArray spell) => _passiveSpells.Remove(spell);

    /// <summary>清空所有主動槽位與被動列表（讀取新存檔前使用）。</summary>
    public void ClearAll()
    {
        for (int i = 0; i < MaxSlots; i++) _slots[i] = null;
        _passiveSpells.Clear();
        ActiveIndex = 0;
    }

    public SpellArray? GetPassive(int i) =>
        i >= 0 && i < _passiveSpells.Count ? _passiveSpells[i] : null;

    /// <summary>替換指定索引的被動技能（編輯後回寫）。</summary>
    public void ReplacePassive(int idx, SpellArray spell)
    {
        if ((uint)idx < (uint)_passiveSpells.Count)
            _passiveSpells[idx] = spell;
    }
}
