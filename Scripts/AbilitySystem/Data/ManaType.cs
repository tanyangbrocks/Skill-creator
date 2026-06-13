namespace SkillCreator.AbilitySystem.Data;

/// <summary>
/// 單一 MP 類型的不可變資料記錄。
/// 71 種潛在類型（18 基礎 + 53 複合），透過 ManaTypeRegistry 查詢。
/// IsComposite = true 的類型由 W-13 開放，W-6A 先定義基礎 18 種。
/// </summary>
public sealed record ManaType(
    int    Id,
    string Key,
    string DisplayName,
    string RootGroup,    // "修煉" / "支配" / "世界"
    bool   IsComposite,
    int    SortOrder     // HUD 縱向排列順序（基礎 1–18，複合 19+）
);
