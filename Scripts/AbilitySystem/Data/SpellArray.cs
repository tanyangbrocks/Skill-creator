namespace SkillCreator.AbilitySystem.Data;

// 法陣：玩家設計的完整能力單元
public class SpellArray
{
    public string Name { get; set; } = "";

    // 插槽式排列，執行順序 = 索引由小到大
    public List<SpellSlot> Slots { get; } = new();

    // 全域刻印（影響整個法陣所有圖騰）
    public List<EngraveData> GlobalEngravings { get; } = new();

    public AbilityActivationType ActivationType { get; set; } = AbilityActivationType.Declare;

    // 施放延遲（秒）；每個法陣各自獨立
    public float CastDelay { get; set; } = 0.3f;

    // 基礎 MP 消耗（設計者設定，發動類型乘數由 AbilityPointCalculator 套用）
    public float BaseMpCost { get; set; } = 10f;

    // 連段：InvokeSpell 指向的下一個法陣名稱（null = 連段終止）
    public string? NextInCombo { get; set; }

    // 場景唯一次刻印：本場最多宣告次數（0 = 無限制）
    public int SceneUseLimit { get; set; } = 0;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Slots.Any(s => !s.IsEmpty);
}
