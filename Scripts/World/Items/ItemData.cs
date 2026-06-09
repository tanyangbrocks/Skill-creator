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
    int           MaxStack        // 堆疊上限（工具類為 1）
);
