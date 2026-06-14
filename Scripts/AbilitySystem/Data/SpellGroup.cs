namespace SkillCreator.AbilitySystem.Data;

/// <summary>
/// 玩家的技能組容器，最多 MaxGroups 個技能組（SpellLoadout），V 鍵切換。
/// 每個技能組獨立持有自己的主動/被動技能配置。
/// </summary>
public sealed class SpellGroup
{
    public const int MaxGroups = 5;

    private readonly SpellLoadout[] _groups;

    public SpellGroup()
    {
        _groups = new SpellLoadout[MaxGroups];
        for (int i = 0; i < MaxGroups; i++)
            _groups[i] = new SpellLoadout();
    }

    public int ActiveGroupIndex { get; set; } = 0;

    public SpellLoadout ActiveLoadout => _groups[ActiveGroupIndex];

    public SpellLoadout GetGroup(int index)
    {
        if (index < 0 || index >= MaxGroups)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _groups[index];
    }

    public void SetActiveGroup(int index)
    {
        if (index >= 0 && index < MaxGroups)
            ActiveGroupIndex = index;
    }
}
