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
}
