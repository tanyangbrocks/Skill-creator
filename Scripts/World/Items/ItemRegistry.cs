namespace SkillCreator.World.Items;

using SkillCreator.World.Materials;

public static class ItemRegistry
{
    private static readonly ItemData[] _data;

    static ItemRegistry()
    {
        int count = Enum.GetValues<ItemId>().Length;
        _data = new ItemData[count];

        // None 佔位
        Reg(new ItemData(ItemId.None, "", false, null, false, 0, 1.0f, 0));

        // ── 方塊物品 ──────────────────────────────────────────────
        //        Id               名稱   可放  放成        工具  Tier  速度   疊
        Reg(new ItemData(ItemId.BlockDirt,  "泥土", true, MaterialType.Dirt,  false, 0, 1.0f, 99));
        Reg(new ItemData(ItemId.BlockStone, "圓石", true, MaterialType.Stone, false, 0, 1.0f, 99));
        Reg(new ItemData(ItemId.BlockWood,  "木材", true, MaterialType.Wood,  false, 0, 1.0f, 99));
        Reg(new ItemData(ItemId.BlockSand,  "沙",   true, MaterialType.Sand,  false, 0, 1.0f, 99));
        Reg(new ItemData(ItemId.BlockAsh,   "灰燼", true, MaterialType.Ash,   false, 0, 1.0f, 99));

        // ── 工具 ──────────────────────────────────────────────────
        Reg(new ItemData(ItemId.ToolBasicPick, "基礎鎬", false, null, true, 1, 2.5f, 1));
        Reg(new ItemData(ItemId.ToolBasicAxe,  "基礎斧", false, null, true, 0, 2.0f, 1));
        Reg(new ItemData(ItemId.ToolIronPick,  "鐵鎬",   false, null, true, 2, 3.5f, 1));

        // ── 裝備 ──────────────────────────────────────────────
        //                         名稱        放 放為  工 Tier  速   疊  裝備槽                      AtkMult DefFlat MpBonus
        Reg(new ItemData(ItemId.EquipBasicSword,   "基礎劍", false, null, false, 0, 1f, 1,
            EquipmentSlotType.Weapon, 1.3f, 0f, 0f));
        Reg(new ItemData(ItemId.EquipLeatherArmor, "皮革護甲", false, null, false, 0, 1f, 1,
            EquipmentSlotType.Armor, 1f, 5f, 0f));
        Reg(new ItemData(ItemId.EquipAmulet,       "護符", false, null, false, 0, 1f, 1,
            EquipmentSlotType.Accessory, 1f, 0f, 30f));

        // ── 礦石原材料（W-4）─────────────────────────────────────────
        //        Id                  名稱       可放   放成                              工具   Tier  速  疊
        Reg(new ItemData(ItemId.OreCoal,         "煤炭",   true, MaterialType.CoalOre,         false, 0, 1f, 99));
        Reg(new ItemData(ItemId.OreCopperRaw,    "生銅礦", true, MaterialType.CopperOre,       false, 0, 1f, 99));
        Reg(new ItemData(ItemId.OreIronRaw,      "生鐵礦", true, MaterialType.IronOre,         false, 0, 1f, 99));
        Reg(new ItemData(ItemId.OreMagicCrystal, "魔晶石", true, MaterialType.MagicCrystalOre, false, 0, 1f, 99));
    }

    private static void Reg(ItemData d) => _data[(int)d.Id] = d;

    public static ItemData Get(ItemId id) => _data[(int)id];
}
