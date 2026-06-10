namespace SkillCreator.AbilitySystem.Data;

public enum TotemType
{
    Area,         // 範圍圖騰：施放形狀（扇形/周身/遠距圓形/射線衝擊）（無門檻）
    Technique,    // 武技圖騰：近戰武器招式（劍技/拳擊/盾防），與玩家手中武器掛鉤（無門檻）
    Projectile,   // 投射物圖騰：發射投射物（能量投射/實物投射）（無門檻）
    Morph,        // 變幻圖騰：角色狀態改變（Buff/變身/飛行）（LV20+）
    Displacement, // 位移圖騰：衝刺/瞬移/閃避（LV20+）
    Summon,       // 召喚圖騰：召喚實體（LV30+）
    Domain,       // 領域圖騰：改變環境/地形/天候（LV50+）
}
