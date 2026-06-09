namespace SkillCreator.AbilitySystem.VM;

public enum BlockType
{
    // ── 控制流 ────────────────────────────────────────────────────
    If,           // 條件分支
    Evaluate,     // 條件執行容器（條件為真才執行包裹積木；無 else 分支）
    RepeatN,      // 重複 N 次（上限 20）
    RepeatWhile,  // 重複直到條件不符
    ForEachNearby,// 對範圍內每個實體執行一次
    Wait,         // 等待 N 秒（僅頂層支援）
    Sleep,        // 等待 N 幀後繼續（幀計時；法陣保持啟動）
    Die,          // 立刻終止整個法陣（後續積木全部跳過）
    RandomChoice, // 隨機選擇（均等/加權/不重複輪轉）
    SequentialGate,// 序列條件閘門（多階段解鎖）

    // ── 邊緣觸發 ─────────────────────────────────────────────────
    RisingEdge,     // 條件從 false→true 時觸發一次
    FallingEdge,    // 條件從 true→false 時觸發一次
    AlternateTrigger,// 奇/偶次輪流執行 A / B
    SinglePulse,    // 條件滿足只輸出一次（冷卻至條件重置）

    // ── 捨棄 ────────────────────────────────────────────────────
    Discard,        // 捨棄輸出，不產生任何效果

    // ── 效果標記 ─────────────────────────────────────────────────
    EffectLabel,
    OnEffectStart,
    OnEffectEnd,

    // ── 呼叫 ────────────────────────────────────────────────────
    InvokeSpell,  // 連段：發動另一個法陣
    InvokeTotem,  // 觸發法陣內具名圖騰

    // ── 圖騰狀態查詢（If 條件用）───────────────────────────────
    TotemDone,
    TotemHit,
    TotemFizzle,

    // ── 發動類型切換 ──────────────────────────────────────────────
    SetActivationInstant,  // 動態設為即時型
    SetActivationDeclare,  // 動態設為宣告型
    SetActivationSustained,// 動態設為持續生效型

    // ── 數值變數（Number）───────────────────────────────────────
    SetVar,
    GetVar,
    Compare,

    // ── 布林變數 ─────────────────────────────────────────────────
    SetVarBool,
    GetVarBool,

    // ── 列表（List）─────────────────────────────────────────────
    ListCreate,    // 宣告具名有序列表
    ListAppend,    // 加入末尾（Enqueue）
    ListPop,       // 取出尾部（Stack Pop）→ resultVar
    ListDequeue,   // 取出頭部（Queue Dequeue）→ resultVar
    ListGet,       // 按 index 讀取（1-based）→ resultVar
    ListSet,       // 設定指定位置值
    ListLength,    // 列表長度 → resultVar
    ListContains,  // 成員查詢 → 0/1 存入 resultVar
    ListRemoveAt,  // 按 index 移除
    ListClear,     // 清空列表

    // ── 任務計數器 ────────────────────────────────────────────────
    TaskCounterSet,     // 設定/宣告計數器（全域命名）
    TaskCounterAdd,     // 增加計數
    TaskCounterGet,     // 讀取當前值 → 數值
    TaskCounterOnReach, // 到達閾值一次性觸發（自動鎖定不重複）
    TaskCounterReset,   // 歸零，用於多階段里程碑切換

    // ── 實體查詢 ─────────────────────────────────────────────────
    QueryNear,       // 查詢範圍內敵/友實體列表
    QueryNearest,    // 最近/最遠 N 個實體
    GetEntityProp,   // 讀取實體屬性（HP/MP/faction 等）
    SetEntityProp,   // 設定實體屬性

    // ── 廣播通訊 ─────────────────────────────────────────────────
    Broadcast,          // 廣播訊號（fire-and-forget）
    BroadcastAndWait,   // 廣播並等待回應
    OnReceive,          // 接收指定廣播後執行

    // ── 執行追蹤 ──────────────────────────────────────────────────
    LoopcastIndex, // 本法陣從啟動以來執行次數 → Number
    SuccessCount,  // 本次已成功執行的圖騰數（HitTotems.Count）→ Number

    // ── 全局戰鬥統計查詢 ─────────────────────────────────────────
    GetBattleStat, // 查詢施放次數/傷害量/擊殺數等
    GetComboCount, // 讀取當前連擊數

    // ── 向量運算（2D Vector，儲存為 name.x / name.y）────────────
    VecMake,       // (x, y) → 具名向量
    VecGetComp,    // 向量分量（x 或 y）→ 數值變數
    VecAdd,        // vecA + vecB → result
    VecSub,        // vecA − vecB → result
    VecScale,      // vec × scalar → result
    VecNegate,     // −vec → result
    VecNorm,       // normalize(vec) → result
    VecLength,     // |vec| → 數值變數
    VecDot,        // vecA · vecB → 數值變數
    VecCross,      // vecA × vecB (2D scalar) → 數值變數
    VecFromEntity, // 當前迭代實體位置 → 具名向量
    Raycast,       // 從向量位置沿向量方向射出，回傳命中格（hit.x/y/hit/mat）
    FocalPoint,    // 焦點位置（滑鼠世界格座標）→ 具名向量

    // ── 被動反應觸發（自動偵測類）────────────────────────────────
    DetectProjectile,  // 偵測投射物進入範圍
    DetectAttack,      // 偵測敵方蓄力/出手
    DetectHitReceived, // 偵測受到指定類型攻擊
    DetectHpThreshold, // 偵測 HP 低於 N%
    DetectMpThreshold, // 偵測 MP 低於 N%
    DetectEntityEnter, // 偵測指定陣營實體進入範圍
    DetectStatusChange,// 偵測自身獲得/失去狀態

    // ── 觸發時機模式 ─────────────────────────────────────────────
    EndOfChain, // 末端時機：等結算鏈清空後才執行
}
