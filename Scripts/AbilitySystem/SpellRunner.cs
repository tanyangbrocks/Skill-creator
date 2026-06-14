namespace SkillCreator.AbilitySystem;

using SkillCreator.AbilitySystem.Data;
using SkillCreator.AbilitySystem.VM;
using SkillCreator.Snapshot;
using SkillCreator.World;

// ─────────────────────────────────────────────────────────────────────────────
//  SpellRunner — 持久化執行容器，讓 Wait 積木產生真實跨幀延遲
//
//  用法：
//    1. Main.cs 建立一個 SpellRunner 實例，每幀呼叫 Update(delta)
//    2. DirectCast 技能整構透過 SpellRunner.Submit 提交，而非同步執行
//    3. Projectile / Contact 命中仍用 SpellCaster.ExecuteEffects（同步，不過 Runner）
//
//  架構：
//    • 一個 ActiveSpell 代表一次正在執行中的技能整構（有自己的 Context + Loop）
//    • 每幀 Update 對每個 ActiveSpell 呼叫一次 Advance(delta)
//    • Advance 把真實 delta 傳給 ExecutionLoop.Step → Wait 倒數真實時間
//    • InvokeSpell 連段在 Runner 內產生新的 ActiveSpell（parallel 執行）
// ─────────────────────────────────────────────────────────────────────────────
public sealed class SpellRunner
{
    // MaxComboDepth 統一定義於 SafetyGuard.MaxComboDepth

    private sealed class ActiveSpell
    {
        public ExecutionContext               Ctx;
        public ExecutionLoop                  Loop;
        public Dictionary<string, SpellSlot>  SlotByRef;
        public PlayerController               Player;
        public TileWorld3D                    World;
        public EnemyManager?                  Enemies;
        public SpellLoadout?                  Loadout;
        public int                            ComboDepth;
        public bool                           AtHitPoint;
        // S-9：供 PruneAfter 使用
        public float                          SubmittedAt;   // GameClock.TotalTicks 提交時間
        public float                          MpCost;        // 已扣除的 MP，退還用

        public ActiveSpell(ExecutionContext ctx, ExecutionLoop loop,
            Dictionary<string, SpellSlot> slotByRef,
            PlayerController player, TileWorld3D world,
            EnemyManager? enemies, SpellLoadout? loadout,
            int comboDepth, bool atHitPoint)
        {
            Ctx = ctx; Loop = loop; SlotByRef = slotByRef;
            Player = player; World = world;
            Enemies = enemies; Loadout = loadout;
            ComboDepth = comboDepth; AtHitPoint = atHitPoint;
        }
    }

    private readonly List<ActiveSpell> _active = new();

    // 目前執行中的技能整構數量（可供 HUD 顯示）
    public int ActiveCount => _active.Count;

    // ── 提交一個新技能整構（DirectCast 施放時呼叫）────────────────────

    public void Submit(SpellArray spell, PlayerController player, TileWorld3D world,
        EnemyManager? enemies = null, SpellLoadout? loadout = null,
        int comboDepth = 0, bool atHitPoint = false, GridPos? fixedOrigin = null,
        EntityInfo? hitTarget = null)
    {
        var blocks = spell.Blocks.Count > 0
            ? spell.Blocks
            : BlockAutoGenerator.Generate(spell);
        if (blocks.Count == 0) return;

        var ctx  = new ExecutionContext(SpellCompiler.Compile(blocks, spell));
        if (hitTarget.HasValue) ctx.CurrentIterEntity = hitTarget;
        if (enemies != null)
            ctx.EntityQuery = r => SpellCaster.QueryEnemies(enemies, player, r);
        ctx.RaycastQuery     = (start, dx, dy, dist) => world.Raycast(start, dx, dy, dist);
        ctx.FocalPointQuery  = () => player.MouseGridPos;
        // S-10：快照 delegates（Anchor/Rollback 積木用）
        var capturedRunner = this;
        ctx.AnchorAction   = radius =>
            SnapshotManager.TakeSnapshot(player.Position, radius, player, enemies, world);
        ctx.RollbackAction = () =>
            SnapshotManager.ApplyLatest(player, enemies, world, capturedRunner);
        ctx.PlayerStatsQuery = key => key switch
        {
            "hp"    => player.Hp,
            "mp"    => player.Mp,
            "hpPct" => player.Hp / player.MaxHp,
            "mpPct" => player.Mp / player.MaxMp,
            _       => 0f,
        };
        ctx.FixedOrigin     = fixedOrigin;
        var capturedSpell = spell;
        ctx.SetActivationMode = mode =>
            capturedSpell.ActivationType = (AbilityActivationType)mode;
        var loop = new ExecutionLoop(new SafetyGuard());
        var slotByRef = SpellCaster.BuildSlotLookup(spell);

        var entry = new ActiveSpell(ctx, loop, slotByRef,
            player, world, enemies, loadout, comboDepth,
            atHitPoint: atHitPoint || fixedOrigin.HasValue)
        {
            SubmittedAt = GameClock.TotalTicks,
            MpCost      = AbilityPointCalculator.CalculateMpCost(spell),
        };
        _active.Add(entry);
    }

    // ── S-9：清除錨點後提交的技能整構並退還 MP ──────────────────────

    /// <summary>移除所有在 anchorTimestamp 之後提交的技能整構並退還其已扣除的 MP。</summary>
    public void PruneAfter(float anchorTimestamp)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var s = _active[i];
            if (s.SubmittedAt > anchorTimestamp)
            {
                s.Player.Mp = Math.Min(s.Player.MaxMp, s.Player.Mp + s.MpCost);
                _active.RemoveAt(i);
            }
        }
    }

    // ── 每幀驅動（Main._Process 呼叫）────────────────────────────

    public void Update(float delta)
    {
        if (_active.Count == 0) return;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var s = _active[i];
            s.Loop.ResetTick();
            Advance(s, delta);
            if (s.Ctx.IsFinished) _active.RemoveAt(i);
        }
    }

    // ── 單一技能整構推進 ──────────────────────────────────────────────

    private void Advance(ActiveSpell s, float delta)
    {
        float stepDelta = delta;  // 第一次 Step 才帶真實 delta，其後同幀為 0

        int safety = 0;
        while (!s.Ctx.IsFinished && safety++ < SafetyGuard.MaxStepsPerCast)
        {
            s.Loop.Step(s.Ctx, stepDelta);
            stepDelta = 0f;

            // Wait / OnReceive / WaitingCondition / EdgeTrigger 等待中：本幀停止推進，下幀繼續
            if (s.Ctx.State == ExecutionState.Waiting            ||
                s.Ctx.State == ExecutionState.WaitingSignal       ||
                s.Ctx.State == ExecutionState.WaitingCondition    ||
                s.Ctx.State == ExecutionState.WaitingRisingEdge   ||
                s.Ctx.State == ExecutionState.WaitingFallingEdge) break;

            // InvokeTotem：共用 helper 自動處理 ForEach 定位（EffectOriginOverride）
            if (s.Ctx.PendingInvokeTotem != null)
            {
                SpellCaster.ConsumeInvokeTotem(s.Ctx, s.SlotByRef, s.Player, s.World, s.AtHitPoint, s.Enemies);
                continue;
            }

            // InvokeSpell 連段：提交為新的 ActiveSpell（與目前技能整構並行執行）
            if (s.Ctx.PendingInvokeSpell != null)
            {
                string nextName = s.Ctx.PendingInvokeSpell;
                s.Ctx.PendingInvokeSpell = null;
                TriggerCombo(nextName, s);
                continue;
            }

            // SetEntityProp 扣血
            if (s.Ctx.PendingEntityDamageId >= 0)
            {
                SpellCaster.ConsumeEntityDamage(s.Ctx, s.Enemies);
                continue;
            }

            // SetEntityProp x/y 位移
            if (s.Ctx.PendingEntityMoveId >= 0)
            {
                SpellCaster.ConsumeEntityMove(s.Ctx, s.Enemies);
                continue;
            }

            // 沒有 pending → 下一輪 Step 繼續或已完成
        }
    }

    // ── 連段邏輯 ──────────────────────────────────────────────────

    private void TriggerCombo(string spellName, ActiveSpell src)
    {
        if (src.Loadout is null || src.ComboDepth >= SafetyGuard.MaxComboDepth) return;

        SpellArray? next = null;
        for (int i = 0; i < SpellLoadout.MaxSlots; i++)
        {
            var s = src.Loadout.GetSlot(i);
            if (s?.Name == spellName) { next = s; break; }
        }
        if (next is null) return;

        float cost = AbilityPointCalculator.CalculateMpCost(next);
        if (!SafetyGuard.HasMp(src.Player.Mp, cost)) return;

        src.Player.Mp -= cost;
        src.Player.SetCastCooldown(next.CastDelay);
        Submit(next, src.Player, src.World, src.Enemies, src.Loadout,
               src.ComboDepth + 1, src.AtHitPoint);
    }
}
