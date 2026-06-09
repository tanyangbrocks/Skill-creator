namespace SkillCreator.AbilitySystem.Data;

public class TotemData
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public TotemType Type { get; init; }
    public int BaseAbilityPointCost { get; init; } = 10;
    public int RequiredPlayerLevel { get; init; } = 1;
}
