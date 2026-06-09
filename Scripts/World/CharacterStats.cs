namespace SkillCreator.World;

using SkillCreator.AbilitySystem.Data;

/// <summary>
/// W-5a：玩家角色完整數值結構。
/// 分為「已接入邏輯」與「⚠️ stub（預留欄位，後期填入公式）」兩類。
/// 所有初始值以具名常數或預設值標記，待平衡時集中修改。
/// </summary>
public class CharacterStats
{
    // ════════════════════════════════════════════════════════════════
    //  已接入邏輯的核心數值
    // ════════════════════════════════════════════════════════════════

    // ── 生命 ──────────────────────────────────────────────────────
    public float MaxHpBase      { get; set; } = 100f;  // 最大生命基礎值（⚠️ 待平衡）
    public float HpRegenRate    { get; set; } = 0f;    // HP/秒 自然回復（⚠️ stub）

    // ── 防禦 ──────────────────────────────────────────────────────
    /// <summary>固定傷害減免（疊加裝備 TotalDefFlat）。</summary>
    public float BaseDefense    { get; set; } = 0f;    // 基礎防禦（⚠️ stub: 0 → 等同舊行為）
    /// <summary>% 減傷（0–1，1 = 完全免疫）。</summary>
    public float DamageReduction{ get; set; } = 0f;    // ⚠️ stub
    public float AntiCombo      { get; set; } = 0f;    // 抗連擊（⚠️ stub）
    public float AntiCrit       { get; set; } = 0f;    // 抗暴（⚠️ stub）
    public float Immunity       { get; set; } = 0f;    // 免疫（⚠️ stub）

    // ── 攻擊 ──────────────────────────────────────────────────────
    public float Power          { get; set; } = 0f;    // 力量（⚠️ stub）
    public float PhysicalDmgPct { get; set; } = 0f;    // 物傷加成 %（⚠️ stub）
    public float CritRate       { get; set; } = 0.05f; // 爆率（預設 5%）
    public float CritDmgMult    { get; set; } = 1.5f;  // 爆傷倍率（預設 ×1.5）
    public float SuperCritRate  { get; set; } = 0f;    // 超爆率（⚠️ stub）
    public float SuperCritDmgMult{ get; set; } = 2.0f; // 超爆傷（⚠️ stub）
    public float Thorns         { get; set; } = 0f;    // 反傷（⚠️ stub）
    public float Lifesteal      { get; set; } = 0f;    // 吸血（⚠️ stub）

    // ── MP ────────────────────────────────────────────────────────
    public float MaxMpBase      { get; set; } = 100f;  // 最大 MP 基礎值（⚠️ 待平衡）
    /// <summary>MP/秒 自然回復（取代 PlayerController 舊 MpRegen 常數）。</summary>
    public float MpRegenRate    { get; set; } = 8f;    // ⚠️ 待平衡
    public float MpOutput       { get; set; } = 1.0f;  // MP輸出效率（⚠️ stub，1.0=標準）
    public float MpEfficiency   { get; set; } = 1.0f;  // MP功率（⚠️ stub）
    public float MpControl      { get; set; } = 1.0f;  // MP掌控度（⚠️ stub）

    // ── 機動 ──────────────────────────────────────────────────────
    /// <summary>移速倍率（>1=加速；與 Aura.SpeedPenalty 組合計算）。</summary>
    public float MoveSpeedMult  { get; set; } = 1.0f;  // 預設 1.0（無加成）
    public float AtkSpeedMult   { get; set; } = 1.0f;  // 攻速（⚠️ stub）
    public float DodgeRate      { get; set; } = 0f;    // 閃避率（⚠️ stub）
    public float HitRate        { get; set; } = 1.0f;  // 命中率（⚠️ stub）
    public float DoubleHitRate  { get; set; } = 0f;    // 二連擊率（⚠️ stub）
    public float TripleHitRate  { get; set; } = 0f;    // 三連擊率（⚠️ stub）

    // ════════════════════════════════════════════════════════════════
    //  社會 / 外貌類（全部 stub，無 NPC 系統時不生效）
    // ════════════════════════════════════════════════════════════════

    public float Appearance     { get; set; } = 0f;    // 顏值
    public float Temperament    { get; set; } = 0f;    // 氣質
    public float TrustLevel     { get; set; } = 0f;    // 信任度
    public float AffinityScore  { get; set; } = 0f;    // 好感度
    public float Luck           { get; set; } = 0f;    // 幸運度

    // ════════════════════════════════════════════════════════════════
    //  生產 / 探索類（全部 stub）
    // ════════════════════════════════════════════════════════════════

    public float Deliciousness  { get; set; } = 0f;    // 美味度
    public float Fragrance      { get; set; } = 0f;    // 香氣度
    public float MaterialRarity { get; set; } = 0f;    // 素材珍稀度
    public float Insight        { get; set; } = 0f;    // 洞察度
    public float Stealth        { get; set; } = 0f;    // 隱密度

    // ════════════════════════════════════════════════════════════════
    //  元素親和力 / 輸出 / 抗性（W-3 擴充後使用；11 種元素）
    // ════════════════════════════════════════════════════════════════

    private readonly Dictionary<ElementType, float> _elemAffinity   = new();
    private readonly Dictionary<ElementType, float> _elemOutputMult = new();
    private readonly Dictionary<ElementType, float> _elemResistance = new();

    /// <summary>元素親和力（0 = 無加成）。</summary>
    public float GetElemAffinity(ElementType e)    => _elemAffinity.GetValueOrDefault(e, 0f);
    /// <summary>元素輸出倍率（1.0 = 標準）。</summary>
    public float GetElemOutputMult(ElementType e)  => _elemOutputMult.GetValueOrDefault(e, 1.0f);
    /// <summary>元素抗性（0 = 無抗性；1.0 = 完全免疫）。</summary>
    public float GetElemResistance(ElementType e)  => _elemResistance.GetValueOrDefault(e, 0f);
    public void  SetElemAffinity(ElementType e, float v)   => _elemAffinity[e]   = v;
    public void  SetElemOutputMult(ElementType e, float v) => _elemOutputMult[e] = v;
    public void  SetElemResistance(ElementType e, float v) => _elemResistance[e] = v;

    // ════════════════════════════════════════════════════════════════
    //  法則 / 能量親和力（W-6/W-7 實作後填入）
    // ════════════════════════════════════════════════════════════════

    // 預留 Dictionary 結構，key 待 W-6/W-7 確定型別後補入
    // private readonly Dictionary<LawType, float> _lawAffinity = new();
    // private readonly Dictionary<int, float> _manaAffinity = new();   // key = ManaTypeId

    // ════════════════════════════════════════════════════════════════
    //  經驗加成（全部 stub）
    // ════════════════════════════════════════════════════════════════

    public float SkillExpBonus      { get; set; } = 0f;   // MP技能經驗加成
    public float LevelExpBonus      { get; set; } = 0f;   // 等級經驗加成
    public float NonCombatExpBonus  { get; set; } = 0f;   // 非戰類技能經驗加成
    public float DispatchEfficiency { get; set; } = 1.0f; // 派遣任務效率加成

    // ════════════════════════════════════════════════════════════════
    //  天賦能力點（W-10 角色創建後設定；初始皆 50）
    // ════════════════════════════════════════════════════════════════

    public int TalentConstitution { get; set; } = 50;  // 體魄天賦
    public int TalentStrength     { get; set; } = 50;  // 肌力天賦
    public int TalentEndurance    { get; set; } = 50;  // 耐力天賦
    public int TalentAgility      { get; set; } = 50;  // 敏捷天賦
    public int TalentWisdom       { get; set; } = 50;  // 智慧天賦
    public int TalentCharisma     { get; set; } = 50;  // 魅力天賦
    public int TalentLuck         { get; set; } = 50;  // 幸運天賦

    // ════════════════════════════════════════════════════════════════
    //  基礎能力點（W-10 分配，初始 0；各天賦對加成公式待 W-10 填入）
    // ════════════════════════════════════════════════════════════════

    public int ConstitutionPoints { get; set; } = 0;  // 體魄點
    public int StrengthPoints     { get; set; } = 0;  // 肌力點
    public int EndurancePoints    { get; set; } = 0;  // 耐力點
    public int AgilityPoints      { get; set; } = 0;  // 敏捷點
    public int WisdomPoints       { get; set; } = 0;  // 智慧點
    public int CharismaPoints     { get; set; } = 0;  // 魅力點
    public int LuckPoints         { get; set; } = 0;  // 幸運點

    // ── 其他雜項 ──────────────────────────────────────────────────
    public float BloodlineStrength { get; set; } = 1.0f;  // 血脈強度（⚠️ stub）
}
