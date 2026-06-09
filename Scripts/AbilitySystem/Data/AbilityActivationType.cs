namespace SkillCreator.AbilitySystem.Data;

public enum AbilityActivationType
{
    Instant,   // 即時型：×0.8 MP，無宣告窗口，無法被連鎖回應
    Declare,   // 宣告型：×1.0 MP，有反應窗口
    Sustained, // 持續生效型：×1.5 MP，持續消耗 MP
}
