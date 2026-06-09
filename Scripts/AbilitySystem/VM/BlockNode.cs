namespace SkillCreator.AbilitySystem.VM;

public class BlockNode
{
    public BlockType Type { get; init; }

    // 積木參數（如 "duration"、"count"、"name"、"conditionType" 等）
    public Dictionary<string, object?> Params { get; init; } = new();

    // IF 積木的分支
    public List<BlockNode> ThenBranch { get; init; } = new();
    public List<BlockNode> ElseBranch { get; init; } = new();

    // RepeatN 積木的循環體
    public List<BlockNode> LoopBody { get; init; } = new();
}
