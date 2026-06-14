namespace SkillCreator.World.Items;

public enum ItemId
{
    None = 0,

    // ── 方塊物品（可放置）─────────────────────────────────────────
    BlockDirt,
    BlockStone,    // 圓石（Stone 採掘後掉落，可放置回 Stone 材質）
    BlockWood,
    BlockSand,
    BlockAsh,

    // ── 工具 ──────────────────────────────────────────────────────
    ToolBasicPick,  // 基礎鎬：解鎖 RequiredToolTier=1，採掘石類加速
    ToolBasicAxe,   // 基礎斧：加速採掘木類
    ToolIronPick,   // 鐵鎬：解鎖 RequiredToolTier=2，採掘魔晶礦

    // ── 裝備 ──────────────────────────────────────────────────
    EquipBasicSword,   // 武器：基礎劍，攻擊 ×1.3
    EquipLeatherArmor, // 防具：皮革護甲，固定減免 5 傷害
    EquipAmulet,       // 飾品：護符，+30 MP 上限

    // ── 礦石原材料（W-4）─────────────────────────────────────
    OreCoal,         // 煤炭（煤礦掉落）
    OreCopperRaw,    // 生銅礦（銅礦掉落）
    OreIronRaw,      // 生鐵礦（鐵礦掉落）
    OreMagicCrystal, // 魔晶石（魔晶礦掉落）

    // ── 材質碎片（R-5，不可放置，合成原料）────────────────────
    FragmentDirt,
    FragmentStone,
    FragmentSand,
    FragmentWood,
    FragmentAsh,
    FragmentCoal,
    FragmentCopper,
    FragmentIron,
    FragmentMagicCrystal,
}
