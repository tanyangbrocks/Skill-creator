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

        // ── 工具（Phase F 補完實際效果）──────────────────────────
        Reg(new ItemData(ItemId.ToolBasicPick, "基礎鎬", false, null, true, 1, 2.5f, 1));
        Reg(new ItemData(ItemId.ToolBasicAxe,  "基礎斧", false, null, true, 0, 2.0f, 1));
    }

    private static void Reg(ItemData d) => _data[(int)d.Id] = d;

    public static ItemData Get(ItemId id) => _data[(int)id];
}
