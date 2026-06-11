namespace SkillCreator.UI;

using System.Text;
using SkillCreator.AbilitySystem;
using SkillCreator.AbilitySystem.Data;

// Stage 5：技能整構文案自動生成
//   GenerateStructured → 編輯器右側面板（設計者快速確認）
//   GenerateProse      → SpellListUI Tooltip（玩家視角）
public static class SpellDescriptionGenerator
{
    private static readonly Dictionary<ElementType, string> Elem = new()
    {
        [ElementType.Metal]   = "金", [ElementType.Wood]    = "木",
        [ElementType.Water]   = "水", [ElementType.Fire]    = "火",
        [ElementType.Earth]   = "土", [ElementType.Ice]     = "冰",
        [ElementType.Wind]    = "風", [ElementType.Light]   = "光",
        [ElementType.Dark]    = "暗", [ElementType.Thunder] = "雷",
        [ElementType.Poison]  = "毒",
    };

    private static readonly Dictionary<AbilityActivationType, string> Act = new()
    {
        [AbilityActivationType.Instant]   = "即時型",
        [AbilityActivationType.Declare]   = "宣告型",
        [AbilityActivationType.Sustained] = "持續型",
    };

    private static readonly Dictionary<ContainerType, string> Ct = new()
    {
        [ContainerType.DirectCast]     = "直接施放",
        [ContainerType.Projectile]     = "投射物",
        [ContainerType.SummonMinion]   = "召喚精靈",
        [ContainerType.SummonTurret]   = "召喚砲台",
        [ContainerType.SummonGuardian] = "召喚護衛",
    };

    // ── 結構化摘要（給設計者） ──────────────────────────────────────
    public static string GenerateStructured(SpellArray spell)
    {
        var sb = new StringBuilder();

        float mp = AbilityPointCalculator.CalculateMpCost(spell);
        if (spell.Container != ContainerType.DirectCast)
        {
            sb.Append(Ct.GetValueOrDefault(spell.Container, ""));
            sb.Append("  ");
        }
        sb.Append(Act.GetValueOrDefault(spell.ActivationType, ""));
        sb.AppendLine($"  MP {mp:F0}");
        if (spell.IsPassive) sb.AppendLine("[被動]");

        var nonEmpty = spell.Slots.Where(s => !s.IsEmpty).ToList();
        if (nonEmpty.Count > 0)
        {
            sb.AppendLine();
            foreach (var slot in nonEmpty)
            {
                var totem = slot.Totem!;
                sb.Append("▸ ").Append(totem.DisplayName);
                var elem = slot.LocalEngravings
                    .FirstOrDefault(e => e.Element != ElementType.None)?.Element
                    ?? ElementType.None;
                if (elem != ElementType.None)
                    sb.Append("  ").Append(Elem.GetValueOrDefault(elem, ""));
                sb.AppendLine();
                if (slot.LocalEngravings.Count > 0)
                {
                    sb.Append("  ");
                    sb.AppendLine(string.Join("·", slot.LocalEngravings.Select(e => e.DisplayName)));
                }
            }
        }

        if (spell.GlobalEngravings.Count > 0)
        {
            sb.AppendLine();
            sb.Append("全域：");
            sb.AppendLine(string.Join("·", spell.GlobalEngravings.Select(e => e.DisplayName)));
        }

        if (spell.Blocks.Count > 0)
        {
            sb.AppendLine();
            var names = spell.Blocks.Take(7).Select(b => ScratchCanvas.BlockName(b));
            sb.Append("積木：").Append(string.Join("›", names));
            if (spell.Blocks.Count > 7) sb.Append("…");
            sb.AppendLine();
        }

        if (spell.ContainerEffect is { } ce && ce.Blocks.Count > 0)
        {
            sb.AppendLine();
            var names = ce.Blocks.Take(5).Select(b => ScratchCanvas.BlockName(b));
            sb.Append("容器：").Append(string.Join("›", names));
            if (ce.Blocks.Count > 5) sb.Append("…");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ── 散文段落（給玩家看） ───────────────────────────────────────
    public static string GenerateProse(SpellArray spell)
    {
        var sb = new StringBuilder();

        float mp = AbilityPointCalculator.CalculateMpCost(spell);
        string passiveStr = spell.IsPassive ? "被動" : "主動";
        string actStr     = Act.GetValueOrDefault(spell.ActivationType, "宣告型");
        string ctStr = spell.Container != ContainerType.DirectCast
            ? Ct.GetValueOrDefault(spell.Container, "") + "，" : "";

        sb.Append($"{passiveStr}技能，{actStr}，{ctStr}消耗 MP {mp:F0}。");

        var nonEmpty = spell.Slots.Where(s => !s.IsEmpty).ToList();
        if (nonEmpty.Count > 0)
        {
            var totemParts = nonEmpty.Select(slot =>
            {
                var t    = slot.Totem!;
                var elem = slot.LocalEngravings
                    .FirstOrDefault(e => e.Element != ElementType.None)?.Element
                    ?? ElementType.None;
                string elemStr = elem != ElementType.None
                    ? $"（{Elem.GetValueOrDefault(elem, "")}）" : "";
                return t.DisplayName + elemStr;
            });
            sb.AppendLine();
            sb.Append($"以{string.Join("、", totemParts)}發動。");

            var allLocal = nonEmpty
                .SelectMany(s => s.LocalEngravings)
                .Select(e => e.DisplayName)
                .Distinct()
                .ToList();
            if (allLocal.Count > 0)
            {
                sb.AppendLine();
                sb.Append($"附加刻印：{string.Join("、", allLocal)}。");
            }
        }

        if (spell.GlobalEngravings.Count > 0)
        {
            sb.AppendLine();
            sb.Append($"全域刻印：{string.Join("、", spell.GlobalEngravings.Select(e => e.DisplayName))}。");
        }

        if (spell.Blocks.Count > 0)
        {
            sb.AppendLine();
            var names = spell.Blocks.Take(5).Select(b => ScratchCanvas.BlockName(b));
            sb.Append($"施放邏輯含 {string.Join("›", names)}");
            if (spell.Blocks.Count > 5) sb.Append("…");
            sb.Append("。");
        }

        return sb.ToString().TrimEnd();
    }
}
