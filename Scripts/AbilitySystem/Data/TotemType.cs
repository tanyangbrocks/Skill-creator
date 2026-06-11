namespace SkillCreator.AbilitySystem.Data;

public enum TotemType
{
    Area,         // 範圍技能因子：施放形狀（扇形/周身/遠距圓形/射線衝擊）（無門檻）
    Technique,    // 武技技能因子：近戰武器招式（劍技/拳擊/盾防），與玩家手中武器掛鉤（無門檻）
    Projectile,   // 投射物技能因子：發射投射物（能量投射/實物投射）（無門檻）
    Passive,      // 被動技能因子：技能整構持續執行模式（持續偵測／開關式）（無門檻）
    Morph,        // 變幻技能因子：角色狀態改變（Buff/變身/飛行）（LV20+）
    Displacement, // 位移技能因子：衝刺/瞬移/閃避（LV20+）
    Summon,       // 召喚技能因子：召喚實體（LV30+）
    Domain,       // 領域技能因子：改變環境/地形/天候（LV50+）
    Custom,       // 自定義技能因子：玩家自由命名，語意由刻印決定
}
