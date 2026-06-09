namespace SkillCreator.AbilitySystem.VM;

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

            // 其餘 BlockType 在 Phase 1 未實作，略過即可
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
