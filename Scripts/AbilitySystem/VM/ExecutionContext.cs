namespace SkillCreator.AbilitySystem.VM;

public enum ExecutionState
{
    Running,
    Waiting,   // Wait 積木暫停，下一幀繼續
    Completed,
    Fizzled,   // 執行途中目標消失（MP 不退還）
}

public class ExecutionContext
{
    // 頂層積木序列（執行入口）
    public List<BlockNode> Blocks { get; }

    // 頂層序列的當前執行位置
    public int CurrentIndex { get; set; } = 0;

    public ExecutionState State { get; set; } = ExecutionState.Running;

    // Wait 積木剩餘等待時間
    public float WaitRemaining { get; set; } = 0f;

    // 已消耗的 MP（用於 Fizzle 時的日誌，不退還）
    public float MpConsumed { get; set; } = 0f;

    // 實例變數（此次施放獨立）
    public Dictionary<string, float> InstanceVars { get; } = new();

    // 全域變數（跨法陣共享，持久存活）
    public static Dictionary<string, float> GlobalVars { get; } = new();

    // 圖騰執行結果（由呼叫方在圖騰執行後寫入）
    public HashSet<string> HitTotems { get; } = new();
    public HashSet<string> DoneTotems { get; } = new();
    public HashSet<string> FizzledTotems { get; } = new();

    // InvokeSpell 積木設置此欄位，由呼叫方讀取並發動連段
    public string? PendingInvokeSpell { get; set; }

    // InvokeTotem 積木設置此欄位，由呼叫方讀取並觸發圖騰
    public string? PendingInvokeTotem { get; set; }

    public bool IsFinished => State is ExecutionState.Completed or ExecutionState.Fizzled;

    public ExecutionContext(List<BlockNode> blocks)
    {
        Blocks = blocks;
    }
}
