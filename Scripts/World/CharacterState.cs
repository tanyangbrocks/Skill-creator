namespace SkillCreator.World;

/// <summary>
/// W-5b：角色動態狀態（體力、精力、心情、健康、社會身份、生存四值）。
/// 所有閾值與消耗速率以具名常數定義，方便後期平衡調整。
///
/// Tick() 回傳本幀應「直接扣血」的生存傷害總量（繞過防禦管線）。
/// </summary>
public class CharacterState
{
    // ════════════════════════════════════════════════════════════════
    //  體力類（Stamina）
    //  0–100%；戰鬥/衝刺消耗，休息回復；歸 0 無法進行需體力的行動
    // ════════════════════════════════════════════════════════════════

    public const float MaxStamina               = 100f;
    public const float StaminaRegenPerSec       = 5f;    // ⚠️ 待平衡
    public const float StaminaDrainCombat       = 2f;    // ⚠️ 戰鬥中每秒消耗，待平衡
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

    public const float MaxMentalEnergy               = 100f;
    public const float MentalEnergyRegenPerSec       = 3f;  // ⚠️ 待平衡
    public const float MentalEnergyDrainCombat       = 1f;  // ⚠️ 戰鬥中每秒消耗，待平衡
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

    public const float MaxMood               = 100f;
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
    //  體溫（Temperature）
    //  追蹤角色核心體溫（°C）；向「環境溫度」緩慢漂移
    //  AmbientTemperature 由 PlayerController.UpdateEnvironment 每幀設置
    //  （含基礎室溫 + 元素 Aura 溫度偏移：Fire +15 / Ice -20 / Water -8）
    //
    //  IsHypothermic / IsHeatstroke：目前只記錄狀態，效果待接入
    // ════════════════════════════════════════════════════════════════

    public const float NormalBodyTemp       = 36.5f;  // °C
    public const float DefaultAmbientTemp   = 20.0f;  // °C，預設室溫
    public const float HypothermiaThreshold = 10.0f;  // ⚠️ 低於此值視為體溫過低
    public const float HeatstrokeThreshold  = 42.0f;  // ⚠️ 高於此值視為中暑
    public const float BodyTempAdaptRate    = 1.5f;   // ⚠️ °C/s，體溫向環境溫度漂移速率
    public const float ThirstHeatMultiplier = 2.0f;   // ⚠️ 中暑時口渴消耗倍率

    /// <summary>角色目前核心體溫（°C）。</summary>
    public float BodyTemperature { get; private set; } = NormalBodyTemp;
    /// <summary>由 PlayerController.UpdateEnvironment 每幀設置；CharacterState 只讀取，不主動修改。</summary>
    public float AmbientTemperature { get; set; } = DefaultAmbientTemp;

    public bool IsHypothermic => BodyTemperature <= HypothermiaThreshold;
    public bool IsHeatstroke  => BodyTemperature >= HeatstrokeThreshold;

    public void SetBodyTemperature(float v)    => BodyTemperature = Math.Clamp(v, -60f, 100f);
    public void ModifyBodyTemperature(float d) => SetBodyTemperature(BodyTemperature + d);

    // ════════════════════════════════════════════════════════════════
    //  口渴（Thirst）
    //  0–100；自然消耗，耗盡後每秒直接扣血；飲水恢復
    // ════════════════════════════════════════════════════════════════

    public const float MaxThirst               = 100f;
    public const float DefaultThirst           = 100f;
    public const float ThirstDrainPerSec       = 0.05f; // ⚠️ 待平衡；~33 分鐘耗盡
    public const float ThirstWarningThreshold  = 20f;
    public const float ThirstCriticalThreshold = 5f;
    public const float ThirstCriticalDamage    = 2f;    // ⚠️ 耗盡後每秒傷害

    public float Thirst { get; private set; } = DefaultThirst;
    public bool  IsThirsty    => Thirst <= ThirstWarningThreshold;
    public bool  IsDehydrated => Thirst <= ThirstCriticalThreshold;

    public void SetThirst(float v)     => Thirst = Math.Clamp(v, 0f, MaxThirst);
    public void DrainThirst(float v)   => Thirst = MathF.Max(0f, Thirst - v);
    public void RestoreThirst(float v) => Thirst = MathF.Min(MaxThirst, Thirst + v);

    // ════════════════════════════════════════════════════════════════
    //  飢餓（Hunger）
    //  0–100；自然消耗（比口渴慢），耗盡後每秒直接扣血；進食恢復
    // ════════════════════════════════════════════════════════════════

    public const float MaxHunger               = 100f;
    public const float DefaultHunger           = 100f;
    public const float HungerDrainPerSec       = 0.02f; // ⚠️ 待平衡；~83 分鐘耗盡
    public const float HungerWarningThreshold  = 20f;
    public const float HungerCriticalThreshold = 5f;
    public const float HungerCriticalDamage    = 1f;    // ⚠️ 耗盡後每秒傷害

    public float Hunger { get; private set; } = DefaultHunger;
    public bool  IsHungry   => Hunger <= HungerWarningThreshold;
    public bool  IsStarving => Hunger <= HungerCriticalThreshold;

    public void SetHunger(float v)     => Hunger = Math.Clamp(v, 0f, MaxHunger);
    public void DrainHunger(float v)   => Hunger = MathF.Max(0f, Hunger - v);
    public void RestoreHunger(float v) => Hunger = MathF.Min(MaxHunger, Hunger + v);

    // ════════════════════════════════════════════════════════════════
    //  氧氣（Oxygen）
    //  0–100；正常環境保持滿值；缺氧環境（水下）快速消耗；耗盡窒息直接扣血
    //  IsOxygenDeprived 由 PlayerController.UpdateEnvironment 每幀設置
    // ════════════════════════════════════════════════════════════════

    public const float MaxOxygen               = 100f;
    public const float OxygenDrainPerSec       = 10f;   // ⚠️ 待平衡；~10 秒耗盡
    public const float OxygenRegenPerSec       = 30f;   // ⚠️ 回到空氣中快速補充
    public const float OxygenCriticalThreshold = 5f;
    public const float OxygenCriticalDamage    = 15f;   // ⚠️ 窒息每秒傷害（直接扣血）

    public float Oxygen { get; private set; } = MaxOxygen;
    public bool  IsBreathingNormal => Oxygen >= MaxOxygen - 0.1f;
    public bool  IsSuffocating     => Oxygen <= OxygenCriticalThreshold;
    /// <summary>由 PlayerController.UpdateEnvironment 每幀設置（站在 Water 格 = true）。</summary>
    public bool  IsOxygenDeprived  { get; set; } = false;

    public void SetOxygen(float v)     => Oxygen = Math.Clamp(v, 0f, MaxOxygen);
    public void DrainOxygen(float v)   => Oxygen = MathF.Max(0f, Oxygen - v);
    public void RestoreOxygen(float v) => Oxygen = MathF.Min(MaxOxygen, Oxygen + v);

    // ════════════════════════════════════════════════════════════════
    //  每幀更新（回傳本幀應直接扣除的生存傷害總量，繞過防禦管線）
    // ════════════════════════════════════════════════════════════════

    /// <summary>每幀呼叫。inCombat 由 CombatState.InCombat 傳入。</summary>
    public float Tick(float delta, bool inCombat)
    {
        float pendingDamage = 0f;

        // ── 體力 / 精力
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

        // ── 心情值緩慢回歸中值（⚠️ 簡化規則，待完整設計）
        if (Mood < MoodDefaultValue)
            SetMood(Mood + 1f * delta);

        // ── 體溫：向 AmbientTemperature 漂移（AmbientTemperature 由外部設置）
        float tempDiff = AmbientTemperature - BodyTemperature;
        float tempStep = BodyTempAdaptRate * delta;
        if (MathF.Abs(tempDiff) <= tempStep)
            BodyTemperature = AmbientTemperature;
        else
            BodyTemperature += MathF.Sign(tempDiff) * tempStep;

        // ── 口渴：正常消耗；中暑時加速
        float thirstDrain = ThirstDrainPerSec;
        if (IsHeatstroke) thirstDrain *= ThirstHeatMultiplier;
        DrainThirst(thirstDrain * delta);
        if (IsDehydrated)
            pendingDamage += ThirstCriticalDamage * delta;

        // ── 飢餓：正常消耗
        DrainHunger(HungerDrainPerSec * delta);
        if (IsStarving)
            pendingDamage += HungerCriticalDamage * delta;

        // ── 氧氣：依 IsOxygenDeprived 消耗或恢復
        if (IsOxygenDeprived)
        {
            DrainOxygen(OxygenDrainPerSec * delta);
            if (IsSuffocating)
                pendingDamage += OxygenCriticalDamage * delta;
        }
        else
        {
            RestoreOxygen(OxygenRegenPerSec * delta);
        }

        return pendingDamage;
    }
}

// ── 列舉定義 ────────────────────────────────────────────────────────

/// <summary>健康狀況列舉。</summary>
public enum HealthCondition
{
    Healthy,    // 健康
    Weakened,   // 衰弱
    Insomnia,   // 失眠
    HeavyCold,  // 重感冒
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
