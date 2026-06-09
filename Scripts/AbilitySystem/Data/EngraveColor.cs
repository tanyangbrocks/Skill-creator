namespace SkillCreator.AbilitySystem.Data;

public enum EngraveColor
{
    Black,   // 邏輯刻印（積木程式）
    White,   // 傷害型
    Orange,  // 控制型
    Blue,    // 圖騰改造（開關/被動/多段/條件觸發/不可打斷）
    Red,     // 侵略效果（Debuff / 斷招）
    Green,   // 輔助效果（Buff / 護盾 / 召喚）
    Purple,  // 額外操作
    Yellow,  // 能力限制（限制換點）
}

public enum ScalingType
{
    Hyperbolic, // 傷害/概率類：f(x) = 1 - 1 / (1 + a*x)
    Linear,     // 輔助類：f(x) = base + x * k
}
