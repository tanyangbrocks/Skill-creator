namespace SkillCreator.AbilitySystem.Elemental;

// ── 抽象基底 ──────────────────────────────────────────────────────────────

/// <summary>
/// 元素狀態效果基底類別。
/// 所有效果數值以具名常數定義，方便統一調整。
/// </summary>
public abstract class ElementalStatusEffect
{
    /// <summary>剩餘持續時間（秒）。</summary>
    public float RemainingDuration { get; set; }

    /// <summary>最大疊層數（A 方案：各層獨立計時）。</summary>
    public abstract int MaxStacks { get; }

    /// <summary>效果首次套用時呼叫（例如感電即時傷害）。</summary>
    public virtual void OnApply(IElementalTarget target) { }

    /// <summary>每幀呼叫（例如流沙累積減速）。</summary>
    public virtual void OnProcess(float delta) { }

    /// <summary>移速懲罰，加法；0 = 無，0.15 = 慢 15%（移動計時器 ×1.15）。</summary>
    public virtual float SpeedPenalty     => 0f;

    /// <summary>是否使目標完全無法移動。</summary>
    public virtual bool  Immobilizes      => false;

    /// <summary>受傷倍率加成，加法；0 = 無，0.20 = 多受 20% 傷害。</summary>
    public virtual float DamageTakenBonus => 0f;

    /// <summary>防禦力懲罰，0–1；0 = 無，0.10 = 防禦降低 10%。</summary>
    public virtual float DefensePenalty   => 0f;
}

// ── 鏽化（水 + 金）────────────────────────────────────────────────────────

/// <summary>鏽化：防禦降低 10%。觸發：水 + 金。</summary>
public sealed class RustEffect : ElementalStatusEffect
{
    public const float DefensePenaltyValue = 0.10f;  // 防禦降低幅度
    public const float DefaultDuration     = 5.0f;   // 持續秒數（⚠️ 待平衡）
    private const int  _maxStacks          = 3;       // 最大疊層

    public override int   MaxStacks      => _maxStacks;
    public override float DefensePenalty => DefensePenaltyValue;
}

// ── 蔓生（水 + 木）────────────────────────────────────────────────────────

/// <summary>蔓生：降低移速 15%（每層）。觸發：水 + 木。</summary>
public sealed class GrowthSlowEffect : ElementalStatusEffect
{
    public const float SpeedPenaltyValue = 0.15f;  // 每層移速懲罰
    public const float DefaultDuration   = 4.0f;   // 持續秒數（⚠️ 待平衡）
    private const int  _maxStacks        = 5;       // 最大疊層（5 層 = 75%）

    public override int   MaxStacks    => _maxStacks;
    public override float SpeedPenalty => SpeedPenaltyValue;
}

// ── 流沙（水 + 土）────────────────────────────────────────────────────────

/// <summary>流沙：每秒累積移速懲罰 10%，上限 80%。觸發：水 + 土。</summary>
public sealed class QuicksandSlowEffect : ElementalStatusEffect
{
    public const float SpeedPenaltyPerSec = 0.10f;  // 每秒累積懲罰（⚠️ 待平衡）
    public const float MaxSpeedPenalty    = 0.80f;  // 單層上限
    public const float DefaultDuration    = 5.0f;   // 持續秒數（⚠️ 待平衡）
    private const int  _maxStacks         = 8;       // 最大疊層

    private float _current = 0f;

    public override int   MaxStacks    => _maxStacks;
    public override float SpeedPenalty => _current;

    public override void OnProcess(float delta)
        => _current = MathF.Min(MaxSpeedPenalty, _current + SpeedPenaltyPerSec * delta);
}

// ── 感電（水 + 雷）────────────────────────────────────────────────────────

/// <summary>
/// 感電（佔位版）：即時傷害 + 短暫麻痺。觸發：水 + 雷。
/// ⚠️ TODO：等異常狀態系統完成後整體修改數值與機制。
/// </summary>
public sealed class ElectrocutionEffect : ElementalStatusEffect
{
    public const float ContactDamage   = 5.0f;   // ⚠️ TODO：待定
    public const float StunDurationSec = 0.5f;   // ⚠️ TODO：待定
    public const float DefaultDuration = 0.5f;   // 麻痺持續秒數
    private const int  _maxStacks      = 3;

    public override int  MaxStacks  => _maxStacks;
    public override bool Immobilizes => RemainingDuration > 0f;

    public override void OnApply(IElementalTarget target)
        => target.TakeDirectDamage(ContactDamage);
}

// ── 結凍（水 + 冰）────────────────────────────────────────────────────────

/// <summary>結凍：短暫無法行動，期間受傷害 +20%。觸發：水 + 冰。</summary>
public sealed class FrozenEffect : ElementalStatusEffect
{
    // ⚠️ ImmobilizeDurationSec 受其他因素影響，設計上可在未來由外部係數修改
    public const float ImmobilizeDurationSec = 1.0f;   // 無法行動時間（秒）
    public const float DamageTakenBonusValue = 0.20f;  // 受傷倍率加成
    public const float DefaultDuration       = 1.0f;
    private const int  _maxStacks            = 1;       // 疊加 = 各層獨立計時

    public override int   MaxStacks        => _maxStacks;
    public override bool  Immobilizes      => RemainingDuration > 0f;
    public override float DamageTakenBonus => DamageTakenBonusValue;
}
