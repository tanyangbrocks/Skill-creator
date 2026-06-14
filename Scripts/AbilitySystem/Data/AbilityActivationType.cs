namespace SkillCreator.AbilitySystem.Data;

public enum AbilityActivationType
{
    Instant   = 0, // 即時型：×0.8 MP，無宣告窗口，無法被連鎖回應
    Declare   = 1, // 宣告型：×1.0 MP，有反應窗口
    Sustained = 2, // 持續生效型：×1.5 MP，持續消耗 MP
    None      = 3, // 未指定：×1.0 MP，描述中不顯示類型標籤
}
