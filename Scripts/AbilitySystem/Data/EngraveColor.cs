namespace SkillCreator.AbilitySystem.Data;

public enum EngraveColor
{
    Action,    // 動作刻印（技能因子基礎行為，自動插入）
    Black,     // 邏輯刻印（積木程式）
    White,     // 傷害型
    Orange,    // 控制型（擊退/減速/凍結/牽引）
    Blue,      // 技能因子改造（多段/被動/不可打斷/軌跡）
    Red,       // 侵略效果（暈眩/瞬殺/斷招）
    Green,     // 輔助效果（護盾/治療/替代效果）
    Purple,    // 額外操作（選擇型/節奏輸入）
    Yellow,    // 能力限制（限制換點）
    Elemental, // 屬性刻印（金木水火土冰風光暗雷毒，11 種）
    Law,       // 法則刻印（時間/空間/造化等，14 種，高等級）
}

// 刻印類別：Modifier 修改參數；Action 觸發實際行為
public enum EngraveCategory { Modifier, Action }

// Action 刻印的觸發時機
public enum EngraveTrigger  { OnCast, OnHit, OnTick, OnExpire }

public enum ScalingType
{
    Hyperbolic, // 傷害/概率類：f(x) = 1 - 1 / (1 + a*x)
    Linear,     // 輔助類：f(x) = base + x * k
}
