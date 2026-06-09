namespace SkillCreator.AbilitySystem;

// 執行安全機制（§10）
public class SafetyGuard
{
    // 每 tick 積木執行上限，防止無限連段卡死
    public const int MaxExecutionsPerTick = 50;

    // RepeatWhile 每次施放的總迭代上限（條件永遠為真時 Fizzle）
    public const int MaxWhileIterations = 500;

    // InvokeSpell 連段最深層數（SpellCaster / SpellRunner 共用）
    public const int MaxComboDepth = 5;

    // 場上存活實體上限（⚠️ 數值待調整）
    public const int MaxEntityCount = 100;

    // 檢查 MP 是否足夠（MP 熔斷）
    public static bool HasMp(float currentMp, float cost) => currentMp >= cost;

    // Proc Mask：同一刻印在同一連鎖中只觸發一次（§10）
    private readonly HashSet<string> _procMaskThisChain = new();

    public bool TryProc(string engraveId)
    {
        if (_procMaskThisChain.Contains(engraveId)) return false;
        _procMaskThisChain.Add(engraveId);
        return true;
    }

    public void ResetProcMask() => _procMaskThisChain.Clear();

    // 場景唯一次刻印計數器
    private readonly Dictionary<string, int> _sceneUseCounts = new();

    public bool TryUseSpell(string spellName, int limit)
    {
        if (limit <= 0) return true; // 0 = 無限制
        _sceneUseCounts.TryGetValue(spellName, out int used);
        if (used >= limit) return false;
        _sceneUseCounts[spellName] = used + 1;
        return true;
    }

    public void ResetSceneCounts() => _sceneUseCounts.Clear();
}
