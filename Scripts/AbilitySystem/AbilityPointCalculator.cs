namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;

public static class AbilityPointCalculator
{
    // 發動類型對 MP 的乘數
    private static readonly Dictionary<AbilityActivationType, float> MpMultiplier = new()
    {
        { AbilityActivationType.Instant,   0.8f },
        { AbilityActivationType.Declare,   1.0f },
        { AbilityActivationType.Sustained, 1.5f },
    };

    // 計算法陣的總能力點消耗（設計時資源）
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

    // 判斷法陣能力點是否超過等級上限（⚠️ 數值待調整）
    public static bool ExceedsLevelCap(SpellArray spell, int playerLevel)
    {
        int cap = playerLevel switch
        {
            < 20  => 50,
            < 30  => 120,
            < 50  => 250,
            < 70  => 500,
            _     => 900,
        };
        return CalculateTotalCost(spell) > cap;
    }
}
