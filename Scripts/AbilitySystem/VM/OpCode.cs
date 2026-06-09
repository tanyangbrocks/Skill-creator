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
}
