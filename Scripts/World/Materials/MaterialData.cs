namespace SkillCreator.World.Materials;

using Godot;

public record MaterialData(
    MaterialType Type,
    string DisplayName,
    Color BaseColor,        // 渲染基礎顏色
    PhysicsCategory Physics,
    bool IsFlammable,
    float Density,          // 密度（越大越沉）
    int BurnDurationMin,    // 燃燒最少幀數（0 = 不可燃燒）
    int BurnDurationMax     // 燃燒最多幀數
);
