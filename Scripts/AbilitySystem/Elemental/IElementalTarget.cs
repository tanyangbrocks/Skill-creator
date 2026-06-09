namespace SkillCreator.AbilitySystem.Elemental;

/// <summary>
/// 可被元素狀態效果作用的對象（敵人、玩家）。
/// </summary>
public interface IElementalTarget
{
    /// <summary>唯一識別碼；玩家固定為 -1。</summary>
    int EntityId { get; }

    /// <summary>直接造成傷害（如感電即時傷害），仍走正常傷害管線。</summary>
    void TakeDirectDamage(float amount);
}
