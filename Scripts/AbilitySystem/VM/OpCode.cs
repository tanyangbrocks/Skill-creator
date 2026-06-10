namespace SkillCreator.AbilitySystem.VM;

public enum OpCode
{
    Wait,          // 等待 N 秒（同步執行模式下跳過計時，Phase 3 搭配 SpellRunner 才有效）
    JumpIfFalse,   // If 積木：條件為 false 時跳到 else/end
    Jump,          // If 積木：跳過 else 分支
    SetVar,
    InvokeTotem,
    InvokeSpell,
    RepeatPush,    // RepeatN：把迭代次數推入 LoopCounter 堆疊
    RepeatStep,    // RepeatN：遞減計數，大於 0 則跳回循環起點；否則結束
    WhileCheck,    // RepeatWhile：條件為 false 時跳到 __loopEnd；同時計算迭代安全上限
    StoreCompare,  // Compare 積木：評估比較式，結果以 0/1 存入具名浮點變數
    ForEachStart,  // ForEachNearby：查詢實體，推入迭代器；空則跳到 __loopEnd
    ForEachStep,   // ForEachNearby：前進迭代器，有更多則跳回 __loopStart；否則彈出堆疊
    QueryNearest,  // 查詢半徑內最近實體，結果寫入 "{resultVar}.x/y/hp/maxhp/found"
    GetEntityProp, // 讀當前 ForEach 實體屬性 → 具名變數
    StoreEntityProp, // 對當前 ForEach 實體扣血（PendingEntityDamage → SpellCaster 消費）
    ListCreate,    // 建立具名列表（已存在則清空）
    ListAppend,    // 加入末尾
    ListPop,       // 取出尾部並寫入結果變數
    ListGet,       // 按 1-based 索引讀取並寫入結果變數
    Broadcast,     // 廣播訊號至 EventBus（本幀有效）
    OnReceive,     // 等待訊號：訊號存在則通過，否則進入 WaitingSignal 狀態

    // ── Group 1：進階控制流 ───────────────────────────────────────
    Die,           // 立刻終止法陣（State = Completed）
    SleepFrames,   // 等待 N 幀後繼續（WaitingFrames 狀態）
    // Evaluate 複用 JumpIfFalse（無 else Jump），無需新 OpCode

    // ── Group 2：執行追蹤 ─────────────────────────────────────────
    ReadExecStat,  // 讀取 LoopcastIndex / SuccessCount → resultVar

    // ── Group 3：List 補完 ────────────────────────────────────────
    ListDequeue,   // 取出頭部（index 0）→ resultVar
    ListSet,       // 設定指定 1-based 位置值
    ListLength,    // 列表長度 → resultVar
    ListContains,  // 成員查詢 → 0/1 存入 resultVar
    ListRemoveAt,  // 按 1-based index 移除
    ListClear,     // 清空列表

    // ── Group 4：向量運算（2D，儲存為 name.x / name.y）──────────
    VecMake,       // (x, y) → name.x / name.y
    VecGetComp,    // name.x 或 name.y → resultVar
    VecAdd,        // a + b → result
    VecSub,        // a − b → result
    VecScale,      // v × scalar → result
    VecNegate,     // −v → result
    VecNorm,       // normalize(v) → result
    VecLength,     // |v| → resultVar（純量）
    VecDot,        // a · b → resultVar（純量）
    VecCross,      // a × b (2D) → resultVar（純量：ax*by − ay*bx）
    VecFromEntity, // 當前迭代實體位置 → result
    Raycast,       // DDA 射線：start+dir+maxDist → result.x/y/hit/mat
    GetFocalPoint, // 焦點位置 → result.x / result.y

    // ── §9-B 戰鬥狀態查詢 ─────────────────────────────────────────
    ReadBattleStat,  // 讀取 CombatState 統計數值 → resultVar（castCount/damageDealt/killCount）

    // ── §7 被動反應觸發 ───────────────────────────────────────────
    WaitCondition,   // 暫停直到玩家狀態條件滿足（hpPct/mpPct 低於閾值，或本幀受傷）

    // ── 補完實作 ──────────────────────────────────────────────────
    QueryNearCount,    // 查詢半徑內敵人數量 → resultVar
    RandomJump,        // 隨機跳轉到 N 個目標地址之一（__target_0 / __target_1...）
    EdgeRising,        // 等待條件 false→true（同 WaitingRisingEdge 狀態）
    EdgeFalling,       // 等待條件 true→false（同 WaitingFallingEdge 狀態）
    EdgeSinglePulse,   // 條件首次成立時執行 ThenBranch（__target 指向結尾）
    TaskCounterSet,    // 設定任務計數器值（全域）
    TaskCounterAdd,    // 增加任務計數器
    TaskCounterGet,    // 讀取任務計數器 → resultVar
    TaskCounterOnReach,// 到達閾值一次性觸發，否則跳到 __target
    TaskCounterReset,  // 計數器歸零並解鎖對應 OnReach 鎖定

    // ── Phase 4：行動攔截鉤子 ────────────────────────────────────────
    RegisterFilter,    // 向 ActionBus 登記動作過濾器（DamageShield / DeathGuard）

    // ── Phase 4：狀態快照（S-10）────────────────────────────────────
    AnchorSnapshot,    // 擷取圓形區域快照，壓入 SnapshotManager 棧（param: radius）
    RollbackSnapshot,  // 彈出棧頂快照，還原實體 + Tile + 退還 MP

    // ── 補完實作（第二批）────────────────────────────────────────────
    AlternateJump,     // 奇/偶次呼叫交替跳入 ThenBranch / ElseBranch（__target_even / __target_odd）
    SetActivationMode, // 動態修改本法陣發動類型（mode: 0=Instant, 1=Declare, 2=Sustained）
}
