namespace SkillCreator.AbilitySystem.VM;

using Godot;

// 將 BlockNode AST 編譯成扁平指令序列（方便 Wait 在任意深度暫停執行）
public static class SpellCompiler
{
    public static List<Instruction> Compile(List<BlockNode> blocks)
    {
        var code = new List<Instruction>();
        EmitList(blocks, code);
        return code;
    }

    private static void EmitList(IEnumerable<BlockNode> blocks, List<Instruction> code)
    {
        foreach (var b in blocks)
            EmitBlock(b, code);
    }

    private static void EmitBlock(BlockNode block, List<Instruction> code)
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
                EmitList(block.ThenBranch, code);
                // ── 跳過 else 分支 ──
                var jmp = new Instruction(OpCode.Jump);
                code.Add(jmp);
                // ── patch jif → else 起點 ──
                jif.Params["__target"] = (object?)code.Count;
                EmitList(block.ElseBranch, code);
                // ── patch jmp → 結尾 ──
                jmp.Params["__target"] = (object?)code.Count;
                break;
            }

            case BlockType.RepeatN:
            {
                code.Add(new Instruction(OpCode.RepeatPush, new(block.Params)));
                int loopStart = code.Count;
                EmitList(block.LoopBody, code);
                code.Add(new Instruction(OpCode.RepeatStep,
                    new() { ["__loopStart"] = (object?)loopStart }));
                break;
            }

            case BlockType.RepeatWhile:
            {
                int loopStart = code.Count;
                var whileCheck = new Instruction(OpCode.WhileCheck, new(block.Params));
                code.Add(whileCheck);
                EmitList(block.LoopBody, code);
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
                EmitList(block.LoopBody, code);
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

            case BlockType.Broadcast:
            case BlockType.BroadcastAndWait: // 本版行為同 Broadcast
                code.Add(new Instruction(OpCode.Broadcast, new(block.Params)));
                break;

            case BlockType.OnReceive:
                code.Add(new Instruction(OpCode.OnReceive, new(block.Params)));
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
}
