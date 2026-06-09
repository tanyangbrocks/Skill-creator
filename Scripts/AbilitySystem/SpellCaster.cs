namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.World;
using SkillCreator.World.Materials;

// 將一個 SpellArray 的效果實際施放到 TileWorld 上（Phase 1 硬編碼版）
public static class SpellCaster
{
    public static bool TryCast(SpellArray spell, PlayerController player, TileWorld world)
    {
        if (!player.CanCast) return false;

        float mpCost = AbilityPointCalculator.CalculateMpCost(spell);
        if (!SafetyGuard.HasMp(player.Mp, mpCost)) return false;

        player.Mp -= mpCost;
        player.SetCastCooldown(spell.CastDelay);

        foreach (var slot in spell.Slots)
        {
            if (slot.IsEmpty || slot.Totem?.Type != TotemType.Technique) continue;
            ExecuteTechnique(slot, player, world);
        }
        return true;
    }

    private static void ExecuteTechnique(SpellSlot slot, PlayerController player, TileWorld world)
    {
        // 從刻印計算加成
        float dmgBonus  = 0f;
        int   multiCount = 1;
        foreach (var eng in slot.LocalEngravings)
        {
            switch (eng.Id)
            {
                case "white_dmg":  dmgBonus   = eng.CalculateEffect(); break;
                case "blue_multi": multiCount = Math.Max(1, (int)eng.CalculateEffect()); break;
            }
        }

        int baseRadius = 2 + (int)(dmgBonus * 3f);   // 傷害增幅 0→1 對應 半徑 2→5
        var pos = player.Position;
        int fx  = player.Facing.X;
        int fy  = player.Facing.Y;

        for (int rep = 0; rep < multiCount; rep++)
        {
            switch (slot.Totem!.Id)
            {
                // ── 斬擊：在朝向方向 N 格處爆炸 ───────────────────
                case "technique_slash":
                    var impact = new GridPos(pos.X + fx * (4 + rep * 3),
                                            pos.Y + fy * (4 + rep * 3));
                    world.Explode(impact.X, impact.Y, baseRadius);
                    break;

                // ── 投射物：沿朝向噴出一條火線 ────────────────────
                case "technique_projectile":
                    int range = 15 + rep * 5;
                    for (int i = 1; i <= range; i++)
                    {
                        int tx = pos.X + fx * i, ty = pos.Y + fy * i;
                        var mat = world.TypeAt(tx, ty);
                        if (mat == MaterialType.Stone) break;
                        world.Set(tx, ty, MaterialType.Fire);
                        if (mat != MaterialType.Air) break; // 撞上可燃物即停
                    }
                    break;

                // ── 範圍效果：以玩家為中心爆炸 ────────────────────
                case "technique_area":
                    world.Explode(pos.X + fx * rep * 2, pos.Y + fy * rep * 2,
                                  baseRadius + 2 + rep);
                    world.SpawnEffect("fire", new GridPos(pos.X, pos.Y),
                        new Dictionary<string, object?> { ["radius"] = baseRadius });
                    break;
            }
        }
    }
}
