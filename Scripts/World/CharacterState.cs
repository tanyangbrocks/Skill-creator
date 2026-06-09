namespace SkillCreator.World;

/// <summary>
/// W-5b：角色動態狀態（體力、精力、心情、健康、社會身份）。
/// 所有閾值與消耗速率以具名常數定義，方便後期平衡調整。
/// </summary>
public class CharacterState
{
    // ════════════════════════════════════════════════════════════════
    //  體力類（Stamina）
    //  0–100%；戰鬥/衝刺消耗，休息回復；歸 0 無法進行需體力的行動
    // ════════════════════════════════════════════════════════════════

    public const float MaxStamina           = 100f;
    public const float StaminaRegenPerSec   = 5f;    // ⚠️ 待平衡
    public const float StaminaDrainCombat   = 2f;    // ⚠️ 戰鬥中每秒消耗，待平衡
    public const float StaminaDepletedThreshold = 1f;

    public float Stamina { get; private set; } = MaxStamina;
    public bool  IsStaminaDepleted => Stamina <= StaminaDepletedThreshold;

    public void SetStamina(float v)     => Stamina = Math.Clamp(v, 0f, MaxStamina);
    public void DrainStamina(float v)   => Stamina = MathF.Max(0f, Stamina - v);
    public void RestoreStamina(float v) => Stamina = MathF.Min(MaxStamina, Stamina + v);

    // ════════════════════════════════════════════════════════════════
    //  精力類（MentalEnergy / 精力值）
    //  0–100%；戰鬥、學習、精神力操作消耗；歸 0 無法進行需精力的行動
    // ════════════════════════════════════════════════════════════════

    public const float MaxMentalEnergy          = 100f;
    public const float MentalEnergyRegenPerSec  = 3f;   // ⚠️ 待平衡
    public const float MentalEnergyDrainCombat  = 1f;   // ⚠️ 戰鬥中每秒消耗，待平衡
    public const float MentalEnergyDepletedThreshold = 1f;

    public float MentalEnergy { get; private set; } = MaxMentalEnergy;
    public bool  IsMentalEnergyDepleted => MentalEnergy <= MentalEnergyDepletedThreshold;

    public void SetMentalEnergy(float v)     => MentalEnergy = Math.Clamp(v, 0f, MaxMentalEnergy);
    public void DrainMentalEnergy(float v)   => MentalEnergy = MathF.Max(0f, MentalEnergy - v);
    public void RestoreMentalEnergy(float v) => MentalEnergy = MathF.Min(MaxMentalEnergy, MentalEnergy + v);

    // ════════════════════════════════════════════════════════════════
    //  心情類（Mood）
    //  0–100；達到極端低值陷入瘋狂；高值帶來加成（待設計）
    // ════════════════════════════════════════════════════════════════

    public const float MaxMood              = 100f;
    public const float MoodInsanityThreshold = 10f;  // ⚠️ 低於此值陷入瘋狂
    public const float MoodDefaultValue      = 70f;

    public float Mood   { get; private set; } = MoodDefaultValue;
    public bool  IsInsane => Mood <= MoodInsanityThreshold;

    public void SetMood(float v)    => Mood = Math.Clamp(v, 0f, MaxMood);
    public void ModifyMood(float d) => SetMood(Mood + d);

    // ════════════════════════════════════════════════════════════════
    //  健康類（HealthCondition）
    //  體魄 / 生命值層次越高，越容易保持 Healthy；會影響心情值
    // ════════════════════════════════════════════════════════════════

    public HealthCondition HealthStatus { get; private set; } = HealthCondition.Healthy;
    public void SetHealthStatus(HealthCondition s) => HealthStatus = s;

    // ════════════════════════════════════════════════════════════════
    //  社會類（SocialStatus）
    //  ⚠️ stub：無 NPC 系統時不生效
    // ════════════════════════════════════════════════════════════════

    public SocialStatus Social { get; private set; } = SocialStatus.Normal;
    public void AddSocialFlag(SocialStatus flag)    => Social |= flag;
    public void RemoveSocialFlag(SocialStatus flag) => Social &= ~flag;
    public bool HasSocialFlag(SocialStatus flag)    => (Social & flag) != 0;

    // ════════════════════════════════════════════════════════════════
    //  每幀更新
    // ════════════════════════════════════════════════════════════════

    /// <summary>每幀呼叫。inCombat 由 CombatState.InCombat 傳入。</summary>
    public void Tick(float delta, bool inCombat)
    {
        if (inCombat)
        {
            DrainStamina(StaminaDrainCombat * delta);
            DrainMentalEnergy(MentalEnergyDrainCombat * delta);
        }
        else
        {
            RestoreStamina(StaminaRegenPerSec * delta);
            RestoreMentalEnergy(MentalEnergyRegenPerSec * delta);
        }

        // 心情值隨時間緩慢回歸中值（⚠️ 簡化規則，待完整設計）
        if (Mood < MoodDefaultValue)
            SetMood(Mood + 1f * delta);
    }
}

// ── 列舉定義 ────────────────────────────────────────────────────────

/// <summary>健康狀況列舉。</summary>
public enum HealthCondition
{
    Healthy,        // 健康
    Weakened,       // 衰弱
    Insomnia,       // 失眠
    HeavyCold,      // 重感冒
    // 後期可擴充更多（過敏、骨折…）
}

/// <summary>社會類狀態旗標（可同時存在多種）。</summary>
[Flags]
public enum SocialStatus
{
    Normal   = 0,
    Wanted   = 1 << 0,  // 被通緝
    Banned   = 1 << 1,  // 禁止出入某地區
    Welcomed = 1 << 2,  // 被某地區歡迎
}
