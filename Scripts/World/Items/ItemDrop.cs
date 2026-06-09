namespace SkillCreator.World.Items;

public struct ItemDrop
{
    public ItemId ItemId;
    public int    MinCount;
    public int    MaxCount;
    public float  Chance;    // 0–1（1.0 = 必掉）

    public ItemDrop(ItemId id, int min, int max, float chance = 1f)
    {
        ItemId = id; MinCount = min; MaxCount = max; Chance = chance;
    }
}
