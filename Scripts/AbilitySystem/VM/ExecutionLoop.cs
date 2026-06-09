namespace SkillCreator.AbilitySystem.VM;

using SkillCreator.AbilitySystem;

public class ExecutionLoop
{
    private readonly SafetyGuard _safety;
    private int _executionsThisTick = 0;

    public ExecutionLoop(SafetyGuard safety) => _safety = safety;
    public void ResetTick() => _executionsThisTick = 0;

    // 執行一步（可跨多個指令，直到 Wait/Pending/完成/預算耗盡）。
    // 回傳 false 表示本幀執行預算耗盡。
    public bool Step(ExecutionContext ctx, float delta)
    {
        if (ctx.IsFinished) return true;

        // Wait 計時：Wait 指令執行時 PC 不前進，等到這裡歸零後才 PC++
        if (ctx.State == ExecutionState.Waiting)
        {
            ctx.WaitRemaining -= delta;
            if (ctx.WaitRemaining > 0f) return true;
            ctx.State = ExecutionState.Running;
            ctx.PC++;   // 跳過已完成的 Wait 指令
        }

        while (ctx.State == ExecutionState.Running && ctx.PC < ctx.Code.Count)
        {
            if (_executionsThisTick >= SafetyGuard.MaxExecutionsPerTick)
                return false;

            Execute(ctx.Code[ctx.PC], ctx);
            _executionsThisTick++;

            if (ctx.IsFinished)                           break;
            if (ctx.State == ExecutionState.Waiting)      break; // Wait：PC 留在原位，下幀由頂部前進
            if (ctx.PendingInvokeSpell != null ||
                ctx.PendingInvokeTotem != null)           break;
        }

        if (ctx.State == ExecutionState.Running && ctx.PC >= ctx.Code.Count)
            ctx.State = ExecutionState.Completed;

        return true;
    }

    // ── 指令分派 ─────────────────────────────────────────────────────
    // 每個 case 負責自行推進 PC（Jump 類設為絕對位址，其餘 PC++）

    private void Execute(Instruction instr, ExecutionContext ctx)
    {
        switch (instr.Op)
        {
            case OpCode.Wait:
            {
                float dur = Param<float>(instr, "duration", 0f);
                if (dur > 0f)
                {
                    ctx.WaitRemaining = dur;
                    ctx.State = ExecutionState.Waiting;
                    // PC 不前進：等 Step 頂部在計時歸零後 PC++
                }
                else ctx.PC++;
                break;
            }

            case OpCode.Jump:
                ctx.PC = Param<int>(instr, "__target", ctx.PC + 1);
                break;

            case OpCode.JumpIfFalse:
                ctx.PC = EvalCondition(instr, ctx)
                    ? ctx.PC + 1
                    : Param<int>(instr, "__target", ctx.PC + 1);
                break;

            case OpCode.RepeatPush:
                ctx.LoopCounters.Push(ClampCount(instr));
                ctx.PC++;
                break;

            case OpCode.RepeatStep:
            {
                if (ctx.LoopCounters.Count == 0) { ctx.PC++; break; }
                int rem = ctx.LoopCounters.Pop() - 1;
                if (rem > 0)
                {
                    ctx.LoopCounters.Push(rem);
                    ctx.PC = Param<int>(instr, "__loopStart", ctx.PC + 1);
                }
                else ctx.PC++;
                break;
            }

            case OpCode.SetVar:
            {
                string name   = Param<string>(instr, "name",   "");
                float  val    = Param<float> (instr, "value",  0f);
                bool   global = Param<bool>  (instr, "global", false);
                if (!string.IsNullOrEmpty(name))
                {
                    if (global) ExecutionContext.GlobalVars[name] = val;
                    else        ctx.InstanceVars[name] = val;
                }
                ctx.PC++;
                break;
            }

            case OpCode.InvokeTotem:
                ctx.PendingInvokeTotem = Param<string>(instr, "totemName", "");
                ctx.PC++;
                break;

            case OpCode.InvokeSpell:
                ctx.PendingInvokeSpell = Param<string>(instr, "spellName", "");
                ctx.PC++;
                break;
        }
    }

    // ── 條件評估 ─────────────────────────────────────────────────────

    private bool EvalCondition(Instruction instr, ExecutionContext ctx)
    {
        return Param<string>(instr, "conditionType", "") switch
        {
            "totemHit"    => ctx.HitTotems.Contains(Param<string>(instr, "totemName", "")),
            "totemDone"   => ctx.DoneTotems.Contains(Param<string>(instr, "totemName", "")),
            "totemFizzle" => ctx.FizzledTotems.Contains(Param<string>(instr, "totemName", "")),
            "compare"     => EvalCompare(instr, ctx),
            _             => false,
        };
    }

    private bool EvalCompare(Instruction instr, ExecutionContext ctx)
    {
        float left  = ResolveNum(instr, "left",  ctx);
        float right = ResolveNum(instr, "right", ctx);
        return Param<string>(instr, "op", "=") switch
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

    private float ResolveNum(Instruction instr, string key, ExecutionContext ctx)
    {
        object? val = instr.Params.GetValueOrDefault(key);
        if (val is float f) return f;
        if (val is int   i) return i;
        if (val is string s)
        {
            if (ctx.InstanceVars.TryGetValue(s, out float iv))          return iv;
            if (ExecutionContext.GlobalVars.TryGetValue(s, out float gv)) return gv;
        }
        return 0f;
    }

    // ── 工具方法 ─────────────────────────────────────────────────────

    private static int ClampCount(Instruction instr)
    {
        object? raw = instr.Params.GetValueOrDefault("count");
        int n = raw switch { float f => (int)f, int i => i, _ => 1 };
        return Math.Clamp(n, 1, 20);
    }

    private static T Param<T>(Instruction instr, string key, T def)
    {
        if (instr.Params.TryGetValue(key, out object? v) && v is T t) return t;
        return def;
    }
}
