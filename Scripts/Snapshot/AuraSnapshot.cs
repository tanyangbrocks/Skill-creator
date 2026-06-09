namespace SkillCreator.Snapshot;

using SkillCreator.AbilitySystem.Data;

/// <summary>S-3：ElementalAuraComponent 的不可變狀態快照。</summary>
public sealed record AuraSnapshot(
    IReadOnlyList<AuraEntryData>  Auras,
    IReadOnlyList<AuraEffectData> Effects
);

/// <summary>一條 Aura 的快照（元素 + 剩餘時間）。</summary>
public readonly record struct AuraEntryData(ElementType Element, float Duration);

/// <summary>
/// 一個元素狀態效果的快照（具體型別 + 剩餘時間 + 累積狀態）。
/// AccumulatedState：供 QuicksandSlowEffect._current 等累積型效果使用；其餘效果填 0f。
/// </summary>
public readonly record struct AuraEffectData(
    Type  EffectType,
    float RemainingDuration,
    float AccumulatedState
);
