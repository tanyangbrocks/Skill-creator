namespace SkillCreator.UI;

// 編輯器設定（記憶體層級，每次啟動重置為預設值）
public static class EditorSettings
{
    // 技能因子加入主腳本時，自動在後面插入對應的預設 Action 刻印
    public static bool AutoInsertBaseEngraving { get; set; } = true;
}
