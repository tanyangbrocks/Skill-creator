namespace SkillCreator.World;

using SkillCreator.World.Materials;

// Heightmap + Cellular Automata 混合地圖生成器
public static class MapGenerator
{
    public struct SpawnData
    {
        public GridPos PlayerSpawn;
        public List<(GridPos Pos, EnemyType Type)> EnemySpawns;
    }

    public static SpawnData Generate(TileWorld world, int seed = 12345)
    {
        var rng = new Random(seed);
        int W = world.Width, H = world.Height;

        // 1. 全部填石
        FillAll(world, W, H, MaterialType.Stone);

        // 2. Heightmap → 塑形地表
        int[] heights = GenerateHeightmap(W, H, rng);
        ApplyHeightmap(world, heights, W, H);

        // 3. CA → 挖掘地下洞穴
        bool[,] caves = GenerateCaCaves(W, H, heights, rng);
        ApplyCaves(world, caves, heights, W, H);

        // 4. 修補：確保洞穴與地表有至少一條通路
        GridPos surfaceEntry = EnsureConnectivity(world, heights, W, H, rng);

        // 5. 底部岩床
        SealBedrock(world, W, H);

        // 6. 裝飾
        AddDecor(world, heights, W, H, rng);

        // 7. 生成點
        return BuildSpawns(world, heights, surfaceEntry, W, H, rng);
    }

    // ════════════════════════════════════════════════════════════
    //  Step 1 — 輔助
    // ════════════════════════════════════════════════════════════

    private static void FillAll(TileWorld world, int W, int H, MaterialType mat)
    {
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            world.Set(x, y, mat);
    }

    // ════════════════════════════════════════════════════════════
    //  Step 2 — Heightmap
    // ════════════════════════════════════════════════════════════

    private static int[] GenerateHeightmap(int W, int H, Random rng)
    {
        float baseY  = H * 0.32f;
        float phase1 = rng.NextSingle() * MathF.Tau;
        float phase2 = rng.NextSingle() * MathF.Tau;
        float phase3 = rng.NextSingle() * MathF.Tau;

        var raw = new float[W];
        for (int x = 0; x < W; x++)
        {
            float fx = (float)x / W;
            raw[x] = baseY
                + MathF.Sin(fx * 2 * MathF.PI + phase1) * (H * 0.05f)
                + MathF.Sin(fx * 7 * MathF.PI + phase2) * (H * 0.025f)
                + MathF.Sin(fx * 17 * MathF.PI + phase3) * (H * 0.012f)
                + (rng.NextSingle() - 0.5f) * (H * 0.012f);
        }

        for (int pass = 0; pass < 3; pass++)
            for (int x = 1; x < W - 1; x++)
                raw[x] = (raw[x - 1] + raw[x] + raw[x + 1]) / 3f;

        var heights = new int[W];
        for (int x = 0; x < W; x++)
            heights[x] = Math.Clamp((int)raw[x], (int)(H * 0.20f), (int)(H * 0.45f));

        return heights;
    }

    private static void ApplyHeightmap(TileWorld world, int[] heights, int W, int H)
    {
        for (int x = 0; x < W; x++)
        {
            int sy = heights[x];
            // 地表以上 → 空氣
            for (int y = 0; y < sy; y++)
                world.Set(x, y, MaterialType.Air);
            // 地表 + 往下 2 格土壤層
            world.Set(x, sy,     MaterialType.Dirt);
            if (sy + 1 < H) world.Set(x, sy + 1, MaterialType.Dirt);
            if (sy + 2 < H) world.Set(x, sy + 2, MaterialType.Dirt);
            // sy+3 以下保留 Stone（FillAll 已填好）
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 3 — CA 洞穴
    // ════════════════════════════════════════════════════════════

    private static bool[,] GenerateCaCaves(int W, int H, int[] heights, Random rng)
    {
        int caveTop = heights.Max() + 3;
        var cells   = new bool[W, H]; // true = 空氣（洞穴）

        for (int y = caveTop; y < H - 8; y++)
        for (int x = 0; x < W; x++)
            cells[x, y] = rng.NextSingle() < 0.45f;

        // 4 輪標準平滑 + 1 輪激進平滑（填碎孤島）
        for (int step = 0; step < 4; step++)
            cells = SmoothCa(cells, W, H, caveTop, threshold: 5);
        cells = SmoothCa(cells, W, H, caveTop, threshold: 4);

        return cells;
    }

    private static bool[,] SmoothCa(bool[,] cells, int W, int H, int caveTop, int threshold)
    {
        var next = (bool[,])cells.Clone();
        for (int y = caveTop; y < H - 8; y++)
        for (int x = 0; x < W; x++)
        {
            int stoneN = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= W || ny < 0 || ny >= H || ny < caveTop)
                    stoneN++;
                else if (!cells[nx, ny])
                    stoneN++;
            }
            next[x, y] = stoneN < threshold;
        }
        return next;
    }

    private static void ApplyCaves(TileWorld world, bool[,] caves, int[] heights, int W, int H)
    {
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (caves[x, y] && y > heights[x] + 2)
                world.Set(x, y, MaterialType.Air);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 4 — 連通性保證
    // ════════════════════════════════════════════════════════════

    private static GridPos EnsureConnectivity(TileWorld world, int[] heights, int W, int H, Random rng)
    {
        int midX        = W / 2;
        int spawnY      = Math.Max(0, heights[midX] - 1);
        var start       = new GridPos(midX, spawnY);
        var visited     = FloodFill(world, start, W, H);
        int surfaceDeep = heights.Max() + 8;

        // 掃描中間半幅，找孤立洞穴格子，往上打豎井連通
        for (int x = midX - W / 4; x < midX + W / 4; x++)
        {
            for (int y = surfaceDeep; y < H - 10; y++)
            {
                if (world.TypeAt(x, y) == MaterialType.Air &&
                    !visited.Contains(new GridPos(x, y)))
                {
                    for (int sy = heights[x]; sy <= y; sy++)
                    for (int dx = -1; dx <= 1; dx++)
                        world.Set(x + dx, sy, MaterialType.Air);
                    break; // 每列只打一條豎井
                }
            }
        }

        return start;
    }

    private static HashSet<GridPos> FloodFill(TileWorld world, GridPos start, int W, int H)
    {
        var visited = new HashSet<GridPos>();
        if (world.TypeAt(start.X, start.Y) != MaterialType.Air) return visited;

        var queue = new Queue<GridPos>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            Span<(int dx, int dy)> dirs = stackalloc (int, int)[] { (-1,0),(1,0),(0,-1),(0,1) };
            foreach (var (dx, dy) in dirs)
            {
                var n = new GridPos(pos.X + dx, pos.Y + dy);
                if (n.X < 0 || n.X >= W || n.Y < 0 || n.Y >= H) continue;
                if (world.TypeAt(n.X, n.Y) != MaterialType.Air) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    // ════════════════════════════════════════════════════════════
    //  Step 5 — 底部岩床
    // ════════════════════════════════════════════════════════════

    private static void SealBedrock(TileWorld world, int W, int H)
    {
        for (int x = 0; x < W; x++)
        for (int y = H - 8; y < H; y++)
            world.Set(x, y, MaterialType.Stone);
    }

    private static void SealWalls(TileWorld world, int W, int H)
    {
        for (int y = 0; y < H; y++)
        {
            world.Set(0,     y, MaterialType.Stone);
            world.Set(W - 1, y, MaterialType.Stone);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 6 — 裝飾
    // ════════════════════════════════════════════════════════════

    private static void AddDecor(TileWorld world, int[] heights, int W, int H, Random rng)
    {
        int caveTop = heights.Max() + 3;

        // ── 石鐘乳：預先收集天花板位置，避免迴圈中寫入影響偵測 ──
        var ceilings = new List<(int x, int y)>();
        for (int y = caveTop + 1; y < H - 8; y++)
        for (int x = 0; x < W; x++)
            if (world.TypeAt(x, y) == MaterialType.Air &&
                world.TypeAt(x, y - 1) == MaterialType.Stone)
                ceilings.Add((x, y));

        foreach (var (x, y) in ceilings)
        {
            if (rng.NextSingle() >= 0.06f) continue;
            int len = rng.Next(1, 4);
            for (int i = 0; i < len; i++)
            {
                if (y + i >= H - 8) break;
                if (world.TypeAt(x, y + i) != MaterialType.Air) break;
                world.Set(x, y + i, MaterialType.Stone);
            }
        }

        // ── 水坑：洞穴地板（本身 Air，下方 Stone）────────────────
        for (int y = caveTop; y < H - 9; y++)
        for (int x = 0; x < W; x++)
        {
            if (world.TypeAt(x, y)     != MaterialType.Air)  continue;
            if (world.TypeAt(x, y + 1) != MaterialType.Stone) continue;
            if (rng.NextSingle() >= 0.10f) continue;

            int poolW = rng.Next(3, 7);
            for (int px = x; px < Math.Min(x + poolW, W); px++)
                if (world.TypeAt(px, y) == MaterialType.Air)
                    world.Set(px, y, MaterialType.Water);
        }

        // ── 木頭平台：洞穴淨高 ≥ 8 的寬敞空間 ───────────────────
        for (int x = 2; x < W - 2; x += 5) // 每 5 格採樣一次，避免過密
        for (int y = caveTop + 4; y < H - 12; y++)
        {
            if (world.TypeAt(x, y)     != MaterialType.Air)  continue;
            if (world.TypeAt(x, y + 1) != MaterialType.Stone) continue;

            int airH = 0;
            for (int ay = y - 1; ay >= caveTop; ay--)
            {
                if (world.TypeAt(x, ay) == MaterialType.Air) airH++;
                else break;
            }
            if (airH < 8 || rng.NextSingle() >= 0.25f) continue;

            int platW = rng.Next(4, 9);
            int platX = Math.Max(1, x - platW / 2);
            int platY = y - airH / 2;
            for (int px = platX; px < Math.Min(platX + platW, W - 1); px++)
                if (world.TypeAt(px, platY) == MaterialType.Air)
                    world.Set(px, platY, MaterialType.Wood);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Step 7 — 生成點
    // ════════════════════════════════════════════════════════════

    private static SpawnData BuildSpawns(TileWorld world, int[] heights, GridPos surfaceEntry,
        int W, int H, Random rng)
    {
        int caveMin = heights.Max() + 4;

        // 從洞穴區找一個起點，Flood-fill 取得最大連通洞穴
        GridPos caveStart = FindCaveStart(world, heights, W, H);
        var caveArea = (caveStart.X >= 0)
            ? FloodFill(world, caveStart, W, H)
            : new HashSet<GridPos>();

        // 收集洞穴地板格（本身 Air、下方 Stone、深度足夠）
        var floors = caveArea
            .Where(p => p.Y > caveMin
                     && world.TypeAt(p.X, p.Y + 1) == MaterialType.Stone)
            .OrderBy(p => p.X)
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

    private static GridPos FindCaveStart(TileWorld world, int[] heights, int W, int H)
    {
        int scanFrom = heights.Max() + 8;
        for (int y = scanFrom; y < H - 10; y++)
        for (int x = W / 4; x < 3 * W / 4; x++)
            if (world.TypeAt(x, y) == MaterialType.Air)
                return new GridPos(x, y);
        return new GridPos(-1, -1);
    }
}
