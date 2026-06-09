namespace SkillCreator.World;

// 遊戲內時間系統
// 設計規格（plan-ability-system.md §9-A）：
//   1 現實秒  = 20 ticks（能力效果計時基準）
//   1 遊戲日  = 28,800 ticks = 24 現實分鐘（唯一鎖定值）
//
// 待定（世界觀設計後補）：遊戲秒 tick 數、月份天數、年份長度
public static class GameClock
{
    public const int TicksPerSecond = 20;
    public const int TicksPerDay    = 28_800; // 24 現實分鐘

    private static float _accumulator = 0f;

    // 自本局開始累積的總 tick 數
    public static long TotalTicks { get; private set; } = 0;

    // 已過天數（0-based）
    public static int DayCount { get; private set; } = 0;

    // 當天進度（0.0 = 日出，0.5 = 正午，1.0 = 隔天日出）
    public static float DayFraction => (TotalTicks % TicksPerDay) / (float)TicksPerDay;

    // Main._Process 每幀呼叫
    public static void Advance(float delta)
    {
        _accumulator += delta * TicksPerSecond;
        int ticks = (int)_accumulator;
        if (ticks == 0) return;
        _accumulator -= ticks;
        TotalTicks   += ticks;
        DayCount      = (int)(TotalTicks / TicksPerDay);
    }

    // 場景重啟時呼叫
    public static void Reset()
    {
        TotalTicks   = 0;
        DayCount     = 0;
        _accumulator = 0f;
    }
}
