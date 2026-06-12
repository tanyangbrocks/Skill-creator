namespace SkillCreator.World;

using SkillCreator.AbilitySystem;
using SkillCreator.AbilitySystem.Elemental;
using SkillCreator.Snapshot;
using SkillCreator.World.Items;
using SkillCreator.World.Materials;

public class PlayerController : IElementalTarget, ISnapshottable
{
    // ── IElementalTarget 實作（W-3）─────────────────────────────────
    int  IElementalTarget.EntityId => -1;  // 玩家固定 -1
    void IElementalTarget.TakeDirectDamage(float amount) => TakeDamage(amount);

    /// <summary>玩家身上的元素 Aura 與元素狀態效果管理器。</summary>
    public ElementalAuraComponent Aura  { get; } = new();

    // ── W-5a 完整數值系統 ──────────────────────────────────────────
    public CharacterStats Stats { get; } = new();

    // ── W-5b 角色狀態系統 ──────────────────────────────────────────
    public CharacterState State { get; } = new();

    public GridPos Position     { get; set; }
    // Facing 只追蹤水平方向，確保投射物永遠往左/右打
    public GridPos Facing       { get; private set; } = new GridPos(1, 0);
    // 滑鼠對應的世界格座標（由 Main._Process 每幀更新）
    public GridPos MouseGridPos    { get; set; }
    // 滑鼠指向格的面法線（由 Main._Process 每幀更新；SideScroll2D 預設朝上 -Y）
    public GridPos MouseFaceNormal { get; set; } = new GridPos(0, -1, 0);

    public Inventory       Inventory  { get; } = new();
    public PlayerEquipment Equipment  { get; } = new();
    public const float MiningRange = 5f;

    // ── 碰撞體尺寸（由 WorldScale 驅動）──────────────────────────────────
    public const int BodyH  = WorldScale.PlayerH;          // = 32 at Grain=16
    public const int BodyW  = 1;
    private const int StepH = BodyH / 16;                  // max step-up tiles (2 at Grain=16)

    // 檢查從 (x, yHead) 向下 height 格是否全為 Air。
    private static bool ColumnClear(TileWorld3D w, int x, int yHead, int height, int z)
    {
        for (int i = 0; i < height; i++)
            if (w.GetTile(x, yHead + i, z) != MaterialType.Air)
                return false;
        return true;
    }

    public float Hp { get; set; }
    /// <summary>最大 HP（由 Stats.MaxHpBase 決定；不再是 const，支援動態調整）。</summary>
    public float MaxHp => Stats.MaxHpBase;

    public float Mp { get; set; }
    /// <summary>最大 MP = Stats.MaxMpBase + 裝備加成。</summary>
    public float MaxMp => Stats.MaxMpBase + Equipment.TotalMpBonus;

    // ── XP / 等級 / 境界（星盟通用戰力等級）──────────────────────
    public int   Level { get; private set; } = 1;
    public float Xp    { get; private set; } = 0f;

    // Main.cs 每幀讀取後清除；true = 本幀剛突破境界
    public bool JustBrokeThrough { get; set; }

    public static int XpRequired(int level) => level * 100;

    public static string GetTierName(int level) => level switch
    {
        < 10  => "學徒",
        < 20  => "超凡",
        < 35  => "征戰",
        < 50  => "主將",
        < 65  => "群星",
        < 80  => "耀日",
        < 100 => "巔峰",
        _     => "特異",
    };

    // AP 上限對應境界（⚠️ 數值暫定，待平衡）
    public static int TierApCap(int level) => level switch
    {
        < 10  =>   50,
        < 20  =>  120,
        < 35  =>  200,
        < 50  =>  350,
        < 65  =>  500,
        < 80  =>  700,
        < 100 =>  900,
        _     => 1500,
    };

    // 境界徽章顏色（for HUD）
    public static (float R, float G, float B) GetTierColor(int level) => level switch
    {
        < 10  => (0.65f, 0.65f, 0.65f), // 灰
        < 20  => (0.55f, 0.60f, 0.75f), // 深藍灰
        < 35  => (0.30f, 0.82f, 0.38f), // 綠
        < 50  => (0.28f, 0.58f, 0.95f), // 藍
        < 65  => (0.95f, 0.85f, 0.18f), // 黃
        < 80  => (0.95f, 0.32f, 0.25f), // 紅
        < 100 => (0.80f, 0.28f, 0.95f), // 紫
        _     => (1.00f, 1.00f, 1.00f), // 白（特異以上）
    };

    public string TierName => GetTierName(Level);

    public void GainXp(float amount)
    {
        if (!IsAlive) return;
        string tierBefore = TierName;
        Xp += amount;
        while (Xp >= XpRequired(Level))
        {
            Xp -= XpRequired(Level);
            Level++;
        }
        if (TierName != tierBefore) JustBrokeThrough = true;
    }

    private float _moveCooldown = 0f;
    private float _castCooldown = 0f;
    private const float MoveInterval = 0.12f;

    // 重力與跳躍（等加速度物理）
    private float _vy     = 0f;   // 縱向速度（tiles/s，正向下）
    private float _fractY = 0f;   // 次格累積量（保留小數）
    private const float Gravity      = 30f;  // tiles/s²
    private const float MaxFallSpeed = 20f;  // tiles/s
    private const float JumpSpeed    = 20f;  // tiles/s（向上）

    public bool IsAlive  => Hp > 0f;
    public bool CanMove  => _moveCooldown <= 0f;
    public bool CanCast  => _castCooldown <= 0f;

    public void TakeDamage(float amount)
    {
        // ── 防禦計算（W-5a BaseDefense + 裝備 + W-3 鏽化懲罰）────────
        float totalDefFlat = Stats.BaseDefense + Equipment.TotalDefFlat;
        float effectiveDefFlat = totalDefFlat * (1f - Aura.DefensePenalty);
        float afterDef = Math.Max(0f, amount - effectiveDefFlat);
        // W-5a DamageReduction（% 減傷，0=無；⚠️ stub，預設 0）
        float reduced = afterDef * (1f - Math.Clamp(Stats.DamageReduction, 0f, 1f));
        // ──────────────────────────────────────────────────────────────

        // ── 行動攔截鉤子：傷害層（Phase 4 第三層）────────────────────
        var dmgResult = ActionBus.Dispatch(new PlayerDamageAction(reduced));
        if (dmgResult == null) return; // 攔截：傷害取消（完全免傷）
        float intercepted = dmgResult is PlayerDamageAction pda ? pda.Amount : reduced;
        // ──────────────────────────────────────────────────────────────

        // ── 元素狀態效果修改（W-3）────────────────────────────────────
        // 結凍：期間受傷害加成
        float finalDmg = intercepted * (1f + Aura.DamageTakenBonus);
        // ──────────────────────────────────────────────────────────────

        float newHp = Math.Max(0f, Hp - finalDmg);

        // ── 行動攔截鉤子：死亡層（死亡替代）────────────────────────
        if (newHp <= 0f && IsAlive)
        {
            var deathResult = ActionBus.Dispatch(new PlayerDeathAction());
            if (deathResult == null)
            {
                // 攔截：取消死亡，玩家存活於 1 HP
                Hp = 1f;
                CombatState.OnPlayerTookDamage();
                return;
            }
        }
        // ──────────────────────────────────────────────────────────────

        Hp = newHp;
        if (finalDmg > 0f)
        {
            CombatState.OnHit?.Invoke(Position, finalDmg, true);
            CombatState.OnPlayerTookDamage();
        }
    }

    public PlayerController(GridPos startPos)
    {
        Position = startPos;
        Hp = Stats.MaxHpBase;  // 初始滿 HP
        Mp = MaxMp;
    }

    /// <summary>
    /// 每幀由 Main._Process 在 Tick() 之前呼叫：設置環境依賴的生存狀態旗標。
    /// · 氧氣：站在 Water 格 → 缺氧
    /// · 體溫：基礎室溫 + 元素 Aura 溫度偏移（Fire 加熱 / Ice‧Water 降溫）
    /// </summary>
    public void UpdateEnvironment(TileWorld3D world)
    {
        var tile = world.GetTile(Position.X, Position.Y + BodyH - 1, Position.Z);
        State.IsOxygenDeprived  = tile == MaterialType.Water;
        State.AmbientTemperature = CharacterState.DefaultAmbientTemp + Aura.AuraTemperatureShift;
    }

    public void Tick(float delta)
    {
        Aura.Process(delta, this);
        // W-5b：生存傷害直接扣血，繞過防禦管線
        float survivalDmg = State.Tick(delta, CombatState.InCombat);
        if (survivalDmg > 0f) Hp = MathF.Max(0f, Hp - survivalDmg);
        if (_moveCooldown > 0f) _moveCooldown -= delta;
        if (_castCooldown > 0f) _castCooldown -= delta;
        Mp = MathF.Min(MaxMp, Mp + Stats.MpRegenRate * delta);  // W-5a：MpRegen 來自 Stats
        if (Mp > MaxMp) Mp = MaxMp; // 裝備卸下時上限可能縮小
    }

    // 水平移動（A/D），同時更新朝向；支援地形跨坡（最多 StepH 格）
    public bool TryMove(TileWorld3D world, int dx, int dy)
    {
        if (!CanMove || Aura.IsImmobilized) return false;
        int nx = Position.X + dx, ny = Position.Y + dy, nz = Position.Z;

        if (!ColumnClear(world, nx, ny, BodyH, nz))
        {
            if (dy != 0) return false;
            bool stepped = false;
            for (int s = 1; s <= StepH; s++)
            {
                // target column clear + head clearance at current X
                if (ColumnClear(world, nx, ny - s, BodyH, nz)
                 && ColumnClear(world, Position.X, Position.Y - s, s, nz))
                { ny -= s; stepped = true; break; }
            }
            if (!stepped) return false;
        }

        Position = new GridPos(nx, ny, nz);
        if (dx != 0) Facing = new GridPos(Math.Sign(dx), 0);
        _moveCooldown = MoveInterval / Stats.MoveSpeedMult * (1f + Aura.SpeedPenalty);
        return true;
    }

    /// <summary>
    /// 相機相對方向移動（WASD 換算後呼叫）。
    /// dx/dz 各為 -1/0/1，支援斜向（dx 和 dz 同時非零）。
    /// 每次消耗一個 moveCooldown；Facing 更新為實際移動的 XZ 方向。
    /// 含爬坡：整批移動最多爬升 StepH 格，不會雙重爬坡。
    /// </summary>
    public bool TryMoveDir(TileWorld3D world, int dx, int dz)
    {
        if (!CanMove || Aura.IsImmobilized) return false;
        if (dx == 0 && dz == 0) return false;

        int px = Position.X, py = Position.Y, pz = Position.Z;
        int nx = px, ny = py, nz = pz;
        bool stepped = false;

        // ── X 軸（含爬坡）────────────────────────────────────────────────────
        if (dx != 0)
        {
            int tx = px + dx;
            if (ColumnClear(world, tx, ny, BodyH, pz))
            {
                nx = tx;
            }
            else
            {
                for (int s = 1; s <= StepH && !stepped; s++)
                {
                    if (ColumnClear(world, tx, py - s, BodyH, pz)
                     && ColumnClear(world, px, py - s, s, pz))
                    { nx = tx; ny = py - s; stepped = true; }
                }
            }
        }

        // ── Z 軸（含爬坡；step-up 從原始 py 計算，防二次爬坡）──────────────
        if (dz != 0)
        {
            int tz = pz + dz;
            if (ColumnClear(world, nx, ny, BodyH, tz))
            {
                nz = tz;
            }
            else if (!stepped)
            {
                for (int s = 1; s <= StepH; s++)
                {
                    if (ColumnClear(world, nx, py - s, BodyH, tz)
                     && ColumnClear(world, nx, py - s, s, pz))
                    { nz = tz; ny = py - s; stepped = true; break; }
                }
            }
        }

        if (nx == px && ny == py && nz == pz) return false;

        Position = new GridPos(nx, ny, nz);
        int fDx = nx - px, fDz = nz - pz;
        if (fDx != 0 || fDz != 0)
            Facing = new GridPos(Math.Sign(fDx), 0, Math.Sign(fDz));
        _moveCooldown = MoveInterval / Stats.MoveSpeedMult * (1f + Aura.SpeedPenalty);
        return true;
    }

    // 深度移動（W/S，TPS/FPS 模式），在世界 Z 軸方向前進後退
    // 支援地形跨坡：前方被阻擋時最多爬升 StepH 格
    public bool TryMoveDepth(TileWorld3D world, int dz)
    {
        if (!CanMove || Aura.IsImmobilized) return false;
        int nz = Position.Z + dz;
        if ((uint)nz >= (uint)world.Depth) return false;

        int py = Position.Y, ny = py;
        if (!ColumnClear(world, Position.X, py, BodyH, nz))
        {
            bool stepped = false;
            for (int s = 1; s <= StepH; s++)
            {
                if (ColumnClear(world, Position.X, py - s, BodyH, nz)
                 && ColumnClear(world, Position.X, py - s, s, Position.Z))
                { ny = py - s; stepped = true; break; }
            }
            if (!stepped) return false;
        }

        Position = new GridPos(Position.X, ny, nz);
        _moveCooldown = MoveInterval / Stats.MoveSpeedMult * (1f + Aura.SpeedPenalty);
        return true;
    }

    // 重力 + 跳躍物理（每幀由 Main._Process 呼叫）
    public void ApplyPhysics(TileWorld3D world, float delta)
    {
        int pz = Position.Z;
        _vy     = Math.Clamp(_vy + Gravity * delta, -MaxFallSpeed, MaxFallSpeed);
        _fractY += _vy * delta;

        // 向下移動（查腳底 Y+BodyH）
        while (_fractY >= 1f)
        {
            if (world.GetTile(Position.X, Position.Y + BodyH, pz) != MaterialType.Air)
            { _vy = 0f; _fractY = 0f; return; }
            Position = new GridPos(Position.X, Position.Y + 1, pz);
            _fractY -= 1f;
        }
        // 向上移動（查頭頂 Y-1）
        while (_fractY <= -1f)
        {
            if (world.GetTile(Position.X, Position.Y - 1, pz) != MaterialType.Air)
            { _vy = 0f; _fractY = 0f; return; }
            Position = new GridPos(Position.X, Position.Y - 1, pz);
            _fractY += 1f;
        }
        // 已落地時歸零，防止 _vy 持續累積
        if (_vy > 0f && world.GetTile(Position.X, Position.Y + BodyH, pz) != MaterialType.Air)
        { _vy = 0f; _fractY = 0f; }
    }

    public bool IsOnGround(TileWorld3D world)
        => world.GetTile(Position.X, Position.Y + BodyH, Position.Z) != MaterialType.Air;

    public void StartJump()
    {
        _vy     = -JumpSpeed;
        _fractY = 0f;
    }

    public void SetCastCooldown(float seconds) => _castCooldown = seconds;

    /// <summary>在指定位置復活：HP / MP 回滿，物理速度歸零，冷卻清除。</summary>
    public void Respawn(GridPos spawnPos)
    {
        Position  = spawnPos;
        Hp        = MaxHp;
        Mp        = MaxMp;
        _vy       = 0f;
        _fractY   = 0f;
        _moveCooldown = 0f;
        _castCooldown = 0f;
    }

    // ── ISnapshottable（S-7）──────────────────────────────────────

    public EntitySnapshot TakeSnapshot() => new(
        EntityId:  EntitySnapshot.PlayerId,
        Position:  Position,
        Hp:        Hp,
        Mp:        Mp,
        WasAlive:  IsAlive,
        Aura:      Aura.TakeSnapshot(),
        CharState: State.TakeSnapshot(),
        CharStats: CharStatsSnapshot.From(Stats)
    );

    public void RestoreFromSnapshot(EntitySnapshot snap)
    {
        Position = snap.Position;
        Hp       = snap.Hp;
        Mp       = snap.Mp;
        _vy      = 0f;
        _fractY  = 0f;
        Aura.RestoreFromSnapshot(snap.Aura);
        if (snap.CharState is { } cs)  State.RestoreFromSnapshot(cs);
        if (snap.CharStats is { } cst) cst.ApplyTo(Stats);
    }

    // ── 採掘 ──────────────────────────────────────────────────────

    public GridPos? MiningTarget   { get; private set; }
    public float    MiningProgress { get; private set; }

    public void CancelMining()
    {
        MiningTarget   = null;
        MiningProgress = 0f;
    }

    // 回傳 true 代表本幀破壞了方塊
    public bool TickMining(TileWorld3D world, GridPos target, float delta)
    {
        var mat  = world.GetTile(target.X, target.Y, target.Z);
        var data = MaterialRegistry.Get(mat);

        if (!data.IsMineable || data.RequiredToolTier > Inventory.ActiveToolTier)
        {
            CancelMining();
            return false;
        }

        // 換了目標 → 重置進度
        if (MiningTarget != target)
        {
            MiningTarget   = target;
            MiningProgress = 0f;
        }

        // 累加採掘進度（以 60fps 為基準，乘上工具速度倍率）
        MiningProgress += Inventory.ActiveMiningSpeedMult * delta * 60f;

        if (MiningProgress >= data.Hardness)
        {
            world.DestroyTile(target);
            CancelMining();
            return true;
        }
        return false;
    }
}
