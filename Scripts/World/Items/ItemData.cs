namespace SkillCreator.World.Items;

using SkillCreator.World.Materials;

public record ItemData(
    ItemId        Id,
    string        DisplayName,
    bool          IsPlaceable,
    MaterialType? PlaceAs,        // 放置時使用的材質（null = 不可放置）
    bool          IsTool,
    int           ToolTier,       // 此工具解鎖 RequiredToolTier ≤ ToolTier 的採掘
    float         MiningSpeedMult, // 採掘速度倍率（1.0 = 徒手基準）
    int           MaxStack,       // 堆疊上限（工具類為 1）
    // ── 裝備屬性（預設 = 非裝備物品）──────────────────────────────
    EquipmentSlotType EquipSlot = EquipmentSlotType.None,
    float             AtkMult  = 1f,  // 武器：攻擊倍率
    float             DefFlat  = 0f,  // 防具：固定傷害減免
    float             MpBonus  = 0f   // 飾品：MP 上限加成
);
