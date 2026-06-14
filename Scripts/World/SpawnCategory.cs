namespace SkillCreator.World;

/// <summary>
/// 決定怪物的生成邏輯分類。
/// </summary>
public enum SpawnCategory
{
    Common,    // 通用野怪：在玩家附近動態生成與消除
    Area,      // 區域野怪：同 Common，但限制在特定區域內
    Specific,  // 特定怪（暫未定義）
    Boss,      // Boss（暫未定義）
}

/// <summary>
/// 生成表條目：描述一種可出現的怪物及其分類、機率、區域限制。
/// Weight 值越高出現機率越大（加權隨機抽選）。
/// Area 類型需設定 AreaCenter 與 AreaRadius（tile 為單位）。
/// </summary>
public record MobTableEntry(
    EnemyType     Type,
    SpawnCategory Category,
    float         Weight     = 1f,
    GridPos       AreaCenter = default,
    int           AreaRadius = 0
);
