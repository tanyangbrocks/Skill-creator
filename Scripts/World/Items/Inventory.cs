namespace SkillCreator.World.Items;

public class Inventory
{
    public const int HotbarSize = 5;
    public const int TotalSize  = 30;   // 5 熱鍵 + 25 主欄

    public ItemStack[] Slots { get; } = new ItemStack[TotalSize];

    public int ActiveHotbarIndex
    {
        get => _activeHotbar;
        set => _activeHotbar = Math.Clamp(value, 0, HotbarSize - 1);
    }
    private int _activeHotbar = 0;

    public ItemStack ActiveItem => Slots[ActiveHotbarIndex];

    // 嘗試加入物品，回傳實際加入的數量（超出空間則部分加入）
    public int TryAdd(ItemId id, int count)
    {
        if (id == ItemId.None || count <= 0) return 0;

        int maxStack  = ItemRegistry.Get(id).MaxStack;
        int remaining = count;

        // 第一輪：優先填補已有同種物品的格子
        for (int i = 0; i < TotalSize && remaining > 0; i++)
        {
            if (Slots[i].ItemId != id) continue;
            int canAdd = maxStack - Slots[i].Count;
            if (canAdd <= 0) continue;
            int add = Math.Min(canAdd, remaining);
            Slots[i] = Slots[i].WithCount(Slots[i].Count + add);
            remaining -= add;
        }

        // 第二輪：填入空格
        for (int i = 0; i < TotalSize && remaining > 0; i++)
        {
            if (!Slots[i].IsEmpty) continue;
            int add = Math.Min(maxStack, remaining);
            Slots[i] = new ItemStack(id, add);
            remaining -= add;
        }

        return count - remaining;
    }

    // 消費指定格的物品（放置/使用時呼叫）
    public bool Consume(int slotIndex, int count = 1)
    {
        if (slotIndex < 0 || slotIndex >= TotalSize) return false;
        var slot = Slots[slotIndex];
        if (slot.IsEmpty || slot.Count < count) return false;
        Slots[slotIndex] = slot.Count == count
            ? ItemStack.Empty
            : slot.WithCount(slot.Count - count);
        return true;
    }

    // 取得熱鍵欄有效工具等級（徒手 = 0）
    public int ActiveToolTier
    {
        get
        {
            var item = ActiveItem;
            if (item.IsEmpty) return 0;
            var data = ItemRegistry.Get(item.ItemId);
            return data.IsTool ? data.ToolTier : 0;
        }
    }

    // 交換兩格物品（拖曳移動用）
    public void SwapSlots(int a, int b)
    {
        if (a < 0 || a >= TotalSize || b < 0 || b >= TotalSize || a == b) return;
        (Slots[a], Slots[b]) = (Slots[b], Slots[a]);
    }

    // 取得熱鍵欄採掘速度倍率
    public float ActiveMiningSpeedMult
    {
        get
        {
            var item = ActiveItem;
            if (item.IsEmpty) return 1.0f;
            return ItemRegistry.Get(item.ItemId).MiningSpeedMult;
        }
    }
}
