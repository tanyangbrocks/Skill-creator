namespace SkillCreator.World;

using SkillCreator.World.Materials;

// 細胞自動機世界模擬（純 C#，無 Godot 依賴）
public class TileWorld : IWorldInterface
{
    public int Width  { get; }
    public int Height { get; }

    private readonly TileCell[] _cells;
    private readonly bool[] _updated;   // 防止同幀雙重更新
    private readonly Random _rng = new(42);
    private int _frame = 0;

    // 實體層（Phase 2 基礎，Phase 3 擴充）
    private readonly List<WorldEntity> _entities = new();
    private int _nextEntityId = 1;

    // IWorldInterface 事件
    public event Action<WorldEntity, WorldEntity, float>? OnEntityHit;
    public event Action<GridPos, MaterialType>? OnTileDestroyed;
    public event Action<WorldEntity>? OnEntityDied;
#pragma warning disable CS0067
    public event Action<string, object?>? OnPlayerAction;
#pragma warning restore CS0067
    // 爆炸事件：(center, radius) — EnemyManager 訂閱以計算炸傷
    public event Action<GridPos, int>? OnExplosion;

    public TileWorld(int width = 200, int height = 150)
    {
        Width  = width;
        Height = height;
        _cells  = new TileCell[Width * Height];
        _updated = new bool[Width * Height];
        FillDefault();
    }

    // ── 預設場景：石底、幾塊木頭 ────────────────────────────────
    private void FillDefault()
    {
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            MaterialType mat;
            if (y >= Height - 8)
                mat = MaterialType.Stone;
            else if (y >= Height - 12 && x % 6 < 2)
                mat = MaterialType.Dirt;
            else
                mat = MaterialType.Air;

            Set(x, y, mat);
        }

        // 幾塊木頭柱子
        for (int i = 0; i < 4; i++)
        {
            int bx = 30 + i * 40;
            for (int by = Height - 20; by < Height - 8; by++)
                Set(bx, by, MaterialType.Wood);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  模擬 Tick
    // ════════════════════════════════════════════════════════════

    public void Tick()
    {
        Array.Clear(_updated, 0, _updated.Length);
        _frame++;

        // 由下往上、奇偶幀交替左右方向，避免方向性偏差
        bool leftFirst = (_frame % 2 == 0);

        for (int y = Height - 1; y >= 0; y--)
        for (int xi = 0; xi < Width; xi++)
        {
            int x = leftFirst ? xi : (Width - 1 - xi);
            int idx = Idx(x, y);
            if (_updated[idx]) continue;

            ref var cell = ref _cells[idx];
            switch (MaterialRegistry.Get(cell.Type).Physics)
            {
                case PhysicsCategory.Powder: UpdatePowder(x, y); break;
                case PhysicsCategory.Liquid: UpdateLiquid(x, y); break;
                case PhysicsCategory.Gas:    UpdateGas(x, y);    break;
                case PhysicsCategory.Static: UpdateStatic(x, y); break;
            }
        }
    }

    // ── 粉末（沙）──────────────────────────────────────────────

    private void UpdatePowder(int x, int y)
    {
        if (TryMove(x, y, x, y + 1)) return;
        bool lr = _rng.Next(2) == 0;
        if (TryMove(x, y, x + (lr ? -1 : 1), y + 1)) return;
        TryMove(x, y, x + (lr ? 1 : -1), y + 1);
    }

    // ── 液體（水 / 岩漿）───────────────────────────────────────

    private void UpdateLiquid(int x, int y)
    {
        var mat = _cells[Idx(x, y)].Type;

        // 岩漿每 3 幀才更新一次（流動緩慢）
        if (mat == MaterialType.Lava && _frame % 3 != 0) return;

        // 落下
        if (TryMove(x, y, x, y + 1)) return;
        if (TryMove(x, y, x - 1, y + 1)) return;
        if (TryMove(x, y, x + 1, y + 1)) return;

        // 橫向流動
        bool lr = (_frame % 2 == 0);
        int spread = (mat == MaterialType.Water) ? 3 : 1;
        for (int i = 1; i <= spread; i++)
        {
            int dx = lr ? -i : i;
            if (TryMove(x, y, x + dx, y)) return;
        }
        for (int i = 1; i <= spread; i++)
        {
            int dx = lr ? i : -i;
            if (TryMove(x, y, x + dx, y)) return;
        }

        // 岩漿：點燃相鄰可燃物
        if (mat == MaterialType.Lava)
            TryIgniteAround(x, y, chance: 0.1f);
    }

    // ── 氣體（火 / 蒸汽）───────────────────────────────────────

    private void UpdateGas(int x, int y)
    {
        ref var cell = ref _cells[Idx(x, y)];

        cell.Timer--;
        if (cell.Timer <= 0)
        {
            Set(x, y, cell.Type == MaterialType.Fire ? MaterialType.Ash : MaterialType.Air);
            return;
        }

        if (cell.Type == MaterialType.Fire)
        {
            // 火：上升、擴散、點燃相鄰
            if (!TryMove(x, y, x, y - 1))
            {
                bool lr = _rng.Next(2) == 0;
                TryMove(x, y, x + (lr ? -1 : 1), y - 1);
            }
            TryIgniteAround(x, y, chance: 0.08f);

            // 接觸水：水變蒸汽，火熄滅
            if (HasAdjacent(x, y, MaterialType.Water))
            {
                ExtinguishFire(x, y);
                return;
            }
        }
        else // Steam
        {
            // 蒸汽：上升
            if (!TryMove(x, y, x, y - 1))
            {
                bool lr = _rng.Next(2) == 0;
                if (!TryMove(x, y, x + (lr ? -1 : 1), y))
                    TryMove(x, y, x + (lr ? 1 : -1), y);
            }
        }
    }

    // ── 靜態（木）─────────────────────────────────────────────

    private void UpdateStatic(int x, int y)
    {
        ref var cell = ref _cells[Idx(x, y)];
        if (cell.Type != MaterialType.Wood) return;

        // 木頭燃燒中（Timer > 0）
        if (cell.Timer > 0)
        {
            cell.Timer--;
            if (cell.Timer <= 0)
            {
                Set(x, y, MaterialType.Ash);
                return;
            }
            // 燃燒時散出火苗到上方空氣
            if (_rng.NextDouble() < 0.15 && InBounds(x, y - 1) && TypeAt(x, y - 1) == MaterialType.Air)
                SetFire(x, y - 1);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  輔助函式
    // ════════════════════════════════════════════════════════════

    private bool TryMove(int fx, int fy, int tx, int ty)
    {
        if (!InBounds(tx, ty)) return false;
        var from = _cells[Idx(fx, fy)].Type;
        var to   = _cells[Idx(tx, ty)].Type;

        // 可移動條件：目標是空氣，或目標密度更低（液體沉降）
        var fromData = MaterialRegistry.Get(from);
        var toData   = MaterialRegistry.Get(to);
        if (to != MaterialType.Air && !(toData.Physics == PhysicsCategory.Liquid && fromData.Density > toData.Density))
            return false;

        // 交換
        (_cells[Idx(fx, fy)], _cells[Idx(tx, ty)]) = (_cells[Idx(tx, ty)], _cells[Idx(fx, fy)]);
        _updated[Idx(tx, ty)] = true;
        return true;
    }

    private void TryIgniteAround(int x, int y, float chance)
    {
        Span<(int, int)> neighbors = stackalloc (int, int)[] { (0,-1),(0,1),(-1,0),(1,0) };
        foreach (var (dx, dy) in neighbors)
        {
            int nx = x + dx, ny = y + dy;
            if (!InBounds(nx, ny)) continue;
            var mat = TypeAt(nx, ny);
            if (MaterialRegistry.Get(mat).IsFlammable && _rng.NextDouble() < chance)
                IgniteMaterial(nx, ny);
        }
    }

    private void IgniteMaterial(int x, int y)
    {
        ref var cell = ref _cells[Idx(x, y)];
        var data = MaterialRegistry.Get(cell.Type);
        if (!data.IsFlammable || cell.Timer > 0) return;
        cell.Timer = (short)_rng.Next(data.BurnDurationMin, data.BurnDurationMax + 1);
    }

    private void SetFire(int x, int y)
    {
        ref var cell = ref _cells[Idx(x, y)];
        cell.Type    = MaterialType.Fire;
        cell.Variant = (byte)_rng.Next(256);
        cell.Timer   = (short)_rng.Next(30, 90);
    }

    private void ExtinguishFire(int x, int y)
    {
        Set(x, y, MaterialType.Air);
        // 將附近水格轉為蒸汽
        Span<(int, int)> neighbors = stackalloc (int, int)[] { (0,-1),(0,1),(-1,0),(1,0) };
        foreach (var (dx, dy) in neighbors)
        {
            int nx = x + dx, ny = y + dy;
            if (InBounds(nx, ny) && TypeAt(nx, ny) == MaterialType.Water)
            {
                ref var wc = ref _cells[Idx(nx, ny)];
                wc.Type  = MaterialType.Steam;
                wc.Timer = (short)_rng.Next(60, 120);
            }
        }
    }

    private bool HasAdjacent(int x, int y, MaterialType mat)
    {
        Span<(int, int)> n = stackalloc (int, int)[] { (0,-1),(0,1),(-1,0),(1,0) };
        foreach (var (dx, dy) in n)
        {
            int nx = x + dx, ny = y + dy;
            if (InBounds(nx, ny) && TypeAt(nx, ny) == mat) return true;
        }
        return false;
    }

    // ════════════════════════════════════════════════════════════
    //  公開讀寫 API
    // ════════════════════════════════════════════════════════════

    public TileCell GetCell(int x, int y) => _cells[Idx(x, y)];

    public MaterialType TypeAt(int x, int y) =>
        InBounds(x, y) ? _cells[Idx(x, y)].Type : MaterialType.Stone;

    public void Set(int x, int y, MaterialType type)
    {
        if (!InBounds(x, y)) return;
        ref var c = ref _cells[Idx(x, y)];
        c.Type    = type;
        c.Variant = (byte)_rng.Next(256);
        c.Timer   = type switch
        {
            MaterialType.Fire  => (short)_rng.Next(30, 90),
            MaterialType.Steam => (short)_rng.Next(60, 120),
            _                  => (short)0,
        };
    }

    // 在半徑內摧毀 tiles（爆炸效果）
    public void Explode(int cx, int cy, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy <= radius * radius)
                Set(cx + dx, cy + dy, MaterialType.Air);
        }
        OnExplosion?.Invoke(new GridPos(cx, cy), radius);
    }

    public  bool InBoundsPublic(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    private bool InBounds(int x, int y)       => InBoundsPublic(x, y);
    private int  Idx(int x, int y)      => y * Width + x;

    // ════════════════════════════════════════════════════════════
    //  IWorldInterface 實作
    // ════════════════════════════════════════════════════════════

    public WorldEntity? GetEntityAt(GridPos pos) =>
        _entities.FirstOrDefault(e => e.IsAlive && e.Position == pos);

    public MaterialType GetMaterialAt(GridPos pos) => TypeAt(pos.X, pos.Y);

    public List<WorldEntity> GetEntitiesNear(GridPos pos, float radius) =>
        _entities.Where(e => e.IsAlive && e.Position.DistanceTo(pos) <= radius).ToList();

    public object? GetEntityProperty(WorldEntity entity, string property) => property switch
    {
        "hp"       => entity.Hp,
        "maxHp"    => entity.MaxHp,
        "faction"  => entity.Faction,
        "position" => entity.Position,
        _          => null,
    };

    public void DestroyTile(GridPos pos)
    {
        if (!InBounds(pos.X, pos.Y)) return;
        var mat = TypeAt(pos.X, pos.Y);
        Set(pos.X, pos.Y, MaterialType.Air);
        OnTileDestroyed?.Invoke(pos, mat);
    }

    public void ApplyForce(WorldEntity entity, float dx, float dy)
    {
        var np = entity.Position + new GridPos(Math.Sign((int)dx), Math.Sign((int)dy));
        if (TypeAt(np.X, np.Y) == MaterialType.Air)
            entity.Position = np;
    }

    public void SpawnEffect(string type, GridPos pos, Dictionary<string, object?> parameters)
    {
        switch (type)
        {
            case "fire":
                int fr = parameters.TryGetValue("radius", out var fr_) ? Convert.ToInt32(fr_) : 1;
                for (int dy = -fr; dy <= fr; dy++)
                for (int dx = -fr; dx <= fr; dx++)
                    if (TypeAt(pos.X + dx, pos.Y + dy) == MaterialType.Air)
                        SetFire(pos.X + dx, pos.Y + dy);
                break;

            case "explosion":
                int er = parameters.TryGetValue("radius", out var er_) ? Convert.ToInt32(er_) : 3;
                Explode(pos.X, pos.Y, er);
                break;

            case "water":
                int wr = parameters.TryGetValue("radius", out var wr_) ? Convert.ToInt32(wr_) : 2;
                for (int dy = -wr; dy <= wr; dy++)
                for (int dx = -wr; dx <= wr; dx++)
                    if (dx*dx + dy*dy <= wr*wr && TypeAt(pos.X+dx, pos.Y+dy) == MaterialType.Air)
                        Set(pos.X + dx, pos.Y + dy, MaterialType.Water);
                break;
        }
    }

    public void SetEntityProperty(WorldEntity entity, string property, object? value)
    {
        if (property == "hp")
        {
            float old = entity.Hp;
            entity.Hp = Math.Max(0f, Convert.ToSingle(value));
            if (entity.Hp < old) OnEntityHit?.Invoke(entity, entity, old - entity.Hp);
            if (entity.Hp <= 0f && old > 0f) OnEntityDied?.Invoke(entity);
        }
    }

    public WorldEntity? CreateEntity(string type, GridPos pos, Dictionary<string, object?> parameters)
    {
        float maxHp = parameters.TryGetValue("maxHp", out var hp) ? Convert.ToSingle(hp) : 50f;
        string faction = parameters.TryGetValue("faction", out var f) ? f?.ToString() ?? "enemy" : "enemy";
        var e = new WorldEntity(_nextEntityId++, pos, maxHp, faction);
        _entities.Add(e);
        return e;
    }
}

// ── 世界實體 ──────────────────────────────────────────────────────

public class WorldEntity
{
    public int Id { get; }
    public GridPos Position { get; set; }
    public float Hp { get; set; }
    public float MaxHp { get; }
    public string Faction { get; }
    public bool IsAlive => Hp > 0f;

    public WorldEntity(int id, GridPos pos, float maxHp, string faction)
    {
        Id = id; Position = pos; MaxHp = maxHp; Hp = maxHp; Faction = faction;
    }
}
