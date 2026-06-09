namespace SkillCreator.World;

using SkillCreator.World.Materials;

// BSP 地牢生成器：切割空間 → 雕刻房間 → L 型走廊連接 → 裝飾
public static class MapGenerator
{
    public struct SpawnData
    {
        public GridPos PlayerSpawn;
        public List<(GridPos Pos, EnemyType Type)> EnemySpawns;
    }

    private readonly struct Room
    {
        public readonly int X, Y, W, H;
        public Room(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public GridPos Center   => new(X + W / 2, Y + H / 2);
        public GridPos FloorMid => new(X + W / 2, Y + H - 1);  // 房間最底層（Air，下方是石）
    }

    public static SpawnData Generate(TileWorld world, int seed = 12345)
    {
        var rng = new Random(seed);
        int W = world.Width, H = world.Height;

        // 1. 全部填石
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            world.Set(x, y, MaterialType.Stone);

        // 2. BSP 切割 → 雕刻房間
        var rooms = new List<Room>();
        BspSplit(3, 3, W - 3, H - 10, rng, rooms, 0);
        foreach (var r in rooms)
            CarveRoom(world, r);

        // 3. L 型走廊連接相鄰房間
        for (int i = 0; i < rooms.Count - 1; i++)
            CarveCorridor(world, rooms[i].Center, rooms[i + 1].Center);

        // 4. 底部岩床（覆蓋走廊可能挖到的區域）
        for (int x = 0; x < W; x++)
        for (int y = H - 8; y < H; y++)
            world.Set(x, y, MaterialType.Stone);

        // 5. 裝飾
        AddDecor(world, rooms, rng);

        // 6. 生成點
        return BuildSpawns(rooms, rng, W, H);
    }

    // ── BSP 切割 ──────────────────────────────────────────────────

    private static void BspSplit(int x, int y, int w, int h, Random rng, List<Room> rooms, int depth)
    {
        const int MinW = 14, MinH = 9, MaxDepth = 3;

        bool canH = w >= MinW * 2 + 2;
        bool canV = h >= MinH * 2 + 2;

        if (depth >= MaxDepth || (!canH && !canV))
        {
            if (w >= MinW && h >= MinH)
            {
                int rw = rng.Next(MinW, Math.Min(w, 30) + 1);
                int rh = rng.Next(MinH, Math.Min(h, 18) + 1);
                int rx = x + rng.Next(0, Math.Max(1, w - rw));
                int ry = y + rng.Next(0, Math.Max(1, h - rh));
                rooms.Add(new Room(rx, ry, rw, rh));
            }
            return;
        }

        // 優先水平切（左右分）讓地圖多橫向延伸
        if (canH && (!canV || w >= h * 1.4f))
        {
            int split = x + rng.Next(MinW, w - MinW + 1);
            BspSplit(x,     y, split - x,     h, rng, rooms, depth + 1);
            BspSplit(split, y, x + w - split, h, rng, rooms, depth + 1);
        }
        else
        {
            int split = y + rng.Next(MinH, h - MinH + 1);
            BspSplit(x, y,     w, split - y,     rng, rooms, depth + 1);
            BspSplit(x, split, w, y + h - split, rng, rooms, depth + 1);
        }
    }

    private static void CarveRoom(TileWorld w, Room r)
    {
        for (int y = r.Y; y < r.Y + r.H; y++)
        for (int x = r.X; x < r.X + r.W; x++)
            w.Set(x, y, MaterialType.Air);
    }

    // ── L 型走廊（先橫後豎，各 3-4 格寬確保可通行）────────────────

    private static void CarveCorridor(TileWorld world, GridPos a, GridPos b)
    {
        int x1 = Math.Min(a.X, b.X), x2 = Math.Max(a.X, b.X);
        int y1 = Math.Min(a.Y, b.Y), y2 = Math.Max(a.Y, b.Y);

        // 橫段（沿 a.Y），高度 4 格（玩家 + 跳躍空間）
        for (int x = x1; x <= x2; x++)
        for (int dy = -1; dy <= 2; dy++)
            world.Set(x, a.Y + dy, MaterialType.Air);

        // 豎段（沿 b.X），寬度 3 格
        for (int y = y1; y <= y2; y++)
        for (int dx = -1; dx <= 1; dx++)
            world.Set(b.X + dx, y, MaterialType.Air);
    }

    // ── 裝飾 ──────────────────────────────────────────────────────

    private static void AddDecor(TileWorld world, List<Room> rooms, Random rng)
    {
        foreach (var r in rooms)
        {
            int floorY = r.Y + r.H - 1;

            // 高房間（≥14）中段加木頭平台
            if (r.H >= 14)
            {
                int midY = r.Y + r.H / 2;
                int pw   = rng.Next(4, 9);
                int px   = r.X + rng.Next(1, Math.Max(2, r.W - pw));
                for (int x = px; x < px + pw; x++)
                    world.Set(x, midY, MaterialType.Wood);
            }

            // 天花板沙堆（30% 機率）
            if (rng.Next(10) < 3)
            {
                int px = r.X + rng.Next(2, Math.Max(3, r.W - 2));
                int sz = rng.Next(2, 5);
                for (int i = 0; i < sz; i++)
                    world.Set(px + rng.Next(-1, 2), r.Y + 1, MaterialType.Sand);
            }

            // 地板水坑（25% 機率）
            if (rng.Next(4) == 0)
            {
                int pw2 = rng.Next(3, 7);
                int px2 = r.X + rng.Next(1, Math.Max(2, r.W - pw2));
                for (int x = px2; x < px2 + pw2; x++)
                    world.Set(x, floorY, MaterialType.Water);
            }

            // 泥土散落在地板
            for (int x = r.X; x < r.X + r.W; x++)
                if (rng.Next(5) == 0)
                    world.Set(x, floorY, MaterialType.Dirt);
        }
    }

    // ── 生成點 ────────────────────────────────────────────────────

    private static SpawnData BuildSpawns(List<Room> rooms, Random rng, int W, int H)
    {
        if (rooms.Count == 0)
            return new SpawnData
            {
                PlayerSpawn  = new GridPos(W / 2, H - 10),
                EnemySpawns  = new(),
            };

        // 玩家：距中心最近的房間
        var sorted = rooms
            .OrderBy(r => Math.Abs(r.Center.X - W / 2) + Math.Abs(r.Center.Y - H / 2))
            .ToList();

        var playerRoom  = sorted[0];
        var playerSpawn = playerRoom.FloorMid;

        // 敵人：其餘房間，依左→右排列分配型別
        var others = sorted.Skip(1).OrderBy(r => r.Center.X).ToList();
        var types  = new[] { EnemyType.Patrol, EnemyType.Melee, EnemyType.Ranged, EnemyType.Melee, EnemyType.Heavy };

        var enemySpawns = new List<(GridPos, EnemyType)>();
        for (int i = 0; i < Math.Min(types.Length, others.Count); i++)
        {
            var room = others[i];
            var pos  = room.FloorMid + new GridPos(rng.Next(-2, 3), 0);
            enemySpawns.Add((pos, types[i]));
        }

        return new SpawnData { PlayerSpawn = playerSpawn, EnemySpawns = enemySpawns };
    }
}
