namespace SkillCreator.World;

using SkillCreator.World.Items;

public sealed class PlayerEquipment
{
    public ItemId WeaponId    { get; private set; } = ItemId.None;
    public ItemId ArmorId     { get; private set; } = ItemId.None;
    public ItemId AccessoryId { get; private set; } = ItemId.None;

    public float TotalAtkMult => WeaponId    != ItemId.None ? ItemRegistry.Get(WeaponId).AtkMult    : 1f;
    public float TotalDefFlat => ArmorId     != ItemId.None ? ItemRegistry.Get(ArmorId).DefFlat     : 0f;
    public float TotalMpBonus => AccessoryId != ItemId.None ? ItemRegistry.Get(AccessoryId).MpBonus : 0f;

    // 從熱鍵欄槽位裝備：移除熱鍵欄物品，舊裝備退回背包
    public bool TryEquip(Inventory inv, int hotbarIndex)
    {
        var stack = inv.Slots[hotbarIndex];
        if (stack.IsEmpty) return false;
        var data = ItemRegistry.Get(stack.ItemId);
        if (data.EquipSlot == EquipmentSlotType.None) return false;

        ItemId old = data.EquipSlot switch
        {
            EquipmentSlotType.Weapon    => WeaponId,
            EquipmentSlotType.Armor     => ArmorId,
            EquipmentSlotType.Accessory => AccessoryId,
            _                          => ItemId.None,
        };

        inv.Consume(hotbarIndex, 1);
        if (old != ItemId.None) inv.TryAdd(old, 1);

        switch (data.EquipSlot)
        {
            case EquipmentSlotType.Weapon:    WeaponId    = stack.ItemId; break;
            case EquipmentSlotType.Armor:     ArmorId     = stack.ItemId; break;
            case EquipmentSlotType.Accessory: AccessoryId = stack.ItemId; break;
        }
        return true;
    }

    // 卸下裝備並退回背包
    public bool TryUnequip(Inventory inv, EquipmentSlotType slot)
    {
        ItemId equipped = slot switch
        {
            EquipmentSlotType.Weapon    => WeaponId,
            EquipmentSlotType.Armor     => ArmorId,
            EquipmentSlotType.Accessory => AccessoryId,
            _                          => ItemId.None,
        };
        if (equipped == ItemId.None) return false;

        inv.TryAdd(equipped, 1);
        switch (slot)
        {
            case EquipmentSlotType.Weapon:    WeaponId    = ItemId.None; break;
            case EquipmentSlotType.Armor:     ArmorId     = ItemId.None; break;
            case EquipmentSlotType.Accessory: AccessoryId = ItemId.None; break;
        }
        return true;
    }
}
