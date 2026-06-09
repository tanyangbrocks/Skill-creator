namespace SkillCreator.AbilitySystem.Data;

// 技能的執行容器類型（決定「在哪裡/何時」執行效果）
public enum ContainerType
{
    PlayerBody,  // 玩家本體直接執行（預設）
    Projectile,  // 投射物容器：施放後產生飛行投射物，命中時執行
    Contact,     // 接觸容器：攻擊/碰撞觸發（Phase 3 完整實作）
}
