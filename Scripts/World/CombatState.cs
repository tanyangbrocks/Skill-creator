namespace SkillCreator.World;

// §9-B 戰鬥狀態系統
// 每局追蹤玩家的 InCombat 狀態與本場戰鬥統計，供全局戰鬥統計積木（§7）使用。
public static class CombatState
{
    public const float OutOfCombatTimeout = 5f; // ⚠️ 待平衡：脫戰判定秒數

    // ── 狀態 ─────────────────────────────────────────────────────────
    public static bool InCombat { get; private set; } = false;
    public static int  BattleId { get; private set; } = 0;  // 每次進入新戰鬥遞增

    // ── 本場統計（進入新場戰鬥時重置）──────────────────────────────
    public static int   CastCount   { get; private set; } = 0;
    public static float DamageDealt { get; private set; } = 0f;
    public static int   KillCount   { get; private set; } = 0;

    // ── 本幀事件（Advance 前清除，WaitingCondition 判斷用）──────────
    public static bool TookDamageThisFrame { get; private set; } = false;

    private static float _idleTimer   = 0f;
    private static int   _nextBattleId = 1;

    // ── 事件通知（由 SpellCaster / PlayerController 呼叫）──────────

    public static void OnSpellCast()
    {
        EnterCombat();
        CastCount++;
        _idleTimer = 0f;
    }

    public static void OnPlayerDealtDamage(float amount)
    {
        EnterCombat();
        DamageDealt += amount;
        _idleTimer = 0f;
    }

    public static void OnPlayerTookDamage()
    {
        EnterCombat();
        TookDamageThisFrame = true;
        _idleTimer = 0f;
    }

    public static void OnEnemyKilled()
    {
        if (InCombat) KillCount++;
    }

    // ── 每幀更新（Main._Process 呼叫）────────────────────────────────

    public static void Advance(float delta)
    {
        TookDamageThisFrame = false;
        if (!InCombat) return;
        _idleTimer += delta;
        if (_idleTimer >= OutOfCombatTimeout) ExitCombat();
    }

    // ── 場景重啟（Main._Ready 呼叫）──────────────────────────────────

    public static void Reset()
    {
        InCombat            = false;
        BattleId            = 0;
        CastCount           = 0;
        DamageDealt         = 0f;
        KillCount           = 0;
        TookDamageThisFrame = false;
        _idleTimer          = 0f;
        // _nextBattleId 不重置，保持跨局唯一性
    }

    // ── 內部 ──────────────────────────────────────────────────────────

    private static void EnterCombat()
    {
        if (InCombat) return;
        InCombat    = true;
        BattleId    = _nextBattleId++;
        CastCount   = 0;
        DamageDealt = 0f;
        KillCount   = 0;
        _idleTimer  = 0f;
    }

    private static void ExitCombat()
    {
        InCombat   = false;
        _idleTimer = 0f;
    }
}
