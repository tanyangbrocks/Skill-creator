namespace SkillCreator.AbilitySystem.Data;

// 技能整構中的單一插槽，容納一個技能因子及其附掛的刻印
public class SpellSlot
{
    // 積木序列中引用此插槽的名稱（空 = 自動命名為 "slot_N"）
    public string Name { get; set; } = "";

    public TotemData? Totem { get; set; }
    public List<EngraveData> LocalEngravings { get; } = new();

    public bool IsEmpty => Totem is null;

    public int AbilityPointCost
    {
        get
        {
            if (Totem is null) return 0;
            return Totem.BaseAbilityPointCost
                + LocalEngravings.Sum(e => e.TotalAbilityPointCost);
        }
    }
}
