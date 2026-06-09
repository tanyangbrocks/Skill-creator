namespace SkillCreator.World.Materials;

using Godot;
using SkillCreator.AbilitySystem.Data;
using SkillCreator.World.Items;

public record MaterialData(
    MaterialType Type,
    string DisplayName,
    Color BaseColor,        // 渲染基礎顏色
    PhysicsCategory Physics,
    bool IsFlammable,
    float Density,          // 密度（越大越沉）
    int BurnDurationMin,    // 燃燒最少幀數（0 = 不可燃燒）
    int BurnDurationMax     // 燃燒最多幀數
)
{
    // ── 採掘屬性 ──────────────────────────────────────────────────
    public bool  IsMineable       { get; init; } = false; // 可否被採掘
    public int   Hardness         { get; init; } = 0;     // 基礎採掘幀數（0 = 不適用）
    public int   RequiredToolTier { get; init; } = 0;     // 0 = 徒手；1 = 基礎工具；以此類推

    // ── 預留抗性（暫不使用，供未來爆炸/魔法系統讀取）────────────
    public float BlastResistance  { get; init; } = 1.0f;  // 爆炸傷害係數（1 = 標準）
    public float MagicResistance  { get; init; } = 1.0f;  // 魔法傷害係數（1 = 標準）

    // ── 採掘掉落表 ─────────────────────────────────────────────
    public ItemDrop[] DefaultDrops { get; init; } = Array.Empty<ItemDrop>();

    // ── 元素屬性（W-3 元素碰撞系統）───────────────────────────
    /// <summary>
    /// 材質格天生帶有的元素屬性（永久，不被消耗）。
    /// None = 無元素性（如空氣、灰燼）。
    /// </summary>
    public ElementType NativeElement { get; init; } = ElementType.None;
}
