namespace SkillCreator.GameFlow;

public sealed class CharacterSaveData
{
    public string Id    { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
    public string Name  { get; set; } = "旅者";
    public int    Level { get; set; } = 1;
    public float  Xp    { get; set; } = 0f;
    public float  Hp    { get; set; } = 100f;
    public int    Ap    { get; set; } = 0;
}
