namespace SkillCreator.AbilitySystem.Data;

using SkillCreator.AbilitySystem.VM;

// 技能整構：玩家設計的完整能力單元
public class SpellArray
{
    public string Name { get; set; } = "";

    // 插槽式排列，執行順序 = 索引由小到大
    public List<SpellSlot> Slots { get; } = new();

    // 積木序列（空 = 施放時由 BlockAutoGenerator 根據插槽自動生成）
    public List<BlockNode> Blocks { get; } = new();

    // 全域刻印（影響整個技能整構所有技能因子）
    public List<EngraveData> GlobalEngravings { get; } = new();

    public AbilityActivationType ActivationType { get; set; } = AbilityActivationType.Instant;

    // 施放方式：直接施放 或 透過哪個容器執行
    public ContainerType Container { get; set; } = ContainerType.DirectCast;

    // 施放延遲（秒）；每個技能整構各自獨立
    public float CastDelay { get; set; } = 0.3f;

    // 基礎 MP 消耗（設計者設定，發動類型乘數由 AbilityPointCalculator 套用）
    public float BaseMpCost { get; set; } = 0f;

    // 連段：InvokeSpell 指向的下一個技能整構名稱（null = 連段終止）
    public string? NextInCombo { get; set; }

    // 場景唯一次刻印：本場最多宣告次數（0 = 無限制）
    public int SceneUseLimit { get; set; } = 0;

    // 是否為被動技能：由插槽中是否包含 Passive 技能因子決定（計算屬性）
    public bool IsPassive => Slots.Any(s => s.Totem?.Type == TotemType.Passive);

    // 容器效果：此技能整構投射物 / 召喚物的內部積木邏輯（巢狀最深 SafetyGuard.MaxContainerDepth 層）
    public SpellArray? ContainerEffect { get; set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Slots.Any(s => !s.IsEmpty);

    // W-6B：每個技能整構最多可使用的 MP 種類（日後可隨種族/特異體質擴充，不要 hardcode 數字）
    public const int MaxManaTypes = 3;

    /// <summary>
    /// 收集此技能整構（含容器效果）所有插槽中已指定的 ManaTypeKey（去重）。
    /// </summary>
    public HashSet<string> GetUsedManaTypes(bool recursive = true)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in Slots)
            if (slot.ManaTypeKey != null) result.Add(slot.ManaTypeKey);
        if (recursive && ContainerEffect != null)
            result.UnionWith(ContainerEffect.GetUsedManaTypes(recursive));
        return result;
    }

    /// <summary>此技能整構使用的 MP 種類數是否在上限內。</summary>
    public bool IsValidManaTypeCount(int limit = MaxManaTypes) =>
        GetUsedManaTypes().Count <= limit;

    /// <summary>
    /// 是否存在「有 MP 積木但尚未指定 ManaTypeKey」的技能因子（含容器效果遞迴檢查）。
    /// 供編輯器紅光警告與儲存驗證使用。
    /// </summary>
    public bool HasUnboundMpBlocks() =>
        Slots.Any(s => s.HasAnyMpBlocks && s.ManaTypeKey == null) ||
        (ContainerEffect?.HasUnboundMpBlocks() ?? false);

    /// <summary>
    /// W-3c：技能整構攜帶的主要元素屬性（供投射物 Apply Aura 使用）。
    /// 優先取 GlobalEngravings，再掃各插槽 LocalEngravings；無則回傳 None。
    /// </summary>
    public ElementType PrimaryElement
    {
        get
        {
            foreach (var e in GlobalEngravings)
                if (e.Element != ElementType.None) return e.Element;
            foreach (var slot in Slots)
                foreach (var e in slot.LocalEngravings)
                    if (e.Element != ElementType.None) return e.Element;
            return ElementType.None;
        }
    }
}
