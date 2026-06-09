namespace SkillCreator.AbilitySystem.VM;

public enum BlockType
{
    // 控制流
    If,
    RepeatN,
    Wait,       // 等待 N 秒；Phase 1 限制：只支援出現在頂層積木序列

    // 效果標記
    EffectLabel,
    OnEffectStart,
    OnEffectEnd,

    // 呼叫
    InvokeSpell,  // 發動另一個法陣（連段）
    InvokeTotem,  // 觸發法陣內具名圖騰

    // 圖騰狀態查詢（用於 IF 條件）
    TotemDone,
    TotemHit,
    TotemFizzle,

    // 變數（Phase 1 僅支援 Number）
    SetVar,
    GetVar,
    Compare,
}
