namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.VM;
using SkillCreator.World;
using SkillCreator.World.Materials;
using VmContext = SkillCreator.AbilitySystem.VM.ExecutionContext;

// 施放法陣：透過 VM 執行積木序列，每個 InvokeTotem 導向對應圖騰效果
public static class SpellCaster
{
    // MaxComboDepth 統一定義於 SafetyGuard.MaxComboDepth
    private const int MeleeRange    = 3;   // Contact 容器掃描格數

    // 施放結果（成功 + 可能產生的投射物）
    public readonly struct SpellCastResult
    {
        public bool             Ok        { get; init; }
        public SpellProjectile? Projectile{ get; init; }
        public static SpellCastResult Failed => default;
    }

    // runner != null 時，PlayerBody 法陣提交給 Runner 做跨幀執行（Wait 真實計時）
    // runner == null 時維持舊的同步執行（Projectile/Contact 命中時使用）
    public static SpellCastResult TryCast(SpellArray spell, PlayerController player, TileWorld world,
        EnemyManager? enemies = null, SpellLoadout? loadout = null, SpellRunner? runner = null)
    {
        if (!player.CanCast) return SpellCastResult.Failed;

        if (AbilityPointCalculator.ExceedsLevelCap(spell, player.Level))
        {
            Godot.GD.Print($"[施放] 能力點 {AbilityPointCalculator.CalculateTotalCost(spell)} 超過等級 {player.Level} 上限");
            return SpellCastResult.Failed;
        }

        float mpCost = AbilityPointCalculator.CalculateMpCost(spell);
        if (!SafetyGuard.HasMp(player.Mp, mpCost)) return SpellCastResult.Failed;

        player.Mp -= mpCost;
        player.SetCastCooldown(spell.CastDelay);
        CombatState.OnSpellCast();

        switch (spell.Container)
        {
            case ContainerType.Projectile:
            {
                var start = new GridPos(player.Position.X + player.Facing.X * 2, player.Position.Y);
                return new SpellCastResult
                {
                    Ok         = true,
                    Projectile = new SpellProjectile(start, player.Facing, spell, player, enemies, loadout, runner),
                };
            }
            case ContainerType.Contact:
                ExecuteContactHit(spell, player, world, enemies, loadout, runner);
                return new SpellCastResult { Ok = true };

            default:
                if (runner != null)
                    runner.Submit(spell, player, world, enemies, loadout);
                else
                    ExecuteEffects(spell, player, world, enemies, loadout);
                return new SpellCastResult { Ok = true };
        }
    }

    // ── Contact 容器：近戰範圍命中 ────────────────────────────────

    private static void ExecuteContactHit(SpellArray spell, PlayerController player, TileWorld world,
        EnemyManager? enemies, SpellLoadout? loadout, SpellRunner? runner = null)
    {
        int fx = player.Facing.X;

        // 掃描前方最多 MeleeRange 格找第一個敵人
        if (enemies is not null)
        {
            for (int d = 1; d <= MeleeRange; d++)
            {
                var checkPos = new GridPos(player.Position.X + fx * d, player.Position.Y);
                var target = enemies.Enemies.Find(e => e.IsAlive && e.Position == checkPos);
                if (target is not null)
                {
                    float meleeDmg = 20f * player.Equipment.TotalAtkMult;
                    target.TakeDamage(meleeDmg);
                    CombatState.OnPlayerDealtDamage(meleeDmg);
                    if (runner != null)
                    {
                        runner.Submit(spell, player, world, enemies, loadout, fixedOrigin: checkPos);
                    }
                    else
                    {
                        var orig = player.Position;
                        player.Position = checkPos;
                        ExecuteEffects(spell, player, world, enemies, loadout, atHitPoint: true);
                        player.Position = orig;
                    }
                    return;
                }
            }
        }

        // 未命中敵人：在正前方 2 格執行（AoE 效果仍可擊中範圍內目標）
        var meleePt = new GridPos(player.Position.X + fx * 2, player.Position.Y);
        if (runner != null)
        {
            runner.Submit(spell, player, world, enemies, loadout, fixedOrigin: meleePt);
        }
        else
        {
            var origPos = player.Position;
            player.Position = meleePt;
            ExecuteEffects(spell, player, world, enemies, loadout, atHitPoint: true);
            player.Position = origPos;
        }
    }

    // ── 直接執行效果（PlayerBody / Contact 命中 / 投射物命中時皆可呼叫）────

    // atHitPoint：效果中心即 player.Position（投射物命中 / 接觸命中時為 true）
    //             technique_slash 等會用此 flag 把爆炸 offset 改為 0，避免偏移到命中點之外
    public static void ExecuteEffects(SpellArray spell, PlayerController player, TileWorld world,
        EnemyManager? enemies = null, SpellLoadout? loadout = null, int comboDepth = 0,
        bool atHitPoint = false)
    {
        var blocks = spell.Blocks.Count > 0
            ? spell.Blocks
            : BlockAutoGenerator.Generate(spell);

        if (blocks.Count == 0) return;

        var slotByRef = BuildSlotLookup(spell);
        var ctx  = new ExecutionContext(SpellCompiler.Compile(blocks));
        if (enemies != null)
            ctx.EntityQuery = r => QueryEnemies(enemies, player, r);
        ctx.RaycastQuery     = (start, dx, dy, dist) => world.Raycast(start, dx, dy, dist);
        ctx.FocalPointQuery  = () => player.MouseGridPos;
        ctx.PlayerStatsQuery = key => key switch
        {
            "hp"    => player.Hp,
            "mp"    => player.Mp,
            "hpPct" => player.Hp / PlayerController.MaxHp,
            "mpPct" => player.Mp / player.MaxMp,
            _       => 0f,
        };
        var loop = new ExecutionLoop(new SafetyGuard());
        loop.ResetTick();

        int safety = 0;
        while (!ctx.IsFinished && safety++ < SafetyGuard.MaxStepsPerCast)
        {
            // 同步執行模式：強制跳過 Wait / WaitingSignal / WaitingCondition
            if (ctx.State == ExecutionState.Waiting)
            {
                ctx.WaitRemaining = 0f;
                ctx.PC++;
                ctx.State = ExecutionState.Running;
            }
            if (ctx.State == ExecutionState.WaitingSignal)
            {
                ctx.WaitingSignalName = null;
                ctx.PC++;
                ctx.State = ExecutionState.Running;
            }
            if (ctx.State == ExecutionState.WaitingCondition)
            {
                ctx.WaitingConditionKey = null;
                ctx.PC++;
                ctx.State = ExecutionState.Running;
            }
            if (ctx.State == ExecutionState.WaitingRisingEdge ||
                ctx.State == ExecutionState.WaitingFallingEdge)
            {
                ctx.WaitingEdgePC = -1;
                ctx.PC++;
                ctx.State = ExecutionState.Running;
            }

            loop.Step(ctx, 0f);

            if (ctx.PendingInvokeTotem != null)
                ConsumeInvokeTotem(ctx, slotByRef, player, world, atHitPoint);

            if (ctx.PendingInvokeSpell != null)
            {
                string nextName = ctx.PendingInvokeSpell;
                ctx.PendingInvokeSpell = null;
                TriggerCombo(nextName, player, world, enemies, loadout, comboDepth);
            }

            if (ctx.PendingEntityDamageId >= 0)
                ConsumeEntityDamage(ctx, enemies);

            if (ctx.PendingEntityMoveId >= 0)
                ConsumeEntityMove(ctx, enemies);
        }
    }

    // ── 連段執行 ─────────────────────────────────────────────────

    private static void TriggerCombo(string name, PlayerController player, TileWorld world,
        EnemyManager? enemies, SpellLoadout? loadout, int depth)
    {
        if (loadout is null || depth >= SafetyGuard.MaxComboDepth) return;

        // 在 loadout 中找名稱匹配的法陣
        SpellArray? next = null;
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            var s = loadout.GetSlot(i);
            if (s?.Name == name) { next = s; break; }
        }
        if (next is null) return;

        float cost = AbilityPointCalculator.CalculateMpCost(next);
        if (!SafetyGuard.HasMp(player.Mp, cost)) return;

        player.Mp -= cost;
        player.SetCastCooldown(next.CastDelay);
        ExecuteEffects(next, player, world, enemies, loadout, depth + 1);
    }

    // ── 共用 pending 消費 helper（SpellCaster + SpellRunner 均呼叫）─

    internal static void ConsumeInvokeTotem(
        ExecutionContext ctx, Dictionary<string, SpellSlot> slotByRef,
        PlayerController player, TileWorld world, bool atHitPoint)
    {
        string name = ctx.PendingInvokeTotem!;
        ctx.PendingInvokeTotem = null;
        // 以 EffectOriginOverride 取代 player.Position 暫改，避免競態風險
        ctx.EffectOriginOverride = ctx.CurrentIterEntity.HasValue
            ? ctx.CurrentIterEntity.Value.Position
            : ctx.FixedOrigin;
        if (slotByRef.TryGetValue(name, out var slot))
            ResolveTotem(name, slot, ctx, player, world,
                atHitPoint: ctx.CurrentIterEntity.HasValue || atHitPoint || ctx.FixedOrigin.HasValue);
        else
            ctx.DoneTotems.Add(name);
        ctx.EffectOriginOverride = null;
    }

    internal static void ConsumeEntityMove(ExecutionContext ctx, EnemyManager? enemies)
    {
        int     id  = ctx.PendingEntityMoveId;
        GridPos pos = ctx.PendingEntityMovePos;
        ctx.PendingEntityMoveId = -1;
        if (enemies == null) return;
        var enemy = enemies.Enemies.Find(e => e.Id == id);
        if (enemy == null) return;
        enemy.Position = pos;
        // 同步更新快照
        if (ctx.CurrentIterEntity.HasValue && ctx.CurrentIterEntity.Value.Id == id)
        {
            var e = ctx.CurrentIterEntity.Value;
            ctx.CurrentIterEntity    = new EntityInfo(e.Id, pos, e.Hp, e.MaxHp);
            ctx.InstanceVars["_e.x"] = pos.X;
            ctx.InstanceVars["_e.y"] = pos.Y;
        }
    }

    internal static void ConsumeEntityDamage(ExecutionContext ctx, EnemyManager? enemies)
    {
        int   id  = ctx.PendingEntityDamageId;
        float dmg = ctx.PendingEntityDamageAmount;
        ctx.PendingEntityDamageId     = -1;
        ctx.PendingEntityDamageAmount = 0f;
        if (enemies == null) return;
        var enemy = enemies.Enemies.Find(e => e.Id == id);
        if (enemy == null) return;
        enemy.TakeDamage(dmg);
        CombatState.OnPlayerDealtDamage(dmg);
        // 同步更新 ForEach 迭代快照，避免同輪 GetEntityProp "hp" 讀到舊值
        if (ctx.CurrentIterEntity.HasValue && ctx.CurrentIterEntity.Value.Id == id)
        {
            var e = ctx.CurrentIterEntity.Value;
            ctx.CurrentIterEntity     = new EntityInfo(e.Id, e.Position, enemy.Hp, e.MaxHp);
            ctx.InstanceVars["_e.hp"] = enemy.Hp;
        }
    }

    // ── 實體查詢（ForEachNearby / QueryNearest 共用）──────────────

    internal static List<EntityInfo> QueryEnemies(
        EnemyManager enemies, PlayerController player, float radius)
    {
        var origin = player.Position;
        return enemies.Enemies
            .Select(e => (e, dist: e.IsAlive ? e.Position.DistanceTo(origin) : float.MaxValue))
            .Where(x => x.e.IsAlive && x.dist <= radius)
            .OrderBy(x => x.dist)
            .Select(x => new EntityInfo(x.e.Id, x.e.Position, x.e.Hp, x.e.MaxHp))
            .ToList();
    }

    // ── 建立插槽參考對照表 ─────────────────────────────────────────

    internal static Dictionary<string, SpellSlot> BuildSlotLookup(SpellArray spell)
    {
        var dict = new Dictionary<string, SpellSlot>();
        for (int i = 0; i < spell.Slots.Count; i++)
        {
            var s = spell.Slots[i];
            if (s.IsEmpty) continue;
            dict[BlockAutoGenerator.SlotRef(spell, i)] = s;
        }
        return dict;
    }

    // ── 圖騰解析：觸發條件 or 執行效果 ──────────────────────────────

    internal static void ResolveTotem(string name, SpellSlot slot,
        ExecutionContext ctx, PlayerController player, TileWorld world, bool atHitPoint = false)
    {
        switch (slot.Totem!.Type)
        {
            case TotemType.Trigger:
                if (EvaluateTrigger(slot, player))
                    ctx.DoneTotems.Add(name);
                else
                    ctx.FizzledTotems.Add(name);
                break;

            default:
                ExecuteSlot(slot, player, world, atHitPoint, ctx.EffectOriginOverride);
                ctx.HitTotems.Add(name);
                ctx.DoneTotems.Add(name);
                break;
        }
    }

    // ── 觸發條件判斷（Phase 1 簡化版）────────────────────────────────

    private static bool EvaluateTrigger(SpellSlot slot, PlayerController player)
        => slot.Totem!.Id switch
        {
            "trigger_on_cast"    => true,
            "trigger_on_hit"     => true, // Phase 1：假設命中
            "trigger_on_hp_low"  => player.Hp < PlayerController.MaxHp * 0.3f,
            "trigger_on_kill"    => true, // Phase 1：假設
            "trigger_periodic"   => true,
            "trigger_on_damaged" => true,
            _                    => true,
        };

    // ── 分派到各類型圖騰執行器 ─────────────────────────────────────

    private static void ExecuteSlot(SpellSlot slot, PlayerController player, TileWorld world,
        bool atHitPoint = false, GridPos? originOverride = null)
    {
        switch (slot.Totem!.Type)
        {
            case TotemType.Technique:    ExecuteTechnique(slot, player, world, atHitPoint, originOverride); break;
            case TotemType.Morph:        ExecuteMorph(slot, player, world, originOverride);                break;
            case TotemType.Displacement: ExecuteDisplacement(slot, player, world);                         break; // 位移以玩家真實位置為準
            case TotemType.Summon:       ExecuteSummon(slot, player, world, originOverride);               break;
            case TotemType.Domain:       ExecuteDomain(slot, player, world, originOverride);               break;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  圖騰效果實作
    // ════════════════════════════════════════════════════════════

    private struct Mods
    {
        public float DmgBonus; public int Multi;
        public bool Fire, Water, Ice, Thunder;
    }

    private static Mods ReadMods(SpellSlot slot)
    {
        var m = new Mods { Multi = 1 };
        foreach (var e in slot.LocalEngravings)
        {
            switch (e.Id)
            {
                case "white_dmg":    m.DmgBonus = e.CalculateEffect();              break;
                case "blue_multi":   m.Multi    = Math.Max(1,(int)e.CalculateEffect()); break;
                case "elem_fire":    m.Fire     = true;                             break;
                case "elem_water":   m.Water    = true;                             break;
                case "elem_ice":     m.Ice      = true;                             break;
                case "elem_thunder": m.Thunder  = true;                             break;
            }
        }
        return m;
    }

    // ── 武技 ──────────────────────────────────────────────────────

    private static void ExecuteTechnique(SpellSlot slot, PlayerController player, TileWorld world,
        bool atHitPoint = false, GridPos? originOverride = null)
    {
        var m = ReadMods(slot);
        int r  = 2 + (int)(m.DmgBonus * 3f);
        var p  = originOverride ?? player.Position;
        int fx = player.Facing.X, fy = player.Facing.Y;

        // 直接施放：爆炸在前方 4 格；投射物/接觸命中：爆炸就在命中點（offset 0）
        int slashOfs = atHitPoint ? 0 : 4;

        for (int rep = 0; rep < m.Multi; rep++)
        {
            switch (slot.Totem!.Id)
            {
                case "technique_slash":
                    var hit = new GridPos(p.X + fx * (slashOfs + rep * 3),
                                         p.Y + fy * (slashOfs + rep * 3));
                    world.Explode(hit.X, hit.Y, r);
                    ApplyElement(world, hit, r, m);
                    break;

                case "technique_projectile":
                    int range = 15 + rep * 5;
                    for (int i = 1; i <= range; i++)
                    {
                        int tx = p.X + fx * i, ty = p.Y + fy * i;
                        var mat = world.TypeAt(tx, ty);
                        if (mat == MaterialType.Stone) break;
                        world.Set(tx, ty, m.Water || m.Ice ? MaterialType.Water : MaterialType.Fire);
                        if (mat != MaterialType.Air) break;
                    }
                    break;

                case "technique_area":
                    int ar = r + 2 + rep;
                    world.Explode(p.X + fx * rep * 2, p.Y + fy * rep * 2, ar);
                    ApplyElement(world, p, ar, m);
                    if (!m.Water && !m.Ice)
                        world.SpawnEffect("fire", p, new Dictionary<string, object?> { ["radius"] = r });
                    break;

                case "technique_beam":
                    for (int i = 1; i <= 25 + rep * 8; i++)
                    {
                        int tx = p.X + fx * i, ty = p.Y + fy * i;
                        if (!world.InBoundsPublic(tx, ty)) break;
                        for (int dy = -1; dy <= 1; dy++)
                            if (world.TypeAt(tx, ty + dy) != MaterialType.Stone)
                                world.Set(tx, ty + dy, m.Water ? MaterialType.Water : MaterialType.Fire);
                    }
                    break;

                case "technique_chain":
                    for (int c = 0; c < 3 + rep; c++)
                    {
                        var cp = new GridPos(p.X + fx * (3 + c * 3), p.Y + fy * (3 + c * 3));
                        world.Explode(cp.X, cp.Y, Math.Max(1, r - c));
                    }
                    break;
            }
        }
    }

    private static void ApplyElement(TileWorld world, GridPos pos, int radius, in Mods m)
    {
        if (m.Fire)  world.SpawnEffect("fire",  pos, new Dictionary<string, object?> { ["radius"] = radius });
        if (m.Water) world.SpawnEffect("water", pos, new Dictionary<string, object?> { ["radius"] = radius });
    }

    // ── 變幻 ──────────────────────────────────────────────────────

    private static void ExecuteMorph(SpellSlot slot, PlayerController player, TileWorld world,
        GridPos? originOverride = null)
    {
        var p = originOverride ?? player.Position;
        switch (slot.Totem!.Id)
        {
            case "morph_speed":
            case "morph_strengthen":
                world.SpawnEffect("water", p, new Dictionary<string, object?> { ["radius"] = 1 });
                break;
            case "morph_flight":
                world.Explode(p.X, p.Y + 3, 2);
                break;
            case "morph_invisible":
                world.SpawnEffect("water", p, new Dictionary<string, object?> { ["radius"] = 2 });
                break;
        }
    }

    // ── 位移 ──────────────────────────────────────────────────────

    private static void ExecuteDisplacement(SpellSlot slot, PlayerController player, TileWorld world)
    {
        int fx = player.Facing.X, fy = player.Facing.Y;
        switch (slot.Totem!.Id)
        {
            case "displace_dash":
                for (int i = 0; i < 10; i++)
                {
                    var n = new GridPos(player.Position.X + fx, player.Position.Y + fy);
                    if (world.TypeAt(n.X, n.Y) != MaterialType.Air) break;
                    player.Position = n;
                }
                break;
            case "displace_teleport":
                for (int i = 20; i >= 1; i--)
                {
                    var tp = new GridPos(player.Position.X + fx * i, player.Position.Y + fy * i);
                    if (world.TypeAt(tp.X, tp.Y) == MaterialType.Air) { player.Position = tp; break; }
                }
                break;
            case "displace_dodge":
                for (int i = 0; i < 5; i++)
                {
                    var b = new GridPos(player.Position.X - fx, player.Position.Y - fy);
                    if (world.TypeAt(b.X, b.Y) != MaterialType.Air) break;
                    player.Position = b;
                }
                break;
        }
    }

    // ── 召喚 ──────────────────────────────────────────────────────

    private static void ExecuteSummon(SpellSlot slot, PlayerController player, TileWorld world,
        GridPos? originOverride = null)
    {
        var origin = originOverride ?? player.Position;
        var sp = new GridPos(origin.X + player.Facing.X * 4,
                             origin.Y + player.Facing.Y * 4);
        switch (slot.Totem!.Id)
        {
            case "summon_minion":
                world.SpawnEffect("fire", sp, new Dictionary<string, object?> { ["radius"] = 1 });
                break;
            case "summon_turret":
                for (int dy = -1; dy <= 1; dy++)
                    world.Set(sp.X, sp.Y + dy, MaterialType.Stone);
                break;
            case "summon_guardian":
                world.SpawnEffect("water", sp, new Dictionary<string, object?> { ["radius"] = 2 });
                break;
        }
    }

    // ── 領域 ──────────────────────────────────────────────────────

    private static void ExecuteDomain(SpellSlot slot, PlayerController player, TileWorld world,
        GridPos? originOverride = null)
    {
        var pos = originOverride ?? player.Position;
        switch (slot.Totem!.Id)
        {
            case "domain_barrier":
                for (int angle = 0; angle < 360; angle += 10)
                {
                    float rad = angle * MathF.PI / 180f;
                    world.Set(pos.X + (int)(MathF.Cos(rad) * 8),
                              pos.Y + (int)(MathF.Sin(rad) * 8), MaterialType.Stone);
                }
                break;
            case "domain_terrain":
                world.Explode(pos.X, pos.Y, 12);
                break;
            case "domain_weather":
                for (int dx = -8; dx <= 8; dx++)
                    world.Set(pos.X + dx, pos.Y - 10, MaterialType.Water);
                break;
        }
    }
}
