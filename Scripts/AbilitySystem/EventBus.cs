namespace SkillCreator.AbilitySystem;

// 跨法陣訊號匯流排（Broadcast / OnReceive 積木的後端）
// 訊號為「本幀有效」語意：每幀起始由 Main._Process 呼叫 ClearFrame()，
// 確保所有在同幀廣播的訊號可被同幀的所有 OnReceive 接收。
public static class EventBus
{
    private static readonly HashSet<string> _active = new();

    public static void Broadcast(string signal)
    {
        if (!string.IsNullOrEmpty(signal))
            _active.Add(signal);
    }

    public static bool HasSignal(string signal) =>
        !string.IsNullOrEmpty(signal) && _active.Contains(signal);

    // Main._Process 每幀起始呼叫
    public static void ClearFrame() => _active.Clear();

    // Main._Ready 場景重啟時呼叫（session-level reset）
    public static void ClearAll() => _active.Clear();
}
