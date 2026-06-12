namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.VM;
using SkillCreator.UI;
using SkillCreator.World;
using SkillCreator.World.Materials;
using VmContext = SkillCreator.AbilitySystem.VM.ExecutionContext;

// 施放技能整構：透過 VM 執行積木序列，每個 InvokeTotem 導向對應技能因子效果
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

    // runner != null 時，DirectCast 技能整構提交給 Runner 做跨幀執行（Wait 真實計時）
    // runner == null 時維持同步執行（Projectile/Contact 命中時使用）
    public static SpellCastResult TryCast(SpellArray spell, PlayerController player, TileWorld3D world,
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
        ApplyGlobalEngravings(spell);

        switch (spell.Container)
        {
            case ContainerType.Projectile:
            {
                // 投射物方向：以滑鼠游標位置為準（精確浮點，含 3D Z 方向）
                var mouseDelta = player.MouseGridPos - player.Position;
                float fdx = mouseDelta.X;
                float fdy = mouseDelta.Y;
                float fdz = mouseDelta.Z;
                if (Math.Abs(fdx) < 0.001f && Math.Abs(fdy) < 0.001f && Math.Abs(fdz) < 0.001f)
                    fdx = player.Facing.X;
                // 起點：XZ 方向偏移 2 格，垂直方向只在向上時往上偏移
                int sdx    = Math.Sign(fdx) != 0 ? Math.Sign(fdx) : 0;
                int sdz    = Math.Sign(fdz) != 0 ? Math.Sign(fdz) : 0;
                int startY = player.Position.Y + (fdy < 0f ? -1 : 0);
                var start  = new GridPos(player.Position.X + sdx * 2, startY,
                                         player.Position.Z + sdz);
                return new SpellCastResult
                {
                    Ok         = true,
                    Projectile = new SpellProjectile(start, fdx, fdy, fdz, spell, player, enemies, loadout, runner),
                };
            }

            case ContainerType.Contact:
                // [已移除] Contact 現為觸發條件，保留 case 避免舊存檔施放異常
                ExecuteContactHit(spell, player, world, enemies, loadout, runner);
                return new SpellCastResult { Ok = true };

            // TODO-STUB: 召喚物容器——效果應由實際召喚物 AI 執行，目前以佔位效果代替
            case ContainerType.SummonMinion:
            case ContainerType.SummonTurret:
            case ContainerType.SummonGuardian:
                ExecuteSummonContainer(spell, player, world, enemies, loadout, runner);
                return new SpellCastResult { Ok = true };

            default: // DirectCast：玩家直接施放，不透過容器
                if (runner != null)
                    runner.Submit(spell, player, world, enemies, loadout);
                else
                    ExecuteEffects(spell, player, world, enemies, loadout);
                return new SpellCastResult { Ok = true };
        }
    }

    // ── 召喚物容器（TODO-STUB：佔位）──────────────────────────────
    // 最終應由召喚物 AI 實體裝載 SpellArray 並在其行動時執行；
    // 目前直接在玩家位置執行效果作為佔位，等召喚物 AI 系統完成後替換。
    private static void ExecuteSummonContainer(SpellArray spell, PlayerController player, TileWorld3D world,
        EnemyManager? enemies, SpellLoadout? loadout, SpellRunner? runner = null)
    {
        if (runner != null)
            runner.Submit(spell, player, world, enemies, loadout);
        else
            ExecuteEffects(spell, player, world, enemies, loadout);
    }

    // ── Contact 容器：近戰範圍命中 ────────────────────────────────

    private static void ExecuteContactHit(SpellArray spell, PlayerController player, TileWorld3D world,
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
                    CombatState.OnHit?.Invoke(checkPos, meleeDmg, false);
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

    // ── 直接執行效果（DirectCast / Contact 命中 / 投射物命中時皆可呼叫）────

    // atHitPoint：效果中心即 player.Position（投射物命中 / 接觸命中時為 true）
    //             technique_slash 等會用此 flag 把爆炸 offset 改為 0，避免偏移到命中點之外
    // hitTarget：投射物/接觸命中的敵人快照，預設 CurrentIterEntity 讓 固定傷害 積木可對其扣血
    public static void ExecuteEffects(SpellArray spell, PlayerController player, TileWorld3D world,
        EnemyManager? enemies = null, SpellLoadout? loadout = null, int comboDepth = 0,
        bool atHitPoint = false, EntityInfo? hitTarget = null)
    {
        var blocks = spell.Blocks.Count > 0
            ? spell.Blocks
            : BlockAutoGenerator.Generate(spell);

        if (blocks.Count == 0) return;

        var slotByRef = BuildSlotLookup(spell);
        var ctx  = new ExecutionContext(SpellCompiler.Compile(blocks, spell));
        if (hitTarget.HasValue) ctx.CurrentIterEntity = hitTarget;
        if (enemies != null)
            ctx.EntityQuery = r => QueryEnemies(enemies, player, r);
        ctx.RaycastQuery     = (start, dx, dy, dist) => world.Raycast(start, dx, dy, dist);
        ctx.FocalPointQuery  = () => player.MouseGridPos;
        ctx.PlayerStatsQuery = key => key switch
        {
            "hp"    => player.Hp,
            "mp"    => player.Mp,
            "hpPct" => player.Hp / player.MaxHp,
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
                ConsumeInvokeTotem(ctx, slotByRef, player, world, atHitPoint, enemies);

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

    private static void TriggerCombo(string name, PlayerController player, TileWorld3D world,
        EnemyManager? enemies, SpellLoadout? loadout, int depth)
    {
        if (loadout is null || depth >= SafetyGuard.MaxComboDepth) return;

        // 在 loadout 中找名稱匹配的技能整構
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

    // ── 全域刻印：施放時立即生效的 ActionBus 過濾器 ──────────────────

    private static void ApplyGlobalEngravings(SpellArray spell)
    {
        foreach (var eng in spell.GlobalEngravings)
        {
            switch (eng.Id)
            {
                case "green_death_replace":
                    // 攔截下一次死亡 → 取消死亡，玩家存活於 1 HP（one-shot）
                    ActionBus.Register(
                        act => act is PlayerDeathAction ? null : act,
                        priority: 0, oneShot: true);
                    break;

                case "green_invincible":
                {
                    // 施放後 duration 秒內取消所有玩家受傷動作
                    float duration = eng.CalculateEffect(); // Linear BaseEffect=1f → 預設 1 秒
                    if (duration <= 0f) break;
                    ulong expiryMs = Godot.Time.GetTicksMsec() + (ulong)(duration * 1000);
                    ActionBus.Register(
                        act => act is PlayerDamageAction && Godot.Time.GetTicksMsec() < expiryMs
                            ? null : act,
                        priority: 0, oneShot: false, tag: $"invincible_{spell.Name}");
                    break;
                }
            }
        }
    }

    // ── 共用 pending 消費 helper（SpellCaster + SpellRunner 均呼叫）─

    internal static void ConsumeInvokeTotem(
        ExecutionContext ctx, Dictionary<string, SpellSlot> slotByRef,
        PlayerController player, TileWorld3D world, bool atHitPoint, EnemyManager? enemies = null)
    {
        string name = ctx.PendingInvokeTotem!;
        ctx.PendingInvokeTotem = null;
        // 以 EffectOriginOverride 取代 player.Position 暫改，避免競態風險
        ctx.EffectOriginOverride = ctx.CurrentIterEntity.HasValue
            ? ctx.CurrentIterEntity.Value.Position
            : ctx.FixedOrigin;
        if (slotByRef.TryGetValue(name, out var slot))
            ResolveTotem(name, slot, ctx, player, world, enemies,
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
        CombatState.OnHit?.Invoke(enemy.Position, dmg, false);
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
        var tmp    = new List<(float Dist, EntityInfo Info)>();
        foreach (var e in enemies.Enemies)
        {
            if (!e.IsAlive) continue;
            float d = e.Position.DistanceTo(origin);
            if (d <= radius)
                tmp.Add((d, new EntityInfo(e.Id, e.Position, e.Hp, e.MaxHp)));
        }
        tmp.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));
        var result = new List<EntityInfo>(tmp.Count);
        for (int i = 0; i < tmp.Count; i++) result.Add(tmp[i].Info);
        return result;
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

    // ── 技能因子解析：觸發條件 or 執行效果 ──────────────────────────────

    internal static void ResolveTotem(string name, SpellSlot slot,
        ExecutionContext ctx, PlayerController player, TileWorld3D world,
        EnemyManager? enemies = null, bool atHitPoint = false)
    {
        // 所有技能因子類型均為執行型（無條件評估），直接執行並記錄命中
        ExecuteSlot(slot, player, world, enemies, atHitPoint, ctx.EffectOriginOverride);
        ctx.HitTotems.Add(name);
        ctx.DoneTotems.Add(name);
    }

    // ── 分派到各類型技能因子執行器 ─────────────────────────────────────

    // Design B：以 Action 刻印 Id 驅動技能因子行為
    private static void ExecuteSlot(SpellSlot slot, PlayerController player, TileWorld3D world,
        EnemyManager? enemies = null, bool atHitPoint = false, GridPos? originOverride = null)
    {
        var m = ReadMods(slot);

        // yellow_hp_cost：施放前扣 HP
        if (m.HpCost > 0f) player.TakeDamage(m.HpCost);

        // 1. 先從插槽刻印找 OnCast Action 刻印
        string? actionId = null;
        foreach (var e in slot.LocalEngravings)
        {
            if (e.Category == EngraveCategory.Action && e.Trigger == EngraveTrigger.OnCast)
            { actionId = e.Id; break; }
        }
        // 2. Fallback：從 DefaultActionEngraveId 對照表取技能因子預設行為
        if (actionId == null && slot.Totem != null)
            TotemLibrary.DefaultActionEngraveId.TryGetValue(slot.Totem.Id, out actionId);

        var origin = originOverride ?? player.Position;

        if (actionId != null)
        {
            DispatchAction(actionId, slot, player, world, atHitPoint, originOverride);
            ApplyModsToNearbyEnemies(m, origin, enemies, world);
            return;
        }

        // 3. 向後相容：舊存檔無 Action 刻印時以技能因子類型執行
        if (slot.Totem == null) return;
        switch (slot.Totem.Type)
        {
            case TotemType.Area:         ExecuteArea("act_area_around", slot, player, world, originOverride); break;
            case TotemType.Technique:    ExecuteTechnique(slot, player, world, atHitPoint, originOverride);   break;
            case TotemType.Projectile:   ExecuteProjectileTotem(slot, player, world, originOverride);         break;
            case TotemType.Morph:        ExecuteMorph(slot, player, world, originOverride);                   break;
            case TotemType.Displacement: ExecuteDisplacement(slot, player, world);                            break;
            case TotemType.Summon:       ExecuteSummon(slot, player, world, originOverride);                  break;
            case TotemType.Domain:       ExecuteDomain(slot, player, world, originOverride);                  break;
        }
        ApplyModsToNearbyEnemies(m, origin, enemies, world);
    }

    // 以 act_* 刻印 Id 分派到對應執行器
    private static void DispatchAction(string actionId, SpellSlot slot, PlayerController player,
        TileWorld3D world, bool atHitPoint, GridPos? originOverride)
    {
        switch (actionId)
        {
            case "act_area_fan":
            case "act_area_around":
            case "act_area_distant":
            case "act_area_beam":
                ExecuteArea(actionId, slot, player, world, originOverride);
                break;

            case "act_technique_sword":
            case "act_technique_punch":
            case "act_technique_shield":
                ExecuteTechnique(slot, player, world, atHitPoint, originOverride);
                break;

            case "act_fire_projectile":
                ExecuteProjectileTotem(slot, player, world, originOverride);
                break;

            case "act_morph_apply":
                ExecuteMorph(slot, player, world, originOverride);
                break;

            case "act_dash":
            case "act_teleport":
            case "act_dodge":
            case "act_portal":
                ExecuteDisplacement(slot, player, world);
                break;

            case "act_summon_entity":
                ExecuteSummon(slot, player, world, originOverride);
                break;

            case "act_domain_activate":
                ExecuteDomain(slot, player, world, originOverride);
                break;

            case "act_passive_tick":
                break; // OnTick 路徑，OnCast 時不執行

            default:
                Godot.GD.PushWarning($"[SpellCaster] 未處理的 Action 刻印: {actionId}");
                break;
        }
    }

    private static void ExecuteArea(string shape, SpellSlot slot, PlayerController player,
        TileWorld3D world, GridPos? originOverride)
    {
        var m      = ReadMods(slot);
        var origin = originOverride ?? player.Position;
        int baseR  = 2 + (int)(m.DmgBonus * 3f);
        int fx     = player.Facing.X;
        int fy     = player.Facing.Y;
        int px     = -fy; // XY 平面垂直方向
        int py     =  fx;

        switch (shape)
        {
            case "act_area_around":
                world.Explode(origin.X, origin.Y, origin.Z, baseR + 1);
                ApplyElement(world, origin, baseR + 1, m);
                break;

            case "act_area_fan":
            {
                int fanRange = 8 + baseR;
                for (int d = 1; d <= fanRange; d++)
                {
                    int spread = (d + 1) / 2;
                    bool blocked = true;
                    for (int s = -spread; s <= spread; s++)
                    {
                        int tx = origin.X + fx * d + px * s;
                        int ty = origin.Y + fy * d + py * s;
                        if (!world.InBounds(tx, ty, origin.Z)) continue;
                        if (world.GetTile(tx, ty, origin.Z) == MaterialType.Stone) continue;
                        blocked = false;
                        world.Explode(tx, ty, origin.Z, 1);
                        ApplyElement(world, new GridPos(tx, ty, origin.Z), 1, m);
                    }
                    if (blocked) break;
                }
                break;
            }

            case "act_area_distant":
            {
                int distRange = 16 + baseR * 2;
                var target = new GridPos(origin.X + fx * distRange, origin.Y + fy * distRange, origin.Z);
                world.Explode(target.X, target.Y, target.Z, baseR + 2);
                ApplyElement(world, target, baseR + 2, m);
                break;
            }

            case "act_area_beam":
            {
                int beamLen = 18 + baseR;
                for (int i = 1; i <= beamLen; i++)
                {
                    int tx = origin.X + fx * i;
                    int ty = origin.Y + fy * i;
                    if (!world.InBounds(tx, ty, origin.Z)) break;
                    for (int s = -1; s <= 1; s++)
                    {
                        int bx = tx + px * s;
                        int by = ty + py * s;
                        var mat = world.GetTile(bx, by, origin.Z);
                        if (mat != MaterialType.Stone)
                            world.SetTile(bx, by, origin.Z,
                                m.Water || m.Ice ? MaterialType.Water : MaterialType.Fire);
                    }
                    if (world.GetTile(tx, ty, origin.Z) == MaterialType.Stone) break;
                }
                break;
            }
        }
    }

    private static void ExecuteProjectileTotem(SpellSlot slot, PlayerController player, TileWorld3D world, GridPos? originOverride)
    {
        // TODO-STUB: 投射物技能因子（能量/實物投射），目前以爆炸佔位
        var origin = originOverride ?? player.Position;
        world.Explode(origin.X, origin.Y, origin.Z, 1);
    }

    // ════════════════════════════════════════════════════════════
    //  技能因子效果實作
    // ════════════════════════════════════════════════════════════

    private struct Mods
    {
        public float DmgBonus; public int Multi;
        public bool  Fire, Water, Ice, Thunder;
        public float PushDist;   // orange_push
        public float PullDist;   // orange_pull
        public float SlowDur;    // orange_slow
        public float FreezeDur;  // orange_freeze
        public float StunDur;    // red_stun
        public float HpCost;     // yellow_hp_cost
    }

    private static Mods ReadMods(SpellSlot slot)
    {
        var m = new Mods { Multi = 1 };
        foreach (var e in slot.LocalEngravings)
        {
            switch (e.Id)
            {
                case "white_dmg":      m.DmgBonus  = e.CalculateEffect();               break;
                case "blue_multi":     m.Multi     = Math.Max(1,(int)e.CalculateEffect()); break;
                case "elem_fire":      m.Fire      = true;                               break;
                case "elem_water":     m.Water     = true;                               break;
                case "elem_ice":       m.Ice       = true;                               break;
                case "elem_thunder":   m.Thunder   = true;                               break;
                case "orange_push":    m.PushDist  = e.CalculateEffect();               break;
                case "orange_pull":    m.PullDist  = e.CalculateEffect();               break;
                case "orange_slow":    m.SlowDur   = Math.Max(0.5f, e.CalculateEffect()); break;
                case "orange_freeze":  m.FreezeDur = Math.Max(0.5f, e.CalculateEffect()); break;
                case "red_stun":       m.StunDur   = Math.Max(0.5f, e.CalculateEffect()); break;
                case "yellow_hp_cost": m.HpCost    = e.CalculateEffect();               break;
            }
        }
        return m;
    }

    // 套用 orange/red/yellow 對敵人的位移與狀態效果
    private static void ApplyModsToNearbyEnemies(
        in Mods m, GridPos origin, EnemyManager? enemies, TileWorld3D world)
    {
        if (enemies == null) return;
        bool hasEffect = m.PushDist > 0f || m.PullDist > 0f ||
                         m.SlowDur  > 0f || m.FreezeDur > 0f || m.StunDur > 0f;
        if (!hasEffect) return;

        int radius = 4 + (int)(m.DmgBonus * 3f);
        foreach (var e in enemies.Enemies)
        {
            if (!e.IsAlive) continue;
            int distXZ = Math.Abs(e.Position.X - origin.X) + Math.Abs(e.Position.Z - origin.Z);
            if (distXZ > radius) continue;

            if (m.FreezeDur > 0f) e.Aura.ApplyFreeze(m.FreezeDur, e);
            if (m.StunDur   > 0f) e.Aura.ApplyFreeze(m.StunDur,   e);
            if (m.SlowDur   > 0f) e.Aura.ApplySlow(m.SlowDur, e);

            if (m.PushDist > 0f)
            {
                int dx = Math.Sign(e.Position.X - origin.X);
                int dz = Math.Sign(e.Position.Z - origin.Z);
                if (dx == 0 && dz == 0) dx = 1;
                for (int s = 0; s < (int)m.PushDist; s++)
                {
                    var nxt = new GridPos(e.Position.X + dx, e.Position.Y, e.Position.Z + dz);
                    if (world.GetTile(nxt.X, nxt.Y, nxt.Z) == MaterialType.Air) e.Position = nxt;
                    else break;
                }
            }
            if (m.PullDist > 0f)
            {
                int dx = Math.Sign(origin.X - e.Position.X);
                int dz = Math.Sign(origin.Z - e.Position.Z);
                for (int s = 0; s < (int)m.PullDist; s++)
                {
                    var nxt = new GridPos(e.Position.X + dx, e.Position.Y, e.Position.Z + dz);
                    if (world.GetTile(nxt.X, nxt.Y, nxt.Z) == MaterialType.Air) e.Position = nxt;
                    else break;
                }
            }
        }
    }

    // ── 武技 ──────────────────────────────────────────────────────

    private static void ExecuteTechnique(SpellSlot slot, PlayerController player, TileWorld3D world,
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
                // ── 新武技技能因子（Design B）────────────────────────────
                case "technique_sword":
                {
                    var sHit = new GridPos(p.X + fx * (slashOfs + rep * 3),
                                           p.Y + fy * (slashOfs + rep * 3), p.Z);
                    world.Explode(sHit.X, sHit.Y, sHit.Z, r);
                    ApplyElement(world, sHit, r, m);
                    break;
                }
                case "technique_punch":
                {
                    int pOfs = atHitPoint ? 0 : 2;
                    var pHit = new GridPos(p.X + fx * (pOfs + rep * 2), p.Y, p.Z);
                    world.Explode(pHit.X, pHit.Y, pHit.Z, Math.Max(1, r - 1));
                    ApplyElement(world, pHit, Math.Max(1, r - 1), m);
                    break;
                }
                case "technique_shield": // TODO-STUB: 防禦/反擊，暫以前方衝擊波佔位
                {
                    var shHit = new GridPos(p.X + fx * (slashOfs + rep * 2), p.Y, p.Z);
                    world.Explode(shHit.X, shHit.Y, shHit.Z, r + 1);
                    break;
                }
                // ── 舊武技技能因子（向後相容）────────────────────────────
                case "technique_slash":
                    var hit = new GridPos(p.X + fx * (slashOfs + rep * 3),
                                         p.Y + fy * (slashOfs + rep * 3), p.Z);
                    world.Explode(hit.X, hit.Y, hit.Z, r);
                    ApplyElement(world, hit, r, m);
                    break;

                case "technique_projectile":
                    int range = 15 + rep * 5;
                    for (int i = 1; i <= range; i++)
                    {
                        int tx = p.X + fx * i, ty = p.Y + fy * i;
                        var mat = world.GetTile(tx, ty, p.Z);
                        if (mat == MaterialType.Stone) break;
                        world.SetTile(tx, ty, p.Z, m.Water || m.Ice ? MaterialType.Water : MaterialType.Fire);
                        if (mat != MaterialType.Air) break;
                    }
                    break;

                case "technique_area":
                    int ar = r + 2 + rep;
                    world.Explode(p.X + fx * rep * 2, p.Y + fy * rep * 2, p.Z, ar);
                    ApplyElement(world, p, ar, m);
                    if (!m.Water && !m.Ice)
                        world.SpawnEffect("fire", p, new Dictionary<string, object?> { ["radius"] = r });
                    break;

                case "technique_beam":
                    for (int i = 1; i <= 25 + rep * 8; i++)
                    {
                        int tx = p.X + fx * i, ty = p.Y + fy * i;
                        if (!world.InBounds(tx, ty, p.Z)) break;
                        for (int dy = -1; dy <= 1; dy++)
                            if (world.GetTile(tx, ty + dy, p.Z) != MaterialType.Stone)
                                world.SetTile(tx, ty + dy, p.Z, m.Water ? MaterialType.Water : MaterialType.Fire);
                    }
                    break;

                case "technique_chain":
                    for (int c = 0; c < 3 + rep; c++)
                    {
                        var cp = new GridPos(p.X + fx * (3 + c * 3), p.Y + fy * (3 + c * 3), p.Z);
                        world.Explode(cp.X, cp.Y, cp.Z, Math.Max(1, r - c));
                    }
                    break;
            }
        }
    }

    private static void ApplyElement(TileWorld3D world, GridPos pos, int radius, in Mods m)
    {
        if (m.Fire)  world.SpawnEffect("fire",  pos, new Dictionary<string, object?> { ["radius"] = radius });
        if (m.Water) world.SpawnEffect("water", pos, new Dictionary<string, object?> { ["radius"] = radius });
    }

    // ── 變幻 ──────────────────────────────────────────────────────

    private static void ExecuteMorph(SpellSlot slot, PlayerController player, TileWorld3D world,
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
                world.Explode(p.X, p.Y + 3, p.Z, 2);
                break;
            case "morph_invisible":
                world.SpawnEffect("water", p, new Dictionary<string, object?> { ["radius"] = 2 });
                break;
        }
    }

    // ── 位移 ──────────────────────────────────────────────────────

    private static void ExecuteDisplacement(SpellSlot slot, PlayerController player, TileWorld3D world)
    {
        int fx = player.Facing.X, fy = player.Facing.Y;
        switch (slot.Totem!.Id)
        {
            case "displace_dash":
                for (int i = 0; i < 10; i++)
                {
                    var n = new GridPos(player.Position.X + fx, player.Position.Y + fy, player.Position.Z);
                    if (world.GetTile(n.X, n.Y, n.Z) != MaterialType.Air) break;
                    player.Position = n;
                }
                break;
            case "displace_teleport":
                for (int i = 20; i >= 1; i--)
                {
                    var tp = new GridPos(player.Position.X + fx * i, player.Position.Y + fy * i, player.Position.Z);
                    if (world.GetTile(tp.X, tp.Y, tp.Z) == MaterialType.Air) { player.Position = tp; break; }
                }
                break;
            case "displace_dodge":
                for (int i = 0; i < 5; i++)
                {
                    var b = new GridPos(player.Position.X - fx, player.Position.Y - fy, player.Position.Z);
                    if (world.GetTile(b.X, b.Y, b.Z) != MaterialType.Air) break;
                    player.Position = b;
                }
                break;
        }
    }

    // ── 召喚 ──────────────────────────────────────────────────────

    private static void ExecuteSummon(SpellSlot slot, PlayerController player, TileWorld3D world,
        GridPos? originOverride = null)
    {
        var origin = originOverride ?? player.Position;
        var sp = new GridPos(origin.X + player.Facing.X * 4,
                             origin.Y + player.Facing.Y * 4, origin.Z);
        switch (slot.Totem!.Id)
        {
            case "summon_minion":
                world.SpawnEffect("fire", sp, new Dictionary<string, object?> { ["radius"] = 1 });
                break;
            case "summon_turret":
                for (int dy = -1; dy <= 1; dy++)
                    world.SetTile(sp.X, sp.Y + dy, sp.Z, MaterialType.Stone);
                break;
            case "summon_guardian":
                world.SpawnEffect("water", sp, new Dictionary<string, object?> { ["radius"] = 2 });
                break;
        }
    }

    // ── 領域 ──────────────────────────────────────────────────────

    private static void ExecuteDomain(SpellSlot slot, PlayerController player, TileWorld3D world,
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
