namespace SkillCreator.World;

using SkillCreator.World.Materials;

/// <summary>
/// Phase 1：3D 地圖生成器，對應 <see cref="TileWorld3D"/>。
/// 策略：XZ 高度圖定義地表，Y 軸 CA 洞穴向下延伸，6-鄰接 FloodFill 保證連通性。
/// 完全不動原有 <see cref="MapGenerator"/>（2D 遊戲仍照常運作）。
/// </summary>
public static class MapGenerator3D
{
    // ── 生成點（複用 MapGenerator.SpawnData，GridPos 已有 Z 欄位）──
    public struct SpawnData
    {
        public GridPos PlayerSpawn;
        public List<(GridPos Pos, EnemyType Type)> EnemySpawns;
    }

    // 6-鄰接方向（與 TileWorld3D._neighbors6 一致）
    private static readonly (int dx, int dy, int dz)[] _dirs6 =
    {
        ( 0, +1,  0), (-1,  0,  0), ( 1,  0,  0),
        ( 0, -1,  0), ( 0,  0, -1), ( 0,  0, +1),
    };

    // ── 主入口 ────────────────────────────────────────────────────────────

    public static SpawnData Generate(TileWorld3D world, int seed = 12345)
    {
        var rng = new Random(seed);
        int W = world.Width, H = world.Height, D = world.Depth;

        FillAll(world, W, H, D, MaterialType.Stone);

        int[,] heights = GenerateHeightmap(W, D, H, rng);
        ApplyHeightmap(world, heights, W, H, D);

        bool[,,] caves = GenerateCaCaves(W, H, D, heights, rng);
        ApplyCaves(world, caves, heights, W, H, D);

        GridPos surfaceEntry = EnsureConnectivity(world, heights, W, H, D, rng);

        SealBedrock(world, W, H, D);
        PlaceOreVeins(world, heights, W, H, D, rng);
        AddDecor(world, heights, W, H, D, rng);

        return BuildSpawns(world, heights, surfaceEntry, W, H, D, rng);
    }

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

    private static int[,] GenerateHeightmap(int W, int D, int H, Random rng)
    {
        float baseY  = H * 0.32f;
        float p1 = rng.NextSingle() * MathF.Tau, p2 = rng.NextSingle() * MathF.Tau;
        float p3 = rng.NextSingle() * MathF.Tau, p4 = rng.NextSingle() * MathF.Tau;

        var raw = new float[W, D];
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
        {
            float fx = (float)x / W, fz = (float)z / D;
            raw[x, z] = baseY
                + MathF.Sin(fx * 2 * MathF.PI + p1) * (H * 0.05f)
                + MathF.Sin(fz * 3 * MathF.PI + p2) * (H * 0.04f)
                + MathF.Sin(fx * 7 * MathF.PI + p3) * (H * 0.025f)
                + MathF.Sin(fz * 5 * MathF.PI + p4) * (H * 0.02f)
                + (rng.NextSingle() - 0.5f) * (H * 0.012f);
        }

        // X 軸 + Z 軸各平滑 3 次
        for (int pass = 0; pass < 3; pass++)
        {
            for (int z = 0; z < D; z++)
            for (int x = 1; x < W - 1; x++)
                raw[x, z] = (raw[x-1, z] + raw[x, z] + raw[x+1, z]) / 3f;
            for (int x = 0; x < W; x++)
            for (int z = 1; z < D - 1; z++)
                raw[x, z] = (raw[x, z-1] + raw[x, z] + raw[x, z+1]) / 3f;
        }

        int yMin = (int)(H * 0.20f), yMax = (int)(H * 0.45f);
        var heights = new int[W, D];
        for (int x = 0; x < W; x++)
        for (int z = 0; z < D; z++)
            heights[x, z] = Math.Clamp((int)raw[x, z], yMin, yMax);

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
            cells[x, y, z] = rng.NextSingle() < 0.45f;

        var buf = new bool[W, H, D];
        for (int step = 0; step < 4; step++)
        {
            SmoothCa3D(cells, buf, W, H, D, caveTop, threshold: 14);
            (cells, buf) = (buf, cells);
        }
        SmoothCa3D(cells, buf, W, H, D, caveTop, threshold: 12);
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

    // ════════════════════════════════════════════════════════════
    //  Step 4 — 連通性保證（6-鄰接 FloodFill）
    // ════════════════════════════════════════════════════════════

    private static GridPos EnsureConnectivity(
        TileWorld3D world, int[,] heights, int W, int H, int D, Random rng)
    {
        int midX = W / 2, midZ = D / 2;
        int spawnY   = Math.Max(0, heights[midX, midZ] - 1);
        var start    = new GridPos(midX, spawnY, midZ);
        var visited  = FloodFill3D(world, start, W, H, D);
        int caveDeep = MaxHeight(heights, W, D) + 8;

        for (int x = midX - W / 4; x < midX + W / 4; x++)
        for (int z = midZ - D / 4; z < midZ + D / 4; z++)
        {
            for (int y = caveDeep; y < H - 10; y++)
            {
                if (world.GetTile(x, y, z) == MaterialType.Air &&
                    !visited.Contains(new GridPos(x, y, z)))
                {
                    // 往上打豎井
                    for (int sy = heights[x, z]; sy <= y; sy++)
                    for (int dx = -1; dx <= 1; dx++)
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
                if (!world.InBounds(n.X, n.Y, n.Z)) continue;
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

        // 石鐘乳：天花板（上方為石）向下滴
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

        // 水坑：洞穴地板（本身 Air，下方 Stone）
        for (int y = caveTop; y < H - 9; y++)
        for (int z = 0; z < D; z++)
        for (int x = 0; x < W; x++)
        {
            if (world.GetTile(x, y,     z) != MaterialType.Air)   continue;
            if (world.GetTile(x, y + 1, z) != MaterialType.Stone)  continue;
            if (rng.NextSingle() >= 0.06f) continue;

            // 在 XZ 平面小範圍填水
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
