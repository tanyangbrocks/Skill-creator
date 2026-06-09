namespace SkillCreator.World.Materials;

public enum MaterialType : byte
{
    Air,    // 空氣
    Stone,  // 石（靜態）
    Dirt,   // 土（靜態，可挖）
    Wood,   // 木（靜態，可燃）
    Sand,   // 沙（重力，堆積）
    Water,  // 水（液體，快速流動）
    Lava,   // 岩漿（液體，緩慢，高溫）
    Fire,   // 火（氣體，上升，消耗）
    Steam,  // 蒸汽（氣體，上升，消散）
    Ash,    // 灰燼（靜態殘留）
}

public enum PhysicsCategory
{
    Empty,   // Air
    Static,  // Stone, Dirt, Wood, Ash
    Powder,  // Sand
    Liquid,  // Water, Lava
    Gas,     // Fire, Steam
}
