namespace SkillCreator.AbilitySystem.Data;

using SkillCreator.AbilitySystem.VM;

// 法陣：玩家設計的完整能力單元
public class SpellArray
{
    public string Name { get; set; } = "";

    // 插槽式排列，執行順序 = 索引由小到大
    public List<SpellSlot> Slots { get; } = new();

    // 積木序列（空 = 施放時由 BlockAutoGenerator 根據插槽自動生成）
    public List<BlockNode> Blocks { get; } = new();

    // 全域刻印（影響整個法陣所有圖騰）
    public List<EngraveData> GlobalEngravings { get; } = new();

    public AbilityActivationType ActivationType { get; set; } = AbilityActivationType.Declare;

    // 執行容器：決定效果在哪裡、何時觸發
    public ContainerType Container { get; set; } = ContainerType.PlayerBody;

    // 施放延遲（秒）；每個法陣各自獨立
    public float CastDelay { get; set; } = 0.3f;

    // 基礎 MP 消耗（設計者設定，發動類型乘數由 AbilityPointCalculator 套用）
    public float BaseMpCost { get; set; } = 10f;

    // 連段：InvokeSpell 指向的下一個法陣名稱（null = 連段終止）
    public string? NextInCombo { get; set; }

    // 場景唯一次刻印：本場最多宣告次數（0 = 無限制）
    public int SceneUseLimit { get; set; } = 0;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Slots.Any(s => !s.IsEmpty);

    /// <summary>
    /// W-3c：法陣攜帶的主要元素屬性（供投射物 Apply Aura 使用）。
    /// 優先取 GlobalEngravings，再掃各插槽 LocalEngravings；無則回傳 None。
    /// </summary>
    public ElementType PrimaryElement
    {
        get
        {
            foreach (var e in GlobalEngravings)
                if (e.Element != ElementType.None) return e.Element;
            foreach (var slot in Slots)
                foreach (var e in slot.LocalEngravings)
                    if (e.Element != ElementType.None) return e.Element;
            return ElementType.None;
        }
    }
}
