namespace SkillCreator.AbilitySystem.VM;

using SkillCreator.AbilitySystem;

public class ExecutionLoop
{
    private readonly SafetyGuard _safety;
    private int _executionsThisTick = 0;

    public ExecutionLoop(SafetyGuard safety)
    {
        _safety = safety;
    }

    public void ResetTick() => _executionsThisTick = 0;

    // 每幀呼叫一次。回傳 false 表示本幀執行預算耗盡（safety guard 觸發）。
    public bool Step(ExecutionContext ctx, float delta)
    {
        if (ctx.IsFinished) return true;

        // Wait 計時
        if (ctx.State == ExecutionState.Waiting)
        {
            ctx.WaitRemaining -= delta;
            if (ctx.WaitRemaining > 0f) return true;
            ctx.State = ExecutionState.Running;
        }

        // 逐步執行頂層積木序列
        while (ctx.State == ExecutionState.Running && ctx.CurrentIndex < ctx.Blocks.Count)
        {
            if (_executionsThisTick >= SafetyGuard.MaxExecutionsPerTick)
                return false;

            var block = ctx.Blocks[ctx.CurrentIndex];
            ExecuteBlock(block, ctx, delta);
            _executionsThisTick++;

            // Wait 會把 State 改為 Waiting，下幀從同一個 index 繼續（不推進）
            if (ctx.State == ExecutionState.Waiting) break;

            ctx.CurrentIndex++;

            // InvokeSpell / InvokeTotem 產生 pending 後暫停，讓呼叫方處理
            if (ctx.PendingInvokeSpell != null || ctx.PendingInvokeTotem != null) break;
        }

        if (ctx.State == ExecutionState.Running && ctx.CurrentIndex >= ctx.Blocks.Count)
            ctx.State = ExecutionState.Completed;

        return true;
    }

    private void ExecuteBlock(BlockNode block, ExecutionContext ctx, float delta)
    {
        switch (block.Type)
        {
            case BlockType.Wait:
                float dur = GetParam<float>(block, "duration", 0f);
                if (dur > 0f)
                {
                    ctx.WaitRemaining = dur;
                    ctx.State = ExecutionState.Waiting;
                }
                break;

            case BlockType.If:
                if (EvaluateCondition(block, ctx))
                    ExecuteSyncList(block.ThenBranch, ctx, delta);
                else
                    ExecuteSyncList(block.ElseBranch, ctx, delta);
                break;

            case BlockType.RepeatN:
                // Phase 1：Wait 不可置於 RepeatN 循環體內（無效）
                int count = Math.Min((int)GetParam<float>(block, "count", 1f), 20);
                for (int i = 0; i < count; i++)
                {
                    ExecuteSyncList(block.LoopBody, ctx, delta);
                    if (ctx.IsFinished) break;
                }
                break;

            case BlockType.SetVar:
                string varName = GetParam<string>(block, "name", "");
                float varValue = GetParam<float>(block, "value", 0f);
                bool isGlobal = GetParam<bool>(block, "global", false);
                if (!string.IsNullOrEmpty(varName))
                {
                    if (isGlobal) ExecutionContext.GlobalVars[varName] = varValue;
                    else ctx.InstanceVars[varName] = varValue;
                }
                break;

            case BlockType.InvokeSpell:
                ctx.PendingInvokeSpell = GetParam<string>(block, "spellName", "");
                break;

            case BlockType.InvokeTotem:
                ctx.PendingInvokeTotem = GetParam<string>(block, "totemName", "");
                break;

            // 其餘積木（EffectLabel、TotemHit 等）為標記或查詢，由呼叫方讀取，不直接執行
        }
    }

    // 同步執行子積木列表（IF 分支、RepeatN 循環體）
    // Phase 1 限制：Wait 在子列表中不生效（不暫停父序列）
    private void ExecuteSyncList(List<BlockNode> blocks, ExecutionContext ctx, float delta)
    {
        foreach (var block in blocks)
        {
            if (ctx.IsFinished || _executionsThisTick >= SafetyGuard.MaxExecutionsPerTick) break;
            ExecuteBlock(block, ctx, delta);
            _executionsThisTick++;
        }
    }

    private bool EvaluateCondition(BlockNode ifBlock, ExecutionContext ctx)
    {
        string condType = GetParam<string>(ifBlock, "conditionType", "");
        return condType switch
        {
            "totemHit"    => ctx.HitTotems.Contains(GetParam<string>(ifBlock, "totemName", "")),
            "totemDone"   => ctx.DoneTotems.Contains(GetParam<string>(ifBlock, "totemName", "")),
            "totemFizzle" => ctx.FizzledTotems.Contains(GetParam<string>(ifBlock, "totemName", "")),
            "compare"     => EvaluateCompare(ifBlock, ctx),
            _             => false,
        };
    }

    private bool EvaluateCompare(BlockNode block, ExecutionContext ctx)
    {
        float left  = ResolveNumber(block, "left", ctx);
        float right = ResolveNumber(block, "right", ctx);
        string op   = GetParam<string>(block, "op", "=");
        return op switch
        {
            ">"  => left > right,
            "<"  => left < right,
            "="  => MathF.Abs(left - right) < 0.0001f,
            "≠"  => MathF.Abs(left - right) >= 0.0001f,
            ">=" => left >= right,
            "<=" => left <= right,
            _    => false,
        };
    }

    private float ResolveNumber(BlockNode block, string key, ExecutionContext ctx)
    {
        object? val = block.Params.GetValueOrDefault(key);
        if (val is float f) return f;
        if (val is int i) return i;
        if (val is string varName)
        {
            if (ctx.InstanceVars.TryGetValue(varName, out float iv)) return iv;
            if (ExecutionContext.GlobalVars.TryGetValue(varName, out float gv)) return gv;
        }
        return 0f;
    }

    private static T GetParam<T>(BlockNode block, string key, T defaultValue)
    {
        if (block.Params.TryGetValue(key, out object? val) && val is T typed)
            return typed;
        return defaultValue;
    }
}
