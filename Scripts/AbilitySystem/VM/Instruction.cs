namespace SkillCreator.AbilitySystem.VM;

public sealed class Instruction
{
    public OpCode                    Op     { get; }
    public Dictionary<string, object?> Params { get; }

    public Instruction(OpCode op, Dictionary<string, object?> p)
    {
        Op     = op;
        Params = p;
    }

    public Instruction(OpCode op) : this(op, new()) { }
}
