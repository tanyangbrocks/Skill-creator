namespace SkillCreator.World.Terrain;

using SkillCreator.World.Materials;

/// <summary>
/// 地形特徵：地表水池（碗型凹地 + 平靜水面）。
/// 中心最深（拋物面），邊緣逐漸與自然地表齊平；水面高度固定為一條水平線。
/// </summary>
public sealed class SurfaceWaterPool : TerrainFeature
{
    public override string Name => "地表水池";

    private sealed class Pool
    {
        public readonly int CX, CZ, Radius, MaxDepth;
        public int WaterSurface = -1;  // PlaceInWorld 後才固定；-1 = 未初始化
        public Pool(int cx, int cz, int r, int d) { CX = cx; CZ = cz; Radius = r; MaxDepth = d; }
    }

    private readonly List<Pool> _pools = new();
    private int _worldW, _worldD;

    // ── 生成參數（可調整）────────────────────────────────────────────────────
    private const int   CountMin   = 4;
    private const int   CountMax   = 9;
    private const int   RadiusMin  = 3;    // 最小半徑（遊戲單位，×Grain = tiles）
    private const int   RadiusMax  = 10;   // 最大半徑（遊戲單位，exclusive）
    private const int   DepthMin   = 10;   // 碗底最小深度（tile）
    private const int   DepthMax   = 50;   // 碗底最大深度（tile，exclusive）
    private const int   EdgeMargin = 32;
    // 水面在碗深的位置比例：0.0=碗底，1.0=碗口（滿水）
    private const float WaterFill  = 0.7f;

    // ── TerrainFeature 實作 ───────────────────────────────────────────────────

    public override void Initialize(int seed, int worldW, int worldH, int worldD)
    {
        _worldW = worldW; _worldD = worldD;
        _pools.Clear();

        var rng   = new Random(seed ^ unchecked((int)0xA5F3C2B1));
        int count = rng.Next(CountMin, CountMax);
        int G     = WorldScale.Grain;

        // 保底：第 0 號池距離出生點一定距離（池邊至少 64 tiles 外），讓玩家走幾步就能看到
        int baseR0 = rng.Next(4, 7) * G;
        int dist0  = baseR0 + 64;           // pool center 距 spawn = (radius + 64) tiles
        int spawnCX, spawnCZ;
        switch (rng.Next(4))                 // 四個基本方向隨機選一
        {
            case 0:  spawnCX = worldW / 2 + dist0; spawnCZ = worldD / 2;         break;
            case 1:  spawnCX = worldW / 2 - dist0; spawnCZ = worldD / 2;         break;
            case 2:  spawnCX = worldW / 2;         spawnCZ = worldD / 2 + dist0; break;
            default: spawnCX = worldW / 2;         spawnCZ = worldD / 2 - dist0; break;
        }
        spawnCX = Math.Clamp(spawnCX, EdgeMargin, worldW - EdgeMargin);
        spawnCZ = Math.Clamp(spawnCZ, EdgeMargin, worldD - EdgeMargin);
        _pools.Add(new Pool(spawnCX, spawnCZ, baseR0, rng.Next(DepthMin, DepthMax)));

        for (int i = 1; i < count; i++)
        {
            int cx     = rng.Next(EdgeMargin, worldW - EdgeMargin);
            int cz     = rng.Next(EdgeMargin, worldD - EdgeMargin);
            int radius = rng.Next(RadiusMin, RadiusMax) * G;
            int depth  = rng.Next(DepthMin, DepthMax);
            _pools.Add(new Pool(cx, cz, radius, depth));
        }
    }

    public override void Prepare(Func<int, int, int> getHeight)
    {
        // 以各池中心的自然高度固定水面 Y；Generate 和 InitTerrainParams 兩條路徑都必須呼叫
        foreach (var pool in _pools)
            pool.WaterSurface = getHeight(pool.CX, pool.CZ) - (int)(pool.MaxDepth * WaterFill);
    }

    public override void PlaceInWorld(TileWorld3D world, Func<int, int, int> getHeight, int initW, int initD)
    {
        foreach (var pool in _pools)
        {
            for (int dx = -pool.Radius; dx <= pool.Radius; dx++)
            for (int dz = -pool.Radius; dz <= pool.Radius; dz++)
            {
                long dist2 = (long)dx * dx + (long)dz * dz;
                if (dist2 > (long)pool.Radius * pool.Radius) continue;
                int wx = pool.CX + dx, wz = pool.CZ + dz;
                if ((uint)wx >= (uint)initW   || (uint)wz >= (uint)initD)   continue;
                if ((uint)wx >= (uint)_worldW  || (uint)wz >= (uint)_worldD) continue;

                int h = getHeight(wx, wz);
                PlaceTile(world, wx, wz, h, dist2, pool);
            }
        }
    }

    public override (int h, MaterialType mat)? GetSurfaceOverride(int wx, int wz, int naturalH)
    {
        foreach (var pool in _pools)
        {
            if (pool.WaterSurface <= 0) continue;
            int dx = wx - pool.CX, dz = wz - pool.CZ;
            long dist2 = (long)dx * dx + (long)dz * dz;
            if (dist2 > (long)pool.Radius * pool.Radius) continue;

            (int effectiveH, MaterialType mat) = BowlSurface(naturalH, dist2, pool);
            if (effectiveH < naturalH)
                return (effectiveH, mat);
        }
        return null;
    }

    // ── 內部工具 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 計算碗型拋物面在 dist² 位置的「有效地表高度」與材質。
    /// effectiveH < naturalH → 凹下；effectiveH >= naturalH → 不影響。
    /// </summary>
    private static (int effectiveH, MaterialType mat) BowlSurface(
        int naturalH, long dist2, Pool pool)
    {
        float t        = MathF.Sqrt(dist2) / pool.Radius;     // 0=中心 → 1=邊緣
        int bowlDepth  = (int)(pool.MaxDepth * (1f - t * t)); // 拋物面深度（tile）
        int floorY     = naturalH - bowlDepth;                 // 碗底 Y（較小 = 視覺較低）
        int waterSurface = pool.WaterSurface;

        if (bowlDepth <= 0) return (naturalH, MaterialType.Air);

        if (floorY <= waterSurface)
            return (waterSurface, MaterialType.Water);   // 水下：水面平齊
        else
            return (floorY, MaterialType.Dirt);           // 碗緣：Dirt 露出
    }

    private void PlaceTile(TileWorld3D world, int wx, int wz, int h, long dist2, Pool pool)
    {
        var (effectiveH, mat) = BowlSurface(h, dist2, pool);
        if (effectiveH >= h) return;  // 不在碗內，不做任何事

        if (mat == MaterialType.Water)
        {
            // 水下：把 waterSurface 到 h-1 的 Air 格填為 Water（水柱）
            // h 以上已是 Solid（初始 chunk），不需再設；h-1 以下已是 Air
            for (int wy = effectiveH; wy < h; wy++)
                world.SetTile(wx, wy, wz, MaterialType.Water);
        }
        else
        {
            // 碗緣：把 floorY 到 h-1 的 Air 格填為 Dirt（讓地表降低）
            for (int wy = effectiveH; wy < h; wy++)
                world.SetTile(wx, wy, wz, MaterialType.Dirt);
        }
    }
}
