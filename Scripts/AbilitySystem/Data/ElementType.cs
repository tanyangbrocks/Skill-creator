namespace SkillCreator.AbilitySystem.Data;

/// <summary>
/// 十一種基礎元素（蒼究元素體系），對應屬性刻印 EngraveColor.Elemental。
/// 元素碰撞效果待 W-3 元素碰撞材質表實作後填入。
/// </summary>
public enum ElementType
{
    None    = 0,
    Metal   = 1,  // 金
    Wood    = 2,  // 木
    Water   = 3,  // 水
    Fire    = 4,  // 火
    Earth   = 5,  // 土
    Ice     = 6,  // 冰
    Wind    = 7,  // 風
    Light   = 8,  // 光
    Dark    = 9,  // 暗
    Thunder = 10, // 雷
    Poison  = 11, // 毒
}
