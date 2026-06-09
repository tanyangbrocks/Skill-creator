namespace SkillCreator.World.Items;

public struct ItemStack
{
    public static readonly ItemStack Empty = new(ItemId.None, 0);

    public ItemId ItemId { get; }
    public int    Count  { get; }
    public bool   IsEmpty => ItemId == ItemId.None || Count <= 0;

    public ItemStack(ItemId id, int count) { ItemId = id; Count = count; }

    public ItemStack WithCount(int newCount) => new(ItemId, newCount);
}
