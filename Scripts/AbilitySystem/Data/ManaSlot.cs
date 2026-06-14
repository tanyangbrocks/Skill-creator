namespace SkillCreator.AbilitySystem.Data;

/// <summary>
/// 玩家持有的單一 MP 槽位。
/// 每種已解鎖的 MP 類型對應一個獨立槽位（Current / Max / RegenRate 各自獨立）。
/// Max 與 RegenRate 初期使用固定預設值；W-10 角色創建完成後可按種族/天賦覆寫。
/// </summary>
public sealed class ManaSlot
{
    public const float DefaultMax       = 100f;
    public const float DefaultRegenRate = 1f;    // 每秒回復量

    public string ManaTypeKey { get; }
    public float  Current     { get; set; }
    public float  Max         { get; set; }
    public float  RegenRate   { get; set; }

    public ManaSlot(string manaTypeKey, float max = DefaultMax, float regenRate = DefaultRegenRate)
    {
        ManaTypeKey = manaTypeKey;
        Current     = max;
        Max         = max;
        RegenRate   = regenRate;
    }

    /// <summary>每幀回復 MP（在 PlayerController._Process 呼叫）。</summary>
    public void Tick(float delta) =>
        Current = Math.Min(Current + RegenRate * delta, Max);
}
