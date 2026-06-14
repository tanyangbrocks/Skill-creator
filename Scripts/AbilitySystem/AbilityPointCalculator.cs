namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.World;

public static class AbilityPointCalculator
{
    // 發動類型對 MP 的乘數
    private static readonly Dictionary<AbilityActivationType, float> MpMultiplier = new()
    {
        { AbilityActivationType.Instant,   0.8f },
        { AbilityActivationType.Declare,   1.0f },
        { AbilityActivationType.Sustained, 1.5f },
    };

    // 計算技能整構的總能力點消耗（設計時資源）
    public static int CalculateTotalCost(SpellArray spell)
    {
        int total = 0;

        foreach (var slot in spell.Slots)
            total += slot.AbilityPointCost;

        foreach (var engrave in spell.GlobalEngravings)
            total += engrave.TotalAbilityPointCost;

        return total;
    }

    // 計算實際 MP 消耗（套用發動類型乘數）
    public static float CalculateMpCost(SpellArray spell)
    {
        float multiplier = MpMultiplier.GetValueOrDefault(spell.ActivationType, 1.0f);
        return spell.BaseMpCost * multiplier;
    }

    // 雙曲線效果公式：f(x) = 1 - 1 / (1 + a * x)
    public static float HyperbolicEffect(float points, float a)
        => 1f - 1f / (1f + a * points);

    // 線性效果公式：f(x) = base + x * k
    public static float LinearEffect(float points, float baseValue, float k)
        => baseValue + points * k;

    // 判斷技能整構能力點是否超過境界上限（數值定義於 PlayerController.TierApCap）
    public static bool ExceedsLevelCap(SpellArray spell, int playerLevel)
        => CalculateTotalCost(spell) > PlayerController.TierApCap(playerLevel);

    // 計算各 MP 類型的分攤消耗（供編輯器右側面板 read-only 顯示）
    // 未綁定 ManaTypeKey 的 Slot 不計入分攤；比例 = 該類型 Slot 數 / 全部已綁定 Slot 數。
    public static Dictionary<string, float> CalculateSlotCostByType(SpellArray spell)
    {
        var result = new Dictionary<string, float>();
        float multiplier = MpMultiplier.GetValueOrDefault(spell.ActivationType, 1.0f);
        float totalMp = spell.BaseMpCost * multiplier;

        var countByType = new Dictionary<string, int>();
        int totalBound = 0;
        foreach (var slot in spell.Slots)
        {
            if (slot.IsEmpty || slot.Totem?.Type == TotemType.Passive) continue;
            if (slot.ManaTypeKey is null) continue;
            countByType.TryGetValue(slot.ManaTypeKey, out int c);
            countByType[slot.ManaTypeKey] = c + 1;
            totalBound++;
        }

        if (totalBound == 0) return result;

        foreach (var (key, count) in countByType)
            result[key] = totalMp * count / totalBound;

        return result;
    }
}
