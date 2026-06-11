namespace SkillCreator.GameFlow;

public sealed class WorldSaveData
{
    public string Id         { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
    public string Name       { get; set; } = "新世界";
    public int    Seed       { get; set; } = 0;
    public string LastPlayed { get; set; } = "";
}
