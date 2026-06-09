namespace SkillCreator.Snapshot;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.World;

/// <summary>S-4b：CharacterStats 的不可變數值快照（含元素親和力三組字典）。</summary>
public sealed record CharStatsSnapshot
{
    // ── 生命 ──
    public float MaxHpBase        { get; init; }
    public float HpRegenRate      { get; init; }

    // ── 防禦 ──
    public float BaseDefense      { get; init; }
    public float DamageReduction  { get; init; }
    public float AntiCombo        { get; init; }
    public float AntiCrit         { get; init; }
    public float Immunity         { get; init; }

    // ── 攻擊 ──
    public float Power            { get; init; }
    public float PhysicalDmgPct   { get; init; }
    public float CritRate         { get; init; }
    public float CritDmgMult      { get; init; }
    public float SuperCritRate    { get; init; }
    public float SuperCritDmgMult { get; init; }
    public float Thorns           { get; init; }
    public float Lifesteal        { get; init; }

    // ── MP ──
    public float MaxMpBase        { get; init; }
    public float MpRegenRate      { get; init; }
    public float MpOutput         { get; init; }
    public float MpEfficiency     { get; init; }
    public float MpControl        { get; init; }

    // ── 機動 ──
    public float MoveSpeedMult    { get; init; }
    public float AtkSpeedMult     { get; init; }
    public float DodgeRate        { get; init; }
    public float HitRate          { get; init; }
    public float DoubleHitRate    { get; init; }
    public float TripleHitRate    { get; init; }

    // ── 社會/外貌 ──
    public float Appearance       { get; init; }
    public float Temperament      { get; init; }
    public float TrustLevel       { get; init; }
    public float AffinityScore    { get; init; }
    public float Luck             { get; init; }

    // ── 生產/探索 ──
    public float Deliciousness    { get; init; }
    public float Fragrance        { get; init; }
    public float MaterialRarity   { get; init; }
    public float Insight          { get; init; }
    public float Stealth          { get; init; }

    // ── 元素親和力（深拷貝，以 IReadOnlyDictionary 封裝）──
    public IReadOnlyDictionary<ElementType, float> ElemAffinity   { get; init; }
        = new Dictionary<ElementType, float>();
    public IReadOnlyDictionary<ElementType, float> ElemOutputMult { get; init; }
        = new Dictionary<ElementType, float>();
    public IReadOnlyDictionary<ElementType, float> ElemResistance { get; init; }
        = new Dictionary<ElementType, float>();

    // ── 經驗加成 ──
    public float SkillExpBonus      { get; init; }
    public float LevelExpBonus      { get; init; }
    public float NonCombatExpBonus  { get; init; }
    public float DispatchEfficiency { get; init; }

    // ── 天賦能力點 ──
    public int TalentConstitution { get; init; }
    public int TalentStrength     { get; init; }
    public int TalentEndurance    { get; init; }
    public int TalentAgility      { get; init; }
    public int TalentWisdom       { get; init; }
    public int TalentCharisma     { get; init; }
    public int TalentLuck         { get; init; }

    // ── 基礎能力點 ──
    public int ConstitutionPoints { get; init; }
    public int StrengthPoints     { get; init; }
    public int EndurancePoints    { get; init; }
    public int AgilityPoints      { get; init; }
    public int WisdomPoints       { get; init; }
    public int CharismaPoints     { get; init; }
    public int LuckPoints         { get; init; }

    // ── 其他 ──
    public float BloodlineStrength { get; init; }

    /// <summary>將快照的所有欄位寫回 CharacterStats 實例。</summary>
    public void ApplyTo(CharacterStats s)
    {
        s.MaxHpBase        = MaxHpBase;
        s.HpRegenRate      = HpRegenRate;
        s.BaseDefense      = BaseDefense;
        s.DamageReduction  = DamageReduction;
        s.AntiCombo        = AntiCombo;
        s.AntiCrit         = AntiCrit;
        s.Immunity         = Immunity;
        s.Power            = Power;
        s.PhysicalDmgPct   = PhysicalDmgPct;
        s.CritRate         = CritRate;
        s.CritDmgMult      = CritDmgMult;
        s.SuperCritRate    = SuperCritRate;
        s.SuperCritDmgMult = SuperCritDmgMult;
        s.Thorns           = Thorns;
        s.Lifesteal        = Lifesteal;
        s.MaxMpBase        = MaxMpBase;
        s.MpRegenRate      = MpRegenRate;
        s.MpOutput         = MpOutput;
        s.MpEfficiency     = MpEfficiency;
        s.MpControl        = MpControl;
        s.MoveSpeedMult    = MoveSpeedMult;
        s.AtkSpeedMult     = AtkSpeedMult;
        s.DodgeRate        = DodgeRate;
        s.HitRate          = HitRate;
        s.DoubleHitRate    = DoubleHitRate;
        s.TripleHitRate    = TripleHitRate;
        s.Appearance       = Appearance;
        s.Temperament      = Temperament;
        s.TrustLevel       = TrustLevel;
        s.AffinityScore    = AffinityScore;
        s.Luck             = Luck;
        s.Deliciousness    = Deliciousness;
        s.Fragrance        = Fragrance;
        s.MaterialRarity   = MaterialRarity;
        s.Insight          = Insight;
        s.Stealth          = Stealth;
        foreach (var kvp in ElemAffinity)   s.SetElemAffinity(kvp.Key, kvp.Value);
        foreach (var kvp in ElemOutputMult) s.SetElemOutputMult(kvp.Key, kvp.Value);
        foreach (var kvp in ElemResistance) s.SetElemResistance(kvp.Key, kvp.Value);
        s.SkillExpBonus      = SkillExpBonus;
        s.LevelExpBonus      = LevelExpBonus;
        s.NonCombatExpBonus  = NonCombatExpBonus;
        s.DispatchEfficiency = DispatchEfficiency;
        s.TalentConstitution = TalentConstitution;
        s.TalentStrength     = TalentStrength;
        s.TalentEndurance    = TalentEndurance;
        s.TalentAgility      = TalentAgility;
        s.TalentWisdom       = TalentWisdom;
        s.TalentCharisma     = TalentCharisma;
        s.TalentLuck         = TalentLuck;
        s.ConstitutionPoints = ConstitutionPoints;
        s.StrengthPoints     = StrengthPoints;
        s.EndurancePoints    = EndurancePoints;
        s.AgilityPoints      = AgilityPoints;
        s.WisdomPoints       = WisdomPoints;
        s.CharismaPoints     = CharismaPoints;
        s.LuckPoints         = LuckPoints;
        s.BloodlineStrength  = BloodlineStrength;
    }

    /// <summary>從 CharacterStats 建立快照。元素字典以所有已知 ElementType 迭代複製。</summary>
    public static CharStatsSnapshot From(CharacterStats s)
    {
        var elems = Enum.GetValues<ElementType>();
        return new CharStatsSnapshot
        {
            MaxHpBase        = s.MaxHpBase,
            HpRegenRate      = s.HpRegenRate,
            BaseDefense      = s.BaseDefense,
            DamageReduction  = s.DamageReduction,
            AntiCombo        = s.AntiCombo,
            AntiCrit         = s.AntiCrit,
            Immunity         = s.Immunity,
            Power            = s.Power,
            PhysicalDmgPct   = s.PhysicalDmgPct,
            CritRate         = s.CritRate,
            CritDmgMult      = s.CritDmgMult,
            SuperCritRate    = s.SuperCritRate,
            SuperCritDmgMult = s.SuperCritDmgMult,
            Thorns           = s.Thorns,
            Lifesteal        = s.Lifesteal,
            MaxMpBase        = s.MaxMpBase,
            MpRegenRate      = s.MpRegenRate,
            MpOutput         = s.MpOutput,
            MpEfficiency     = s.MpEfficiency,
            MpControl        = s.MpControl,
            MoveSpeedMult    = s.MoveSpeedMult,
            AtkSpeedMult     = s.AtkSpeedMult,
            DodgeRate        = s.DodgeRate,
            HitRate          = s.HitRate,
            DoubleHitRate    = s.DoubleHitRate,
            TripleHitRate    = s.TripleHitRate,
            Appearance       = s.Appearance,
            Temperament      = s.Temperament,
            TrustLevel       = s.TrustLevel,
            AffinityScore    = s.AffinityScore,
            Luck             = s.Luck,
            Deliciousness    = s.Deliciousness,
            Fragrance        = s.Fragrance,
            MaterialRarity   = s.MaterialRarity,
            Insight          = s.Insight,
            Stealth          = s.Stealth,
            ElemAffinity     = elems.ToDictionary(e => e, e => s.GetElemAffinity(e)),
            ElemOutputMult   = elems.ToDictionary(e => e, e => s.GetElemOutputMult(e)),
            ElemResistance   = elems.ToDictionary(e => e, e => s.GetElemResistance(e)),
            SkillExpBonus      = s.SkillExpBonus,
            LevelExpBonus      = s.LevelExpBonus,
            NonCombatExpBonus  = s.NonCombatExpBonus,
            DispatchEfficiency = s.DispatchEfficiency,
            TalentConstitution = s.TalentConstitution,
            TalentStrength     = s.TalentStrength,
            TalentEndurance    = s.TalentEndurance,
            TalentAgility      = s.TalentAgility,
            TalentWisdom       = s.TalentWisdom,
            TalentCharisma     = s.TalentCharisma,
            TalentLuck         = s.TalentLuck,
            ConstitutionPoints = s.ConstitutionPoints,
            StrengthPoints     = s.StrengthPoints,
            EndurancePoints    = s.EndurancePoints,
            AgilityPoints      = s.AgilityPoints,
            WisdomPoints       = s.WisdomPoints,
            CharismaPoints     = s.CharismaPoints,
            LuckPoints         = s.LuckPoints,
            BloodlineStrength  = s.BloodlineStrength,
        };
    }
}
