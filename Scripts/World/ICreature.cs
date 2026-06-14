namespace SkillCreator.World;

using SkillCreator.AbilitySystem.Elemental;

/// <summary>玩家與敵人的共同生物介面。</summary>
public interface ICreature
{
    int    Id      { get; }
    GridPos Position { get; }
    float   Hp      { get; }
    float   MaxHp   { get; }
    bool    IsAlive { get; }
    ElementalAuraComponent Aura { get; }
}
