namespace SkillCreator.AbilitySystem.VM;

using Godot;
using SkillCreator.AbilitySystem.Data;

// 將 BlockNode AST 編譯成扁平指令序列（方便 Wait 在任意深度暫停執行）
public static class SpellCompiler
{
    public static List<Instruction> Compile(List<BlockNode> blocks, SpellArray? spell = null)
    {
        // 建立 totemId → slotRef 對照表（供 BlockType.Totem 積木轉換為 InvokeTotem 指令）
        var tsm = new Dictionary<string, string>();
        if (spell != null)
        {
            for (int i = 0; i < spell.Slots.Count; i++)
            {
                var s = spell.Slots[i];
                if (!s.IsEmpty && s.Totem != null)
                    tsm[s.Totem.Id] = string.IsNullOrEmpty(s.Name) ? $"slot_{i}" : s.Name;
            }
        }

        var code = new List<Instruction>();
        EmitList(blocks, code, tsm);
        return code;
    }

    private static void EmitList(IEnumerable<BlockNode> blocks, List<Instruction> code,
                                  Dictionary<string, string> tsm)
    {
        foreach (var b in blocks)
            EmitBlock(b, code, tsm);
    }

    private static void EmitBlock(BlockNode block, List<Instruction> code,
                                   Dictionary<string, string> tsm)
    {
        switch (block.Type)
        {
            case BlockType.Wait:
            case BlockType.SetVar:
            case BlockType.InvokeTotem:
            case BlockType.InvokeSpell:
                code.Add(new Instruction(MapSimple(block.Type), new(block.Params)));
                break;

            case BlockType.If:
            {
                // ── 條件跳轉（目標暫時為 0，稍後 patch）──
                var jif = new Instruction(OpCode.JumpIfFalse, new(block.Params));
                code.Add(jif);
                EmitList(block.ThenBranch, code, tsm);
                // ── 跳過 else 分支 ──
                var jmp = new Instruction(OpCode.Jump);
                code.Add(jmp);
                // ── patch jif → else 起點 ──
                jif.Params["__target"] = (object?)code.Count;
                EmitList(block.ElseBranch, code, tsm);
                // ── patch jmp → 結尾 ──
                jmp.Params["__target"] = (object?)code.Count;
                break;
            }

            case BlockType.Evaluate:
            {
                // 無 else 分支的條件容器
                var jif = new Instruction(OpCode.JumpIfFalse, new(block.Params));
                code.Add(jif);
                EmitList(block.ThenBranch, code, tsm);
                jif.Params["__target"] = (object?)code.Count;
                break;
            }

            case BlockType.Die:
                code.Add(new Instruction(OpCode.Die));
                break;

            case BlockType.Sleep:
                code.Add(new Instruction(OpCode.SleepFrames, new(block.Params)));
                break;

            case BlockType.LoopcastIndex:
                code.Add(new Instruction(OpCode.ReadExecStat,
                    new(block.Params) { ["stat"] = (object?)"loopcastIndex" }));
                break;

            case BlockType.SuccessCount:
                code.Add(new Instruction(OpCode.ReadExecStat,
                    new(block.Params) { ["stat"] = (object?)"successCount" }));
                break;

            case BlockType.RepeatN:
            {
                code.Add(new Instruction(OpCode.RepeatPush, new(block.Params)));
                int loopStart = code.Count;
                EmitList(block.LoopBody, code, tsm);
                code.Add(new Instruction(OpCode.RepeatStep,
                    new() { ["__loopStart"] = (object?)loopStart }));
                break;
            }

            case BlockType.RepeatWhile:
            {
                int loopStart = code.Count;
                var whileCheck = new Instruction(OpCode.WhileCheck, new(block.Params));
                code.Add(whileCheck);
                EmitList(block.LoopBody, code, tsm);
                code.Add(new Instruction(OpCode.Jump,
                    new() { ["__target"] = (object?)loopStart }));
                // patch：條件不成立時跳到迴圈結尾
                whileCheck.Params["__loopEnd"] = (object?)code.Count;
                break;
            }

            case BlockType.Compare:
                code.Add(new Instruction(OpCode.StoreCompare, new(block.Params)));
                break;

            case BlockType.ForEachNearby:
            {
                int loopStart = code.Count;
                var forStart = new Instruction(OpCode.ForEachStart, new(block.Params));
                code.Add(forStart);
                EmitList(block.LoopBody, code, tsm);
                code.Add(new Instruction(OpCode.ForEachStep,
                    new() { ["__loopStart"] = (object?)loopStart }));
                forStart.Params["__loopEnd"] = (object?)code.Count;
                break;
            }

            case BlockType.QueryNearest:
                code.Add(new Instruction(OpCode.QueryNearest, new(block.Params)));
                break;

            case BlockType.GetEntityProp:
                code.Add(new Instruction(OpCode.GetEntityProp, new(block.Params)));
                break;

            case BlockType.SetEntityProp:
                code.Add(new Instruction(OpCode.StoreEntityProp, new(block.Params)));
                break;

            case BlockType.ListCreate:
                code.Add(new Instruction(OpCode.ListCreate, new(block.Params)));
                break;

            case BlockType.ListAppend:
                code.Add(new Instruction(OpCode.ListAppend, new(block.Params)));
                break;

            case BlockType.ListPop:
                code.Add(new Instruction(OpCode.ListPop, new(block.Params)));
                break;

            case BlockType.ListGet:
                code.Add(new Instruction(OpCode.ListGet, new(block.Params)));
                break;

            case BlockType.ListDequeue:
                code.Add(new Instruction(OpCode.ListDequeue, new(block.Params)));
                break;

            case BlockType.ListSet:
                code.Add(new Instruction(OpCode.ListSet, new(block.Params)));
                break;

            case BlockType.ListLength:
                code.Add(new Instruction(OpCode.ListLength, new(block.Params)));
                break;

            case BlockType.ListContains:
                code.Add(new Instruction(OpCode.ListContains, new(block.Params)));
                break;

            case BlockType.ListRemoveAt:
                code.Add(new Instruction(OpCode.ListRemoveAt, new(block.Params)));
                break;

            case BlockType.ListClear:
                code.Add(new Instruction(OpCode.ListClear, new(block.Params)));
                break;

            case BlockType.Broadcast:
            case BlockType.BroadcastAndWait: // 本版行為同 Broadcast
                code.Add(new Instruction(OpCode.Broadcast, new(block.Params)));
                break;

            case BlockType.OnReceive:
                code.Add(new Instruction(OpCode.OnReceive, new(block.Params)));
                break;

            // ── Group 4：向量運算 ─────────────────────────────────────

            case BlockType.VecMake:
            case BlockType.VecGetComp:
            case BlockType.VecAdd:
            case BlockType.VecSub:
            case BlockType.VecScale:
            case BlockType.VecNegate:
            case BlockType.VecNorm:
            case BlockType.VecLength:
            case BlockType.VecDot:
            case BlockType.VecCross:
            case BlockType.VecFromEntity:
                code.Add(new Instruction(MapVec(block.Type), new(block.Params)));
                break;

            case BlockType.Raycast:
                code.Add(new Instruction(OpCode.Raycast, new(block.Params)));
                break;

            case BlockType.FocalPoint:
                code.Add(new Instruction(OpCode.GetFocalPoint, new(block.Params)));
                break;

            // ── §9-B 戰鬥統計查詢 ──────────────────────────────────────
            case BlockType.GetBattleStat:
                code.Add(new Instruction(OpCode.ReadBattleStat, new(block.Params)));
                break;

            // ── §7 被動觸發條件 ────────────────────────────────────────
            case BlockType.DetectHpThreshold:
            {
                float pct = block.Params.TryGetValue("percent", out var pv) && pv is float f ? f : 30f;
                code.Add(new Instruction(OpCode.WaitCondition, new()
                {
                    ["condKey"]   = (object?)"hpPct",
                    ["threshold"] = (object?)(pct / 100f),
                }));
                break;
            }
            case BlockType.DetectMpThreshold:
            {
                float pct = block.Params.TryGetValue("percent", out var pv) && pv is float f ? f : 30f;
                code.Add(new Instruction(OpCode.WaitCondition, new()
                {
                    ["condKey"]   = (object?)"mpPct",
                    ["threshold"] = (object?)(pct / 100f),
                }));
                break;
            }
            case BlockType.DetectHitReceived:
                code.Add(new Instruction(OpCode.WaitCondition, new()
                {
                    ["condKey"]   = (object?)"damaged",
                    ["threshold"] = (object?)0f,
                }));
                break;

            // ── 邊緣觸發 ───────────────────────────────────────────────
            case BlockType.RisingEdge:
                code.Add(new Instruction(OpCode.EdgeRising, new(block.Params)));
                break;

            case BlockType.FallingEdge:
                code.Add(new Instruction(OpCode.EdgeFalling, new(block.Params)));
                break;

            case BlockType.SinglePulse:
            {
                var jif = new Instruction(OpCode.EdgeSinglePulse, new(block.Params));
                code.Add(jif);
                EmitList(block.ThenBranch, code, tsm);
                jif.Params["__target"] = (object?)code.Count;
                break;
            }

            case BlockType.DetectEntityEnter:
            {
                float radius = block.Params.TryGetValue("radius", out var rv) && rv is float rf ? rf : 5f;
                code.Add(new Instruction(OpCode.WaitCondition, new()
                {
                    ["condKey"]   = (object?)"entityInRange",
                    ["threshold"] = (object?)radius,
                }));
                break;
            }

            // ── 布林變數 ───────────────────────────────────────────────
            case BlockType.SetVarBool:
            {
                float val = block.Params.TryGetValue("value", out var bv) &&
                            bv is string sv && sv.ToLower() == "true" ? 1f : 0f;
                code.Add(new Instruction(OpCode.SetVar, new()
                {
                    ["name"]   = block.Params.GetValueOrDefault("name",   (object?)""),
                    ["value"]  = (object?)val,
                    ["global"] = block.Params.GetValueOrDefault("global", (object?)false),
                }));
                break;
            }
            case BlockType.GetVarBool:
            {
                // 複製 name 變數值到 resultVar（ResolveNum 支援字串→變數查詢）
                code.Add(new Instruction(OpCode.SetVar, new()
                {
                    ["name"]   = block.Params.GetValueOrDefault("resultVar",
                                     block.Params.GetValueOrDefault("name", (object?)"_bool")),
                    ["value"]  = block.Params.GetValueOrDefault("name",   (object?)""),
                    ["global"] = block.Params.GetValueOrDefault("global", (object?)false),
                }));
                break;
            }

            // ── 敵人計數查詢 ───────────────────────────────────────────
            case BlockType.QueryNear:
                code.Add(new Instruction(OpCode.QueryNearCount, new(block.Params)));
                break;

            // ── 隨機選擇（ThenBranch=選項A，ElseBranch=選項B）─────────
            case BlockType.RandomChoice:
            {
                var rj = new Instruction(OpCode.RandomJump, new());
                code.Add(rj);
                int addrA = code.Count;
                EmitList(block.ThenBranch, code, tsm);
                var jmpA = new Instruction(OpCode.Jump);
                code.Add(jmpA);
                int addrB = code.Count;
                EmitList(block.ElseBranch, code, tsm);
                rj.Params["__target_0"] = (object?)addrA;
                rj.Params["__target_1"] = (object?)addrB;
                rj.Params["count"]      = (object?)2;
                jmpA.Params["__target"] = (object?)code.Count;
                break;
            }

            // ── 任務計數器 ─────────────────────────────────────────────
            case BlockType.TaskCounterSet:
                code.Add(new Instruction(OpCode.TaskCounterSet, new(block.Params)));
                break;
            case BlockType.TaskCounterAdd:
                code.Add(new Instruction(OpCode.TaskCounterAdd, new(block.Params)));
                break;
            case BlockType.TaskCounterGet:
                code.Add(new Instruction(OpCode.TaskCounterGet, new(block.Params)));
                break;
            case BlockType.TaskCounterReset:
                code.Add(new Instruction(OpCode.TaskCounterReset, new(block.Params)));
                break;
            case BlockType.TaskCounterOnReach:
            {
                var checkInstr = new Instruction(OpCode.TaskCounterOnReach, new(block.Params));
                code.Add(checkInstr);
                EmitList(block.ThenBranch, code, tsm);
                checkInstr.Params["__target"] = (object?)code.Count;
                break;
            }

            // ── Phase 4：行動攔截積木 ─────────────────────────────────
            case BlockType.DamageShield:
            {
                var p = new Dictionary<string, object?>(block.Params);
                p["filterType"] = "damageShield";
                code.Add(new Instruction(OpCode.RegisterFilter, p));
                break;
            }
            case BlockType.DeathGuard:
            {
                var p = new Dictionary<string, object?>(block.Params);
                p["filterType"] = "deathGuard";
                code.Add(new Instruction(OpCode.RegisterFilter, p));
                break;
            }

            // ── Phase 4：狀態快照（S-12）─────────────────────────────
            case BlockType.Anchor:
                code.Add(new Instruction(OpCode.AnchorSnapshot, block.Params));
                break;
            case BlockType.Rollback:
                code.Add(new Instruction(OpCode.RollbackSnapshot, block.Params));
                break;

            // ── 補完實作（第二批）────────────────────────────────────────
            case BlockType.Discard:
            case BlockType.EffectLabel:
                // 純 UI 提示積木，不產生任何指令
                break;

            case BlockType.GetVar:
            {
                // 讀取具名變數 → 寫入 resultVar（複用 SetVar；ResolveNum 支援字串→變數查詢）
                code.Add(new Instruction(OpCode.SetVar, new()
                {
                    ["name"]   = block.Params.GetValueOrDefault("resultVar",
                                     block.Params.GetValueOrDefault("name", (object?)"_var")),
                    ["value"]  = block.Params.GetValueOrDefault("name", (object?)""),
                    ["global"] = block.Params.GetValueOrDefault("global", (object?)false),
                }));
                break;
            }

            case BlockType.GetComboCount:
                code.Add(new Instruction(OpCode.ReadExecStat,
                    new(block.Params) { ["stat"] = (object?)"comboCount" }));
                break;

            case BlockType.AlternateTrigger:
            {
                // 第 0, 2, 4... 次 → ThenBranch；第 1, 3, 5... 次 → ElseBranch
                var altJump = new Instruction(OpCode.AlternateJump, new());
                code.Add(altJump);
                int evenStart = code.Count;
                EmitList(block.ThenBranch, code, tsm);
                var jmpEven = new Instruction(OpCode.Jump);
                code.Add(jmpEven);
                int oddStart = code.Count;
                EmitList(block.ElseBranch, code, tsm);
                altJump.Params["__target_even"] = (object?)evenStart;
                altJump.Params["__target_odd"]  = (object?)oddStart;
                jmpEven.Params["__target"]      = (object?)code.Count;
                break;
            }

            case BlockType.SetActivationInstant:
                code.Add(new Instruction(OpCode.SetActivationMode, new() { ["mode"] = (object?)0 }));
                break;
            case BlockType.SetActivationDeclare:
                code.Add(new Instruction(OpCode.SetActivationMode, new() { ["mode"] = (object?)1 }));
                break;
            case BlockType.SetActivationSustained:
                code.Add(new Instruction(OpCode.SetActivationMode, new() { ["mode"] = (object?)2 }));
                break;

            // ── Direction A：圖騰／刻印宣告積木 ──────────────────────────
            case BlockType.Totem:
            {
                // 查 totemId → slotRef，emit InvokeTotem（使圖騰積木兼具宣告與執行語義）
                var id = block.Params.TryGetValue("totemId", out var v) && v is string s ? s : "";
                if (tsm.TryGetValue(id, out var slotRef))
                    code.Add(new Instruction(OpCode.InvokeTotem,
                        new() { ["totemName"] = (object?)slotRef }));
                break;
            }
            case BlockType.Engraving:
                // 純宣告積木（刻印效果由 slot/engraving 系統在施放前套用），不產生 VM 指令
                break;

            default:
                GD.PushWarning($"[SpellCompiler] 未處理的 BlockType: {block.Type}");
                break;
        }
    }

    private static OpCode MapSimple(BlockType t) => t switch
    {
        BlockType.Wait        => OpCode.Wait,
        BlockType.SetVar      => OpCode.SetVar,
        BlockType.InvokeTotem => OpCode.InvokeTotem,
        BlockType.InvokeSpell => OpCode.InvokeSpell,
        _                     => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    private static OpCode MapVec(BlockType t) => t switch
    {
        BlockType.VecMake        => OpCode.VecMake,
        BlockType.VecGetComp     => OpCode.VecGetComp,
        BlockType.VecAdd         => OpCode.VecAdd,
        BlockType.VecSub         => OpCode.VecSub,
        BlockType.VecScale       => OpCode.VecScale,
        BlockType.VecNegate      => OpCode.VecNegate,
        BlockType.VecNorm        => OpCode.VecNorm,
        BlockType.VecLength      => OpCode.VecLength,
        BlockType.VecDot         => OpCode.VecDot,
        BlockType.VecCross       => OpCode.VecCross,
        BlockType.VecFromEntity  => OpCode.VecFromEntity,
        _                        => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };
}
