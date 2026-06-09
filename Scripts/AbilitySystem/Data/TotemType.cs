namespace SkillCreator.AbilitySystem.Data;

public enum TotemType
{
    Trigger,      // 觸發圖騰：定義何時觸發效果（無門檻）
    Technique,    // 武技圖騰：媒介施放（劍技/弓箭等）（無門檻）
    Morph,        // 變幻圖騰：角色狀態改變（Buff/變身/飛行）（LV20+）
    Displacement, // 位移圖騰：衝刺/瞬移/閃避（LV20+）
    Summon,       // 召喚圖騰：召喚實體（LV30+）
    Domain,       // 領域圖騰：改變環境/地形/天候（LV50+）
}
