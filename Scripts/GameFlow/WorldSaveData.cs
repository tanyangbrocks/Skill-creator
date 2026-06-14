namespace SkillCreator.GameFlow;

public sealed class WorldSaveData
{
    public string Id         { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
    public string Name       { get; set; } = "新世界";
    public int    Seed       { get; set; } = 0;
    public string LastPlayed { get; set; } = "";

    // G-5: chunk 持久化 + 出生點
    public string WorldDir     { get; set; } = "";    // OS 絕對路徑，CreateDirectory 後填入
    public int    SpawnX       { get; set; }
    public int    SpawnY       { get; set; }
    public int    SpawnZ       { get; set; }
    public bool   IsFirstEnter { get; set; } = true;  // false = 直接讀 SpawnX/Y/Z
}
