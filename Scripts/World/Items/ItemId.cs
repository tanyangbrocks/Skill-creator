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

    // ── 工具（Phase F 補完功能，先佔位）──────────────────────────
    ToolBasicPick,  // 基礎鎬：解鎖 RequiredToolTier=1，採掘石類加速
    ToolBasicAxe,   // 基礎斧：加速採掘木類

    // ── 裝備 ──────────────────────────────────────────────────
    EquipBasicSword,   // 武器：基礎劍，攻擊 ×1.3
    EquipLeatherArmor, // 防具：皮革護甲，固定減免 5 傷害
    EquipAmulet,       // 飾品：護符，+30 MP 上限
}
