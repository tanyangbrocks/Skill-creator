namespace SkillCreator.AbilitySystem.Data;

// 技能的施放方式（決定「透過哪個入口執行效果」）
//
// DirectCast              = 玩家直接施放，不透過任何容器（非容器）
// Projectile/Summon*     = 裝載效果的「容器」實體；容器是能像生物一樣存放並執行效果的實體
//
// 注意：Contact 為觸發條件，不是容器，已從 UI 移除（保留 enum 供舊存檔相容）
public enum ContainerType
{
    DirectCast,      // 直接施放（玩家本體執行，非容器）
    Projectile,      // 投射物容器：施放後產生飛行投射物，命中時執行
    Contact,         // [已移除] 接觸條件（觸發條件，非容器；保留供舊存檔相容）
    SummonMinion,    // 精靈容器：由召喚精靈裝載並執行    // TODO-STUB: 召喚物 AI 未實作
    SummonTurret,    // 砲台容器：由召喚砲台裝載並執行    // TODO-STUB: 召喚物 AI 未實作
    SummonGuardian,  // 護衛容器：由召喚護衛裝載並執行    // TODO-STUB: 召喚物 AI 未實作
}
