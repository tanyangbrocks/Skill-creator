namespace SkillCreator.AbilitySystem.VM;

using SkillCreator.World;

public enum ExecutionState
{
    Running,
    Waiting,        // Wait 積木暫停；PC 停在 Wait 指令，由 Step 頂部在計時歸零後前進
    WaitingSignal,  // OnReceive 暫停；等到訊號被廣播後由 Step 頂部恢復
    Completed,
    Fizzled,        // 執行途中目標消失（MP 不退還）
}

public class ExecutionContext
{
    // 編譯後的扁平指令序列
    public List<Instruction> Code { get; }

    // 程式計數器（當前指令索引）
    public int PC { get; set; } = 0;

    public ExecutionState State          { get; set; } = ExecutionState.Running;
    public float          WaitRemaining  { get; set; } = 0f;
    public float          MpConsumed     { get; set; } = 0f;

    // RepeatN 嵌套計數器堆疊（支援巢狀循環）
    public Stack<int> LoopCounters { get; } = new();

    // RepeatWhile 安全計數器：key = WhileCheck 指令的 PC，value = 該迴圈已跑次數
    // 各迴圈獨立計數，支援巢狀 while（每個 WhileCheck 有自己的上限）
    public Dictionary<int, int> WhileIterCounters { get; } = new();

    // ── ForEachNearby / QueryNearest ────────────────────────────
    // 實體查詢代理（SpellCaster / SpellRunner 建立 ctx 時注入）
    public Func<float, List<EntityInfo>>? EntityQuery { get; set; }

    // 迭代器堆疊（支援巢狀 ForEach）
    public Stack<EntityIterState> EntityIterators { get; } = new();

    // 當前迭代實體（InvokeTotem 時 SpellCaster 用來定位效果）
    public EntityInfo? CurrentIterEntity { get; set; }

    // SetEntityProp 扣血：SpellCaster/SpellRunner 消費後清除（儲存敵人穩定 Id，非列表索引）
    public int   PendingEntityDamageId     { get; set; } = -1;
    public float PendingEntityDamageAmount { get; set; } = 0f;

    // SetEntityProp x/y：傳送敵人到指定格座標（SpellCaster/Runner 消費後清除）
    public int      PendingEntityMoveId  { get; set; } = -1;
    public GridPos  PendingEntityMovePos { get; set; }

    // OnReceive 等待中的訊號名稱（WaitingSignal 狀態時有效）
    public string? WaitingSignalName { get; set; }

    // ForEachNearby 迭代時的效果原點覆蓋（由 ConsumeInvokeTotem 設定，取代 player.Position 暫改）
    public GridPos? EffectOriginOverride { get; set; }

    // 實例變數（此次施放獨立）
    public Dictionary<string, float> InstanceVars { get; } = new();

    // 全域變數（跨法陣共享，持久存活）
    public static Dictionary<string, float> GlobalVars { get; } = new();

    // ── 列表變數 ─────────────────────────────────────────────────
    public Dictionary<string, List<float>> InstanceLists { get; } = new();
    public static Dictionary<string, List<float>> GlobalLists { get; } = new();

    public List<float> GetOrCreateList(string name, bool global)
    {
        var dict = global ? GlobalLists : InstanceLists;
        if (!dict.TryGetValue(name, out var list))
        {
            list = new List<float>();
            dict[name] = list;
        }
        return list;
    }

    public List<float>? GetList(string name, bool global)
    {
        var dict = global ? GlobalLists : InstanceLists;
        return dict.TryGetValue(name, out var list) ? list : null;
    }

    // 圖騰執行結果（由呼叫方在圖騰執行後寫入）
    public HashSet<string> HitTotems     { get; } = new();
    public HashSet<string> DoneTotems    { get; } = new();
    public HashSet<string> FizzledTotems { get; } = new();

    // InvokeSpell / InvokeTotem 積木設置此欄位，由呼叫方讀取後處理
    public string? PendingInvokeSpell { get; set; }
    public string? PendingInvokeTotem { get; set; }

    public bool IsFinished => State is ExecutionState.Completed or ExecutionState.Fizzled;

    public ExecutionContext(List<Instruction> code)
    {
        Code = code;
    }
}

// 迭代中的實體快照（引擎無關，Id 對應 Enemy.Id 穩定識別碼）
public readonly record struct EntityInfo(int Id, GridPos Position, float Hp, float MaxHp);

// ForEachNearby 迭代器堆疊元素
public readonly record struct EntityIterState(List<EntityInfo> Entities, int CurrentIndex);
