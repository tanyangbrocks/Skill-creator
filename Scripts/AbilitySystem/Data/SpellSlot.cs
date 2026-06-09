namespace SkillCreator.AbilitySystem.Data;

// 法陣中的單一插槽，容納一個圖騰及其附掛的刻印
public class SpellSlot
{
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
