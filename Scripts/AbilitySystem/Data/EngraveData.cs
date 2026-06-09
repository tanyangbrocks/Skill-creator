namespace SkillCreator.AbilitySystem.Data;

public class EngraveData
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public EngraveColor Color { get; init; }
    public ScalingType ScalingType { get; init; }

    // a 值（Hyperbolic）或 k 值（Linear）
    public float ScalingCoefficient { get; init; } = 1.0f;

    // Linear 公式的基礎值
    public float BaseEffect { get; init; } = 0f;

    // 局部（只影響所在圖騰）或全域（影響整個法陣所有圖騰）
    public bool IsGlobal { get; init; } = false;

    // 刻印本身的建構基礎成本（設計時固定，不因投入點改變）
    public int BaseCost { get; init; } = 1;

    // 玩家投入的能力點數
    public int PointsInvested { get; set; } = 0;

    // 該刻印消耗的總能力點
    public int TotalAbilityPointCost => BaseCost + PointsInvested;

    // 根據投入點計算效果值
    public float CalculateEffect() => ScalingType switch
    {
        ScalingType.Hyperbolic => 1f - 1f / (1f + ScalingCoefficient * PointsInvested),
        ScalingType.Linear     => BaseEffect + PointsInvested * ScalingCoefficient,
        _                      => 0f,
    };
}
