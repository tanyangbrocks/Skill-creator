namespace SkillCreator.World;

using Godot;
using SkillCreator.World.Materials;

/// <summary>
/// 3D 地圖生成器，對應 <see cref="TileWorld3D"/>。
/// Phase 2-C：改為 instance class，支援懶加載 chunk 生成。
/// 啟動只生成 Z=0 初始 strip；玩家移動時 EnsureChunksGenerated 按需補生成。
/// </summary>
public class MapGenerator3D
{
    // ── 生成點 ─────────────────────────────────────────────────────────────
    public struct SpawnData
    {
        public GridPos PlayerSpawn;
        public List<(GridPos Pos, EnemyType Type)> EnemySpawns;
    }

    private static readonly (int dx, int dy, int dz)[] _dirs6 =
    {
        ( 0, +1,  0), (-1,  0,  0), ( 1,  0,  0),
        ( 0, -1,  0), ( 0,  0, -1), ( 0,  0, +1),
    };

    // ── 懶加載狀態（Generate 後有效）──────────────────────────────────────
    private int[,]? _heights;                              // 出生區高度圖 [initW, initD]
    // G-1: 地形參數（一次初始化，供 GetHeightAt 任意 (x,z) 確定性計算）
    private float _hp1, _hp2, _hp3, _hp4;
    private int _worldSeed, _worldW, _worldH, _worldD;
    private readonly HashSet<Vector3I> _generatedChunks = new();

    // ── 主入口：只生成初始 Z strip ─────────────────────────────────────────

    public SpawnData Generate(TileWorld3D world, int seed = 12345)
    {
        var rng  = new Random(seed);
        int W = world.Width, H = world.Height, D = world.Depth;
        // G-0: 初始只生成出生區 3 chunks 寬 × 1 chunk 深，其餘懶加載
        int initW = Math.Min(W, Chunk3D.Size * 3);  // 48 tiles
        int initD = Math.Min(D, Chunk3D.Size);       // 16 tiles

        // G-1: 儲存地形參數供 GetHeightAt 確定性查詢任意 (x,z)
        _worldSeed = seed; _worldW = W; _worldH = H; _worldD = D;
        _hp1 = rng.NextSingle() * MathF.Tau;
        _hp2 = rng.NextSingle() * MathF.Tau;
        _hp3 = rng.NextSingle() * MathF.Tau;
        _hp4 = rng.NextSingle() * MathF.Tau;

        // 出生區高度圖（initW×initD）
        _heights = GenerateHeightmap(initW, initD);

        FillAll(world, initW, H, initD, MaterialType.Stone);
        ApplyHeightmap(world, _heights, initW, H, initD);

        var caves = GenerateCaCaves(initW, H, initD, _heights, rng);
        ApplyCaves(world, caves, _heights, initW, H, initD);
        EnsureWalkableCaves(world, _heights, initW, H, initD);

        var surfaceEntry = EnsureConnectivity(world, _heights, initW, H, initD, rng);
        SealBedrock(world, initW, H, initD);
        PlaceOreVeins(world, _heights, initW, H, initD, rng);
        AddDecor(world, _heights, initW, H, initD, rng);

        // 標記初始已生成的 chunks（僅出生區 3×all×1）
        for (int cz = 0; cz < CeilDiv(initD, Chunk3D.Size); cz++)
        for (int cy = 0; cy < CeilDiv(H,     Chunk3D.Size); cy++)
        for (int cx = 0; cx < CeilDiv(initW, Chunk3D.Size); cx++)
            _generatedChunks.Add(new Vector3I(cx, cy, cz));

        return BuildSpawns(world, _heights, surfaceEntry, initW, H, initD, rng);
    }

    // ── 懶加載：每幀由 Main._Process 呼叫 ──────────────────────────────────

    /// <summary>
    /// 確保玩家 chunk 座標附近的所有 chunk 都已生成。
    /// 每幀最多生成 maxPerCall 個，避免卡幀。
    /// </summary>
    public void EnsureChunksGenerated(
        TileWorld3D world, int cx, int cy, int cz, int radius, int maxPerCall = 4)
    {
        if (_heights == null) return;
        int W = world.Width, H = world.Height, D = world.Depth;
        int maxCX = CeilDiv(W, Chunk3D.Size) - 1;
        int maxCY = CeilDiv(H, Chunk3D.Size) - 1;
        int maxCZ = CeilDiv(D, Chunk3D.Size) - 1;
        int generated = 0;

        for (int dz = 0; dz <= radius && generated < maxPerCall; dz++)
        for (int dy = -radius; dy <= radius && generated < maxPerCall; dy++)
        for (int dx = -radius; dx <= radius && generated < maxPerCall; dx++)
        {
            // 優先生成 Z+ 方向（玩家按 W 前進），再生成 Z-
            foreach (int sz in dz == 0 ? new[] { 0 } : new[] { 1, -1 })
            {
                var coord = new Vector3I(cx + dx, cy + dy, cz + dz * sz);
                if (coord.X < 0 || coord.Y < 0 || coord.Z < 0) continue;
                if (coord.X > maxCX || coord.Y > maxCY || coord.Z > maxCZ) continue;
                if (!_generatedChunks.Add(coord)) continue; // 已生成
                GenerateChunkLazy(world, coord);
                if (++generated >= maxPerCall) return;
            }
        }
    }

    /// <summary>
    /// G-1: 確定性地形高度查詢，任意 (x,z) 皆可呼叫，不依賴預生成陣列。
    /// 結果只取決於世界 seed 與座標，chunk 邊界天衣無縫。
    /// </summary>
    public int GetHeightAt(int x, int z)
    {
        float fx = (float)x / _worldW, fz = (float)z / _worldD;
        float n = (HeightHash(x, z, _worldSeed) / (float)0x7fff_ffff - 0.5f) * (_worldH * 0.012f);
        float raw = _worldH * 0.32f
            + MathF.Sin(fx * 2 * MathF.PI + _hp1) * (_worldH * 0.05f)
            + MathF.Sin(fz * 3 * MathF.PI + _hp2) * (_worldH * 0.04f)
            + MathF.Sin(fx * 7 * MathF.PI + _hp3) * (_worldH * 0.025f)
            + MathF.Sin(fz * 5 * MathF.PI + _hp4) * (_worldH * 0.02f)
            + n;
        return Math.Clamp((int)raw, (int)(_worldH * 0.20f), (int)(_worldH * 0.45f));
    }

    private static int HeightHash(int x, int z, int seed)
    {
        int h = x * 1664525 + z * 1013904223 + seed * 22695477;
        h ^= h >> 16;
        h *= unchecked((int)0x45d9f3b);
        h ^= h >> 16;
        return h & 0x7fff_ffff;
    }

    private void GenerateChunkLazy(TileWorld3D world, Vector3I coord)
    {
        const int S = Chunk3D.Size;
        int wx0 = coord.X * S, wy0 = coord.Y * S, wz0 = coord.Z * S;
        int W = world.Width, H = world.Height, D = world.Depth;

        for (int lx = 0; lx < S; lx++)
        for (int lz = 0; lz < S; lz++)
        {
            int wx = wx0 + lx, wz = wz0 + lz;
            if ((uint)wx >= (uint)W || (uint)wz >= (uint)D) continue;
            int h = GetHeightAt(wx, wz);  // G-1: 確定性查詢，不限 spawn 區

            for (int ly = 0; ly < S; ly++)
            {
                int wy = wy0 + ly;
                if ((uint)wy >= (uint)H || wy < h) continue;
                var mat = wy <= h + 2 ? MaterialType.Dirt : MaterialType.Stone;
                world.SetTile(wx, wy, wz, mat);
            }
        }
    }

    private static int CeilDiv(int a, int b) => (a + b - 1) / b;

    // ════════════════════════════════════════════════════════════
    //  Step 1 — 全填石
    // ════════════════════════════════════════════════════════════

    private static void FillAll(TileWorld3D world, int W, int H, int D, MaterialType mat)
    {
        for (int y = 0; y < H; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
            world.SetTile(x, y, z, mat);
    }

    // ════════════════════════════════════════════════════════════
    //  Step 2 — 高度圖（XZ 2D，each column has one surface Y）
    // ════════════════════════════════════════════════════════════

    // G-1: 改為 instance method，直接呼叫 GetHeightAt（不再用 rng / smoothing）
    private int[,] GenerateHeightmap(int W, int D)
    {
        var heights = new int[W, D];
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
            heights[x, z] = GetHeightAt(x, z);
        return heights;
    }

    private static void ApplyHeightmap(TileWorld3D world, int[,] heights, int W, int H, int D)
    {
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
        {
            int sy = heights[x, z];
            for (int y = 0; y < sy; y++)
                world.SetTile(x, y, z, MaterialType.Air);
            world.SetTile(x, sy,     z, MaterialType.Dirt);
            if (sy + 1 < H) world.SetTile(x, sy + 1, z, MaterialType.Dirt);
            if (sy + 2 < H) world.SetTile(x, sy + 2, z, MaterialType.Dirt);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 3 — 3D CA 洞穴（3×3×3 Moore 鄰域，threshold 14/27）
    // ════════════════════════════════════════════════════════════

    private static bool[,,] GenerateCaCaves(int W, int H, int D, int[,] heights, Random rng)
    {
        int caveTop = MaxHeight(heights, W, D) + 3;
        var cells   = new bool[W, H, D]; // true = 空氣

        for (int y = caveTop; y < H - 8; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
            cells[x, y, z] = rng.NextSingle() < 0.55f;

        var buf = new bool[W, H, D];
        for (int step = 0; step < 4; step++)
        {
            SmoothCa3D(cells, buf, W, H, D, caveTop, threshold: 18);
            (cells, buf) = (buf, cells);
        }
        SmoothCa3D(cells, buf, W, H, D, caveTop, threshold: 15);
        return buf;
    }

    private static void SmoothCa3D(
        bool[,,] src, bool[,,] dst, int W, int H, int D, int caveTop, int threshold)
    {
        for (int y = caveTop; y < H - 8; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            int stoneN = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (nx < 0 || nx >= W || ny < 0 || ny >= H || ny < caveTop || nz < 0 || nz >= D)
                    stoneN++;
                else if (!src[nx, ny, nz])
                    stoneN++;
            }
            dst[x, y, z] = stoneN < threshold;
        }
    }

    private static void ApplyCaves(TileWorld3D world, bool[,,] caves, int[,] heights, int W, int H, int D)
    {
        for (int y = 0; y < H; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
            if (caves[x, y, z] && y > heights[x, z] + 2)
                world.SetTile(x, y, z, MaterialType.Air);
    }

    // 保證每個洞穴地板（air above solid）有 PlayerH+4 格垂直淨空，讓玩家能站立行走
    private static void EnsureWalkableCaves(TileWorld3D world, int[,] heights, int W, int H, int D)
    {
        const int minClear = WorldScale.PlayerH + 4;
        int bedrock = H - 8;

        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            int caveBase = heights[x, z] + 3;
            for (int y = caveBase; y < bedrock - 1; y++)
            {
                if (world.GetTile(x, y, z) != MaterialType.Air) continue;
                if (world.GetTile(x, y + 1, z) == MaterialType.Air) continue; // not a floor

                // Count air running upward from floor tile y
                int clear = 1;
                for (int up = y - 1; up >= caveBase && world.GetTile(x, up, z) == MaterialType.Air; up--)
                    clear++;

                if (clear < minClear)
                {
                    int topAir = y - (clear - 1); // smallest-Y air tile in segment
                    int toCarve = minClear - clear;
                    for (int i = 1; i <= toCarve; i++)
                    {
                        int ty = topAir - i;
                        if (ty < caveBase) break;
                        world.SetTile(x, ty, z, MaterialType.Air);
                    }
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 4 — 連通性保證（6-鄰接 FloodFill，以 initD 為 Z 邊界）
    // ════════════════════════════════════════════════════════════

    private static GridPos EnsureConnectivity(
        TileWorld3D world, int[,] heights, int W, int H, int D, Random rng)
    {
        int midX = W / 2, midZ = 0;
        int spawnY   = Math.Max(0, heights[midX, midZ] - WorldScale.PlayerH);
        var start    = new GridPos(midX, spawnY, midZ);
        var visited  = FloodFill3D(world, start, W, H, D); // D = initD，邊界安全
        int caveDeep = MaxHeight(heights, W, D) + 8;

        int zFrom = Math.Max(0, midZ - D / 4);
        int zTo   = Math.Min(D, midZ + D / 4);
        for (int x = midX - W / 4; x < midX + W / 4; x++)
        for (int z = zFrom; z < zTo; z++)
        {
            for (int y = caveDeep; y < H - 10; y++)
            {
                if (world.GetTile(x, y, z) == MaterialType.Air &&
                    !visited.Contains(new GridPos(x, y, z)))
                {
                    for (int sy = heights[x, z]; sy <= y; sy++)
                    for (int dx = -2; dx <= 2; dx++)
                        world.SetTile(x + dx, sy, z, MaterialType.Air);
                    break;
                }
            }
        }
        return start;
    }

    private static HashSet<GridPos> FloodFill3D(TileWorld3D world, GridPos start, int W, int H, int D)
    {
        var visited = new HashSet<GridPos>();
        if (world.GetTile(start.X, start.Y, start.Z) != MaterialType.Air) return visited;

        var queue = new Queue<GridPos>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            foreach (var (dx, dy, dz) in _dirs6)
            {
                var n = new GridPos(pos.X + dx, pos.Y + dy, pos.Z + dz);
                // 使用傳入的 W/H/D 做邊界（initD 時 D < world.Depth，防止洪水漫入未生成區）
                if ((uint)n.X >= (uint)W || (uint)n.Y >= (uint)H || (uint)n.Z >= (uint)D) continue;
                if (world.GetTile(n.X, n.Y, n.Z) != MaterialType.Air) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    // ════════════════════════════════════════════════════════════
    //  Step 5 — 底部岩床
    // ════════════════════════════════════════════════════════════

    private static void SealBedrock(TileWorld3D world, int W, int H, int D)
    {
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
        for (int y = H - 8; y < H; y++)
            world.SetTile(x, y, z, MaterialType.Stone);
    }

    // ════════════════════════════════════════════════════════════
    //  Step 6 — 礦脈（6-鄰接 BFS blob）
    // ════════════════════════════════════════════════════════════

    private static void PlaceOreVeins(TileWorld3D world, int[,] heights, int W, int H, int D, Random rng)
    {
        int surfaceBase = MaxHeight(heights, W, D) + 5;

        var configs = new (MaterialType Mat, int YMin, int YMax, int Count, int MaxSize)[]
        {
            (MaterialType.CoalOre,         Math.Max(surfaceBase, (int)(H * 0.28f)), (int)(H * 0.62f), 180, 9),
            (MaterialType.CopperOre,       (int)(H * 0.44f), (int)(H * 0.78f), 120, 6),
            (MaterialType.IronOre,         (int)(H * 0.58f), (int)(H * 0.90f),  80, 5),
            (MaterialType.MagicCrystalOre, (int)(H * 0.74f), (int)(H * 0.95f),  40, 3),
        };

        foreach (var (mat, yMin, yMax, count, maxSize) in configs)
        {
            for (int i = 0; i < count; i++)
            {
                int sx = rng.Next(1, W - 1);
                int sy = rng.Next(yMin, Math.Min(yMax, H - 2));
                int sz = rng.Next(1, D - 1);
                if (world.GetTile(sx, sy, sz) == MaterialType.Stone)
                    PlaceOreBlob3D(world, sx, sy, sz, mat, maxSize, W, H, D, rng);
            }
        }
    }

    private static void PlaceOreBlob3D(TileWorld3D world, int sx, int sy, int sz,
        MaterialType mat, int maxSize, int W, int H, int D, Random rng)
    {
        world.SetTile(sx, sy, sz, mat);
        int placed = 1;
        var queue = new Queue<GridPos>();
        queue.Enqueue(new GridPos(sx, sy, sz));

        while (queue.Count > 0 && placed < maxSize)
        {
            var pos = queue.Dequeue();
            foreach (var (dx, dy, dz) in _dirs6)
            {
                if (placed >= maxSize) break;
                int nx = pos.X + dx, ny = pos.Y + dy, nz = pos.Z + dz;
                if (!world.InBounds(nx, ny, nz)) continue;
                if (world.GetTile(nx, ny, nz) != MaterialType.Stone) continue;
                if (rng.NextSingle() < 0.65f)
                {
                    world.SetTile(nx, ny, nz, mat);
                    queue.Enqueue(new GridPos(nx, ny, nz));
                    placed++;
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 7 — 裝飾（石鐘乳 + 水坑）
    // ════════════════════════════════════════════════════════════

    private static void AddDecor(TileWorld3D world, int[,] heights, int W, int H, int D, Random rng)
    {
        int caveTop = MaxHeight(heights, W, D) + 3;

        for (int y = caveTop + 1; y < H - 8; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            if (world.GetTile(x, y,     z) != MaterialType.Air)  continue;
            if (world.GetTile(x, y - 1, z) != MaterialType.Stone) continue;
            if (rng.NextSingle() >= 0.04f) continue;

            int len = rng.Next(1, 4);
            for (int i = 0; i < len; i++)
            {
                if (y + i >= H - 8) break;
                if (world.GetTile(x, y + i, z) != MaterialType.Air) break;
                world.SetTile(x, y + i, z, MaterialType.Stone);
            }
        }

        for (int y = caveTop; y < H - 9; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            if (world.GetTile(x, y,     z) != MaterialType.Air)   continue;
            if (world.GetTile(x, y + 1, z) != MaterialType.Stone)  continue;
            if (rng.NextSingle() >= 0.06f) continue;

            int poolR = rng.Next(1, 3);
            for (int pz = z - poolR; pz <= z + poolR; pz++)
            for (int px = x - poolR; px <= x + poolR; px++)
            {
                if (!world.InBounds(px, y, pz)) continue;
                if (world.GetTile(px, y, pz) == MaterialType.Air)
                    world.SetTile(px, y, pz, MaterialType.Water);
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 8 — 生成點
    // ════════════════════════════════════════════════════════════

    private static SpawnData BuildSpawns(TileWorld3D world, int[,] heights, GridPos surfaceEntry,
        int W, int H, int D, Random rng)
    {
        int caveMin = MaxHeight(heights, W, D) + 4;

        GridPos caveStart = FindCaveStart3D(world, heights, W, H, D);
        var caveArea = world.InBounds(caveStart.X, caveStart.Y, caveStart.Z)
            ? FloodFill3D(world, caveStart, W, H, D)
            : new HashSet<GridPos>();

        var floors = caveArea
            .Where(p => p.Y > caveMin
                     && world.GetTile(p.X, p.Y + 1, p.Z) == MaterialType.Stone)
            .OrderBy(p => p.X * 1000 + p.Z)
            .ToList();

        var types = new[] { EnemyType.Patrol, EnemyType.Melee, EnemyType.Ranged,
                            EnemyType.Melee,  EnemyType.Heavy };
        var enemySpawns = new List<(GridPos, EnemyType)>();

        if (floors.Count > 0)
        {
            int count = Math.Min(types.Length, floors.Count);
            int seg   = floors.Count / count;
            for (int i = 0; i < count; i++)
            {
                int idx = Math.Min(i * seg + rng.Next(0, Math.Max(1, seg)), floors.Count - 1);
                enemySpawns.Add((floors[idx], types[i]));
            }
        }

        return new SpawnData { PlayerSpawn = surfaceEntry, EnemySpawns = enemySpawns };
    }

    private static GridPos FindCaveStart3D(TileWorld3D world, int[,] heights, int W, int H, int D)
    {
        int scanFrom = MaxHeight(heights, W, D) + 8;
        for (int y = scanFrom; y < H - 10; y++)
        for (int x = W / 4; x < 3 * W / 4; x++)
        for (int z = D / 4; z < 3 * D / 4; z++)
            if (world.GetTile(x, y, z) == MaterialType.Air)
                return new GridPos(x, y, z);
        return new GridPos(-1, -1, -1);
    }

    // ── 輔助 ──────────────────────────────────────────────────────────────

    private static int MaxHeight(int[,] heights, int W, int D)
    {
        int max = 0;
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
            if (heights[x, z] > max) max = heights[x, z];
        return max;
    }
}
