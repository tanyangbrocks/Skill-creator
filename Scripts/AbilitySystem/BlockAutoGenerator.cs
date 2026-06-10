namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.VM;

// 根據 SpellArray 的插槽排列，自動生成對應的積木序列
public static class BlockAutoGenerator
{
    // 插槽名稱規則：優先用 slot.Name，否則用 "slot_{index}"
    public static string SlotRef(SpellArray spell, int idx)
        => string.IsNullOrEmpty(spell.Slots[idx].Name)
            ? $"slot_{idx}"
            : spell.Slots[idx].Name;

    public static List<BlockNode> Generate(SpellArray spell)
    {
        var blocks = new List<BlockNode>();

        var actions = new List<(int idx, SpellSlot slot)>();

        for (int i = 0; i < spell.Slots.Count; i++)
        {
            var s = spell.Slots[i];
            if (s.IsEmpty) continue;
            actions.Add((i, s));
        }

        // 所有插槽均為動作圖騰，依序執行
        foreach (var (idx, _) in actions)
            blocks.Add(Invoke(SlotRef(spell, idx)));

        return blocks;
    }

    // 產生積木的易讀文字描述（供編輯器顯示用）
    public static string Describe(List<BlockNode> blocks, int indent = 0)
    {
        var sb = new System.Text.StringBuilder();
        string pad = new string(' ', indent * 2);
        foreach (var b in blocks)
        {
            string p = "";
            if (b.Params.TryGetValue("totemName",     out var tn)) p = $"「{tn}」";
            else if (b.Params.TryGetValue("spellName", out var sn)) p = $"「{sn}」";
            else if (b.Params.TryGetValue("conditionType", out var ct))
            {
                string tname = b.Params.TryGetValue("totemName", out var n2) ? $"「{n2}」" : "";
                p = $"[{ct} {tname}]";
            }
            else if (b.Params.TryGetValue("duration", out var dur)) p = $"{dur}s";
            else if (b.Params.TryGetValue("count",    out var cnt)) p = $"×{cnt}";

            sb.AppendLine($"{pad}{b.Type} {p}");

            if (b.ThenBranch.Count > 0)
            {
                sb.Append($"{pad}  → ");
                sb.Append(Describe(b.ThenBranch, indent + 2).TrimStart());
            }
        }
        return sb.ToString();
    }

    private static BlockNode Invoke(string name) => new BlockNode
    {
        Type   = BlockType.InvokeTotem,
        Params = new Dictionary<string, object?> { ["totemName"] = name },
    };
}
