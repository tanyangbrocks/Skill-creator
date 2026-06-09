namespace SkillCreator.AbilitySystem.VM;

using System.Linq;
using SkillCreator.AbilitySystem;
using SkillCreator.World;

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

        // Sleep 幀計時：每幀遞減一次
        if (ctx.State == ExecutionState.WaitingFrames)
        {
            ctx.WaitFramesRemaining--;
            if (ctx.WaitFramesRemaining > 0) return true;
            ctx.State = ExecutionState.Running;
            ctx.PC++;
        }

        // OnReceive 等待：每幀頂部重新檢查 EventBus
        if (ctx.State == ExecutionState.WaitingSignal)
        {
            if (ctx.WaitingSignalName == null ||
                !EventBus.HasSignal(ctx.WaitingSignalName)) return true;
            ctx.State            = ExecutionState.Running;
            ctx.WaitingSignalName = null;
            ctx.PC++;   // 跳過已通過的 OnReceive 指令
        }

        while (ctx.State == ExecutionState.Running && ctx.PC < ctx.Code.Count)
        {
            if (_executionsThisTick >= SafetyGuard.MaxExecutionsPerTick)
                return false;

            Execute(ctx.Code[ctx.PC], ctx);
            _executionsThisTick++;

            if (ctx.IsFinished)                           break;
            if (ctx.State == ExecutionState.Waiting)      break; // Wait：PC 留在原位，下幀由頂部前進
            if (ctx.PendingInvokeSpell != null  ||
                ctx.PendingInvokeTotem != null  ||
                ctx.PendingEntityDamageId >= 0  ||
                ctx.PendingEntityMoveId   >= 0)           break;
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
                float  val    = ResolveNum(instr, "value", ctx); // 支援字面數值與變數名
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

            case OpCode.WhileCheck:
            {
                int whilePc = ctx.PC;
                ctx.WhileIterCounters.TryGetValue(whilePc, out int iters);
                iters++;
                if (iters > SafetyGuard.MaxWhileIterations)
                {
                    ctx.WhileIterCounters.Remove(whilePc);
                    ctx.State = ExecutionState.Fizzled;
                    break;
                }
                if (EvalCondition(instr, ctx))
                {
                    ctx.WhileIterCounters[whilePc] = iters;
                    ctx.PC++;
                }
                else
                {
                    ctx.WhileIterCounters.Remove(whilePc); // 迴圈結束，清理計數器
                    ctx.PC = Param<int>(instr, "__loopEnd", ctx.PC + 1);
                }
                break;
            }

            case OpCode.StoreCompare:
            {
                float val     = EvalCompare(instr, ctx) ? 1f : 0f;
                string varName = Param<string>(instr, "resultVar", "");
                bool   global  = Param<bool>  (instr, "global",    false);
                if (!string.IsNullOrEmpty(varName))
                {
                    if (global) ExecutionContext.GlobalVars[varName] = val;
                    else        ctx.InstanceVars[varName] = val;
                }
                ctx.PC++;
                break;
            }

            case OpCode.ForEachStart:
            {
                if (ctx.EntityQuery == null)
                {
                    ctx.PC = Param<int>(instr, "__loopEnd", ctx.PC + 1);
                    break;
                }
                float radius = Param<float>(instr, "radius", 5f);
                var entities = ctx.EntityQuery(radius);
                if (entities.Count == 0)
                {
                    ctx.PC = Param<int>(instr, "__loopEnd", ctx.PC + 1);
                    break;
                }
                ctx.EntityIterators.Push(new EntityIterState(entities, 0));
                SetEntityVars(ctx, entities[0]);
                ctx.CurrentIterEntity = entities[0];
                ctx.PC++;
                break;
            }

            case OpCode.ForEachStep:
            {
                if (ctx.EntityIterators.Count == 0) { ctx.PC++; break; }
                var state = ctx.EntityIterators.Pop();
                int next  = state.CurrentIndex + 1;
                if (next < state.Entities.Count)
                {
                    ctx.EntityIterators.Push(new EntityIterState(state.Entities, next));
                    SetEntityVars(ctx, state.Entities[next]);
                    ctx.CurrentIterEntity = state.Entities[next];
                    ctx.PC = Param<int>(instr, "__loopStart", ctx.PC + 1);
                }
                else
                {
                    ctx.CurrentIterEntity = null;
                    ctx.PC++;
                }
                break;
            }

            case OpCode.QueryNearest:
            {
                float  radius    = Param<float> (instr, "radius",    5f);
                string resultVar = Param<string>(instr, "resultVar", "nearest");
                if (ctx.EntityQuery != null)
                {
                    var list = ctx.EntityQuery(radius); // delegate 回傳已按距離排序
                    if (list.Count > 0)
                    {
                        var e = list[0];
                        ctx.InstanceVars[$"{resultVar}.found"]  = 1f;
                        ctx.InstanceVars[$"{resultVar}.x"]      = e.Position.X;
                        ctx.InstanceVars[$"{resultVar}.y"]      = e.Position.Y;
                        ctx.InstanceVars[$"{resultVar}.hp"]     = e.Hp;
                        ctx.InstanceVars[$"{resultVar}.maxhp"]  = e.MaxHp;
                    }
                    else
                    {
                        ctx.InstanceVars[$"{resultVar}.found"] = 0f;
                    }
                }
                ctx.PC++;
                break;
            }

            case OpCode.GetEntityProp:
            {
                string prop      = Param<string>(instr, "property",  "hp");
                string resultVar = Param<string>(instr, "resultVar", "");
                if (!string.IsNullOrEmpty(resultVar) && ctx.CurrentIterEntity.HasValue)
                {
                    float val = prop switch
                    {
                        "hp"    => ctx.CurrentIterEntity.Value.Hp,
                        "maxhp" => ctx.CurrentIterEntity.Value.MaxHp,
                        "x"     => ctx.CurrentIterEntity.Value.Position.X,
                        "y"     => ctx.CurrentIterEntity.Value.Position.Y,
                        _       => 0f,
                    };
                    ctx.InstanceVars[resultVar] = val;
                }
                ctx.PC++;
                break;
            }

            case OpCode.StoreEntityProp:
            {
                if (!ctx.CurrentIterEntity.HasValue) { ctx.PC++; break; }
                string prop = Param<string>(instr, "property", "hp");
                var ent = ctx.CurrentIterEntity.Value;
                switch (prop)
                {
                    case "hp":
                    {
                        float damage = Param<float>(instr, "damage", 0f);
                        if (damage > 0f)
                        {
                            ctx.PendingEntityDamageId     = ent.Id;
                            ctx.PendingEntityDamageAmount = damage;
                        }
                        break;
                    }
                    case "x":
                    {
                        float newX = ResolveNum(instr, "value", ctx);
                        ctx.PendingEntityMoveId  = ent.Id;
                        ctx.PendingEntityMovePos = new GridPos((int)newX, ent.Position.Y);
                        break;
                    }
                    case "y":
                    {
                        float newY = ResolveNum(instr, "value", ctx);
                        ctx.PendingEntityMoveId  = ent.Id;
                        ctx.PendingEntityMovePos = new GridPos(ent.Position.X, (int)newY);
                        break;
                    }
                }
                ctx.PC++;
                break;
            }

            case OpCode.ListCreate:
            {
                string name   = Param<string>(instr, "name",   "");
                bool   global = Param<bool>  (instr, "global", false);
                if (!string.IsNullOrEmpty(name))
                {
                    var dict = global ? ExecutionContext.GlobalLists : ctx.InstanceLists;
                    dict[name] = new List<float>();
                }
                ctx.PC++;
                break;
            }

            case OpCode.ListAppend:
            {
                string name   = Param<string>(instr, "name",   "");
                float  value  = ResolveNum(instr, "value", ctx);
                bool   global = Param<bool>  (instr, "global", false);
                if (!string.IsNullOrEmpty(name))
                    ctx.GetOrCreateList(name, global).Add(value);
                ctx.PC++;
                break;
            }

            case OpCode.ListPop:
            {
                string name      = Param<string>(instr, "name",      "");
                string resultVar = Param<string>(instr, "resultVar", "");
                bool   global    = Param<bool>  (instr, "global",    false);
                if (!string.IsNullOrEmpty(name))
                {
                    var list = ctx.GetList(name, global);
                    if (list != null && list.Count > 0)
                    {
                        float v = list[^1];
                        list.RemoveAt(list.Count - 1);
                        if (!string.IsNullOrEmpty(resultVar))
                            ctx.InstanceVars[resultVar] = v;
                    }
                }
                ctx.PC++;
                break;
            }

            case OpCode.ListGet:
            {
                string name      = Param<string>(instr, "name",      "");
                float  rawIdx    = ResolveNum(instr, "index", ctx);
                string resultVar = Param<string>(instr, "resultVar", "");
                bool   global    = Param<bool>  (instr, "global",    false);
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(resultVar))
                {
                    var list = ctx.GetList(name, global);
                    if (list != null)
                    {
                        int idx = (int)rawIdx - 1; // 1-based → 0-based
                        if (idx >= 0 && idx < list.Count)
                            ctx.InstanceVars[resultVar] = list[idx];
                    }
                }
                ctx.PC++;
                break;
            }

            case OpCode.Broadcast:
            {
                string signal = Param<string>(instr, "signal", "");
                EventBus.Broadcast(signal);
                ctx.PC++;
                break;
            }

            case OpCode.OnReceive:
            {
                string signal = Param<string>(instr, "signal", "");
                if (string.IsNullOrEmpty(signal) || EventBus.HasSignal(signal))
                {
                    ctx.PC++;  // 訊號已存在，立即通過
                }
                else
                {
                    ctx.WaitingSignalName = signal;
                    ctx.State = ExecutionState.WaitingSignal;
                    // PC 不前進：等 Step 頂部偵測到訊號後 PC++
                }
                break;
            }

            // ── Group 1：進階控制流 ───────────────────────────────────

            case OpCode.Die:
                ctx.State = ExecutionState.Completed;
                break;

            case OpCode.SleepFrames:
            {
                int frames = Math.Max(1, (int)Param<float>(instr, "frames", 1f));
                ctx.WaitFramesRemaining = frames;
                ctx.State = ExecutionState.WaitingFrames;
                // PC 不前進：等 Step 頂部遞減到 0 後 PC++
                break;
            }

            // ── Group 2：執行追蹤 ─────────────────────────────────────

            case OpCode.ReadExecStat:
            {
                string stat      = Param<string>(instr, "stat",      "");
                string resultVar = Param<string>(instr, "resultVar", "");
                bool   global    = Param<bool>  (instr, "global",    false);
                float val = stat switch
                {
                    "loopcastIndex" => ctx.LoopcastIndex,
                    "successCount"  => ctx.HitTotems.Count,
                    _               => 0f,
                };
                if (!string.IsNullOrEmpty(resultVar))
                {
                    if (global) ExecutionContext.GlobalVars[resultVar] = val;
                    else        ctx.InstanceVars[resultVar] = val;
                }
                ctx.PC++;
                break;
            }

            // ── Group 3：List 補完 ────────────────────────────────────

            case OpCode.ListDequeue:
            {
                string name      = Param<string>(instr, "name",      "");
                string resultVar = Param<string>(instr, "resultVar", "");
                bool   global    = Param<bool>  (instr, "global",    false);
                if (!string.IsNullOrEmpty(name))
                {
                    var list = ctx.GetList(name, global);
                    if (list != null && list.Count > 0)
                    {
                        float v = list[0];
                        list.RemoveAt(0);
                        if (!string.IsNullOrEmpty(resultVar))
                            ctx.InstanceVars[resultVar] = v;
                    }
                }
                ctx.PC++;
                break;
            }

            case OpCode.ListSet:
            {
                string name   = Param<string>(instr, "name",  "");
                float  rawIdx = ResolveNum(instr, "index", ctx);
                float  value  = ResolveNum(instr, "value", ctx);
                bool   global = Param<bool>  (instr, "global", false);
                if (!string.IsNullOrEmpty(name))
                {
                    var list = ctx.GetList(name, global);
                    if (list != null)
                    {
                        int idx = (int)rawIdx - 1; // 1-based → 0-based
                        if (idx >= 0 && idx < list.Count)
                            list[idx] = value;
                    }
                }
                ctx.PC++;
                break;
            }

            case OpCode.ListLength:
            {
                string name      = Param<string>(instr, "name",      "");
                string resultVar = Param<string>(instr, "resultVar", "");
                bool   global    = Param<bool>  (instr, "global",    false);
                if (!string.IsNullOrEmpty(resultVar))
                    ctx.InstanceVars[resultVar] = ctx.GetList(name, global)?.Count ?? 0;
                ctx.PC++;
                break;
            }

            case OpCode.ListContains:
            {
                string name      = Param<string>(instr, "name",      "");
                float  value     = ResolveNum(instr, "value", ctx);
                string resultVar = Param<string>(instr, "resultVar", "");
                bool   global    = Param<bool>  (instr, "global",    false);
                if (!string.IsNullOrEmpty(resultVar))
                {
                    var list  = ctx.GetList(name, global);
                    bool found = list != null && list.Any(v => MathF.Abs(v - value) < 0.0001f);
                    ctx.InstanceVars[resultVar] = found ? 1f : 0f;
                }
                ctx.PC++;
                break;
            }

            case OpCode.ListRemoveAt:
            {
                string name   = Param<string>(instr, "name",  "");
                float  rawIdx = ResolveNum(instr, "index", ctx);
                bool   global = Param<bool>  (instr, "global", false);
                if (!string.IsNullOrEmpty(name))
                {
                    var list = ctx.GetList(name, global);
                    if (list != null)
                    {
                        int idx = (int)rawIdx - 1;
                        if (idx >= 0 && idx < list.Count)
                            list.RemoveAt(idx);
                    }
                }
                ctx.PC++;
                break;
            }

            case OpCode.ListClear:
            {
                string name   = Param<string>(instr, "name",  "");
                bool   global = Param<bool>  (instr, "global", false);
                ctx.GetList(name, global)?.Clear();
                ctx.PC++;
                break;
            }

            // ── Group 4：向量運算 ─────────────────────────────────────

            case OpCode.VecMake:
            {
                string name   = Param<string>(instr, "name",   "v");
                float  x      = ResolveNum(instr, "x", ctx);
                float  y      = ResolveNum(instr, "y", ctx);
                bool   global = Param<bool>(instr, "global", false);
                SetVec(ctx, name, x, y, global);
                ctx.PC++;
                break;
            }

            case OpCode.VecGetComp:
            {
                string name      = Param<string>(instr, "name",      "v");
                string comp      = Param<string>(instr, "comp",      "x");
                string resultVar = Param<string>(instr, "resultVar", "");
                bool   global    = Param<bool>  (instr, "global",    false);
                if (!string.IsNullOrEmpty(resultVar))
                {
                    var (vx, vy) = GetVec(ctx, name, global);
                    ctx.InstanceVars[resultVar] = comp == "y" ? vy : vx;
                }
                ctx.PC++;
                break;
            }

            case OpCode.VecAdd:
            {
                string a = Param<string>(instr, "vecA", "a"), b = Param<string>(instr, "vecB", "b");
                string r = Param<string>(instr, "result", "r");
                bool global = Param<bool>(instr, "global", false);
                var (ax, ay) = GetVec(ctx, a, global);
                var (bx, by) = GetVec(ctx, b, global);
                SetVec(ctx, r, ax + bx, ay + by, global);
                ctx.PC++;
                break;
            }

            case OpCode.VecSub:
            {
                string a = Param<string>(instr, "vecA", "a"), b = Param<string>(instr, "vecB", "b");
                string r = Param<string>(instr, "result", "r");
                bool global = Param<bool>(instr, "global", false);
                var (ax, ay) = GetVec(ctx, a, global);
                var (bx, by) = GetVec(ctx, b, global);
                SetVec(ctx, r, ax - bx, ay - by, global);
                ctx.PC++;
                break;
            }

            case OpCode.VecScale:
            {
                string vec    = Param<string>(instr, "vec",    "v");
                float  scalar = ResolveNum(instr, "scalar", ctx);
                string r      = Param<string>(instr, "result", "r");
                bool   global = Param<bool>  (instr, "global", false);
                var (vx, vy) = GetVec(ctx, vec, global);
                SetVec(ctx, r, vx * scalar, vy * scalar, global);
                ctx.PC++;
                break;
            }

            case OpCode.VecNegate:
            {
                string vec = Param<string>(instr, "vec",    "v");
                string r   = Param<string>(instr, "result", "r");
                bool global = Param<bool>(instr, "global", false);
                var (vx, vy) = GetVec(ctx, vec, global);
                SetVec(ctx, r, -vx, -vy, global);
                ctx.PC++;
                break;
            }

            case OpCode.VecNorm:
            {
                string vec = Param<string>(instr, "vec",    "v");
                string r   = Param<string>(instr, "result", "r");
                bool global = Param<bool>(instr, "global", false);
                var (vx, vy) = GetVec(ctx, vec, global);
                float len = MathF.Sqrt(vx * vx + vy * vy);
                SetVec(ctx, r, len > 0.0001f ? vx / len : 0f,
                              len > 0.0001f ? vy / len : 0f, global);
                ctx.PC++;
                break;
            }

            case OpCode.VecLength:
            {
                string vec       = Param<string>(instr, "vec",       "v");
                string resultVar = Param<string>(instr, "resultVar", "");
                bool   global    = Param<bool>  (instr, "global",    false);
                if (!string.IsNullOrEmpty(resultVar))
                {
                    var (vx, vy) = GetVec(ctx, vec, global);
                    ctx.InstanceVars[resultVar] = MathF.Sqrt(vx * vx + vy * vy);
                }
                ctx.PC++;
                break;
            }

            case OpCode.VecDot:
            {
                string a = Param<string>(instr, "vecA", "a"), b = Param<string>(instr, "vecB", "b");
                string resultVar = Param<string>(instr, "resultVar", "");
                bool global = Param<bool>(instr, "global", false);
                if (!string.IsNullOrEmpty(resultVar))
                {
                    var (ax, ay) = GetVec(ctx, a, global);
                    var (bx, by) = GetVec(ctx, b, global);
                    ctx.InstanceVars[resultVar] = ax * bx + ay * by;
                }
                ctx.PC++;
                break;
            }

            case OpCode.VecCross:
            {
                string a = Param<string>(instr, "vecA", "a"), b = Param<string>(instr, "vecB", "b");
                string resultVar = Param<string>(instr, "resultVar", "");
                bool global = Param<bool>(instr, "global", false);
                if (!string.IsNullOrEmpty(resultVar))
                {
                    var (ax, ay) = GetVec(ctx, a, global);
                    var (bx, by) = GetVec(ctx, b, global);
                    ctx.InstanceVars[resultVar] = ax * by - ay * bx;
                }
                ctx.PC++;
                break;
            }

            case OpCode.VecFromEntity:
            {
                string r    = Param<string>(instr, "result", "e_pos");
                bool global = Param<bool>  (instr, "global", false);
                if (ctx.CurrentIterEntity.HasValue)
                {
                    var e = ctx.CurrentIterEntity.Value;
                    SetVec(ctx, r, e.Position.X, e.Position.Y, global);
                }
                else
                {
                    ctx.InstanceVars.TryGetValue("_e.x", out float ex);
                    ctx.InstanceVars.TryGetValue("_e.y", out float ey);
                    SetVec(ctx, r, ex, ey, global);
                }
                ctx.PC++;
                break;
            }

            case OpCode.GetFocalPoint:
            {
                string r    = Param<string>(instr, "result", "focal");
                bool global = Param<bool>  (instr, "global", false);
                var fp = ctx.FocalPointQuery?.Invoke() ?? new GridPos(0, 0);
                SetVec(ctx, r, fp.X, fp.Y, global);
                ctx.PC++;
                break;
            }

            case OpCode.Raycast:
            {
                string startVec  = Param<string>(instr, "startVec",  "pos");
                string dirVec    = Param<string>(instr, "dirVec",    "dir");
                float  maxDist   = ResolveNum(instr, "maxDist", ctx);
                string resultVec = Param<string>(instr, "resultVec", "ray");
                bool   global    = Param<bool>  (instr, "global",    false);

                if (ctx.RaycastQuery != null)
                {
                    var (sx, sy) = GetVec(ctx, startVec, global);
                    var (dx, dy) = GetVec(ctx, dirVec,   global);
                    var start    = new GridPos((int)sx, (int)sy);
                    var (hit, matId, didHit) = ctx.RaycastQuery(start, dx, dy, maxDist);
                    SetVec(ctx, resultVec, hit.X, hit.Y, global);
                    ctx.InstanceVars[$"{resultVec}.hit"] = didHit ? 1f : 0f;
                    ctx.InstanceVars[$"{resultVec}.mat"] = matId;
                }
                ctx.PC++;
                break;
            }
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
            // varBool：讀取 StoreCompare 寫入的 0/1 浮點變數
            "varBool"     => ResolveNum(instr, "varName", ctx) != 0f,
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

    private static float ResolveNum(Instruction instr, string key, ExecutionContext ctx)
    {
        object? val = instr.Params.GetValueOrDefault(key);
        if (val is float f) return f;
        if (val is int   i) return i;
        if (val is string s)
        {
            // UI 的 LineEdit 以字串存值，先嘗試解析字面數值
            if (float.TryParse(s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float parsed))
                return parsed;
            // 否則視為變數名
            if (ctx.InstanceVars.TryGetValue(s, out float iv))           return iv;
            if (ExecutionContext.GlobalVars.TryGetValue(s, out float gv)) return gv;
        }
        return 0f;
    }

    // ── ForEach 輔助 ─────────────────────────────────────────────────

    private static void SetEntityVars(ExecutionContext ctx, EntityInfo e)
    {
        ctx.InstanceVars["_e.x"]     = e.Position.X;
        ctx.InstanceVars["_e.y"]     = e.Position.Y;
        ctx.InstanceVars["_e.hp"]    = e.Hp;
        ctx.InstanceVars["_e.maxhp"] = e.MaxHp;
        ctx.InstanceVars["_e.idx"]   = e.Id;
    }

    // ── 向量輔助 ─────────────────────────────────────────────────────

    private static (float x, float y) GetVec(ExecutionContext ctx, string name, bool global)
    {
        var vars = global ? ExecutionContext.GlobalVars : ctx.InstanceVars;
        vars.TryGetValue($"{name}.x", out float x);
        vars.TryGetValue($"{name}.y", out float y);
        return (x, y);
    }

    private static void SetVec(ExecutionContext ctx, string name, float x, float y, bool global)
    {
        if (global)
        {
            ExecutionContext.GlobalVars[$"{name}.x"] = x;
            ExecutionContext.GlobalVars[$"{name}.y"] = y;
        }
        else
        {
            ctx.InstanceVars[$"{name}.x"] = x;
            ctx.InstanceVars[$"{name}.y"] = y;
        }
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
