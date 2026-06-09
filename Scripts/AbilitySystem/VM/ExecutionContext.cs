namespace SkillCreator.AbilitySystem.VM;

public enum ExecutionState
{
    Running,
    Waiting,   // Wait 積木暫停；PC 停在 Wait 指令，由 Step 頂部在計時歸零後前進
    Completed,
    Fizzled,   // 執行途中目標消失（MP 不退還）
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

    // 實例變數（此次施放獨立）
    public Dictionary<string, float> InstanceVars { get; } = new();

    // 全域變數（跨法陣共享，持久存活）
    public static Dictionary<string, float> GlobalVars { get; } = new();

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
