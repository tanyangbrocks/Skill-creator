namespace SkillCreator.AbilitySystem;

// ─────────────────────────────────────────────────────────────────────────────
//  GameAction  —  Phase 4 第三層：行動結算層
//
//  所有可被 ActionBus 攔截的遊戲動作基底型別。
//  過濾器簽章：GameAction? Filter(GameAction action)
//    → 回傳 null                    = 取消動作（什麼都不發生）
//    → 回傳相同型別（可修改欄位）    = 允許（以修改後的值執行）
//    → 回傳不同型別                  = 替代執行（以新動作取代原動作）
// ─────────────────────────────────────────────────────────────────────────────
public abstract record GameAction;

/// <summary>即將對敵人實體造成傷害（已傳入原始值，防禦計算在 PlayerController.TakeDamage 內）</summary>
public record EntityDamageAction(int EntityId, float Amount) : GameAction;

/// <summary>即將對玩家造成傷害（傳入扣除防禦後的最終值）</summary>
public record PlayerDamageAction(float Amount) : GameAction;

/// <summary>
/// 玩家即將死亡（HP 降至 0）。
/// 過濾器回傳 null → 取消死亡，玩家存活於 1 HP（死亡替代）。
/// 過濾器回傳相同型別 → 允許死亡正常發生。
/// </summary>
public record PlayerDeathAction() : GameAction;
