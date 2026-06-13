namespace SkillCreator.GameFlow;

public sealed class CharacterSaveData
{
    public string Id    { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
    public string Name  { get; set; } = "旅者";
    public int    Level { get; set; } = 1;
    public float  Xp    { get; set; } = 0f;
    public float  Hp    { get; set; } = 100f;
    public float  Mp    { get; set; } = 100f;

    public SlotRecord[] InventorySlots { get; set; } = [];
    public int          ActiveHotbar   { get; set; } = 0;

    // W-6F：全部技能組資料（SaveSystem.SaveGroupToString 序列化）
    public string SpellGroupJson { get; set; } = "";

    // W-6B：各 MP 槽位的 Current 值（key = ManaTypeKey）
    public Dictionary<string, float> ManaCurrents { get; set; } = new();

    public sealed class SlotRecord
    {
        public string ItemId { get; set; } = "None";
        public int    Count  { get; set; } = 0;
    }
}
